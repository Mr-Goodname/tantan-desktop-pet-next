using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using BonbonPet.Core;
using BonbonPet.Services;
using Forms = System.Windows.Forms;

namespace BonbonPet;

public sealed class PetCoordinator : IDisposable
{
    private readonly List<PetWindow> _instances = new();
    private readonly HashSet<PetWindow> _animated = new();
    private readonly bool _testing;
    private Forms.NotifyIcon? _tray;
    private Task _saveTask = Task.CompletedTask;
    private bool _disposed, _rendering;
    private bool _inFrame;
    private PetWindow[] _renderRoster = Array.Empty<PetWindow>();
    private long _renderBucket = -1;
    private int _targetFps;
    private TimeSpan _lastFrame;
    public IReadOnlyList<PetWindow> Instances => _instances;
    internal AudioService AudioMeter { get; } = new(false);

    public PetCoordinator(bool testing = false) => _testing = testing;

    public PetWindow Start(IReadOnlyList<PetSettings> profiles)
    {
        foreach (var settings in profiles)
        {
            settings.Normalize(); settings.FirstRun = false;
            Create(settings, _instances.FirstOrDefault(pet => pet.Settings.CharacterPath == settings.CharacterPath));
        }
        if (_instances.Count == 0) Create(new PetSettings { FirstRun = false }, null);
        if (!_testing) CreateTray();
        System.Windows.Application.Current.MainWindow = _instances[0];
        return _instances[0];
    }

    private PetWindow Create(PetSettings settings, PetWindow? source)
    {
        var pet = new PetWindow(settings, _testing, this, source?.ForkSprite());
        _instances.Add(pet);
        try { pet.Show(); }
        catch { _instances.Remove(pet); pet.Dispose(); throw; }
        NotifyRosterChanged();
        return pet;
    }

    public PetWindow AddInstance(PetWindow? source = null, bool showSettings = true)
    {
        if (_instances.Count >= PetProfiles.MaximumInstances)
            throw new InvalidOperationException($"最多可创建 {PetProfiles.MaximumInstances} 个角色。");
        source ??= _instances.FirstOrDefault();
        var settings = source?.Settings.Copy() ?? new PetSettings();
        settings.InstanceId = Guid.NewGuid().ToString("N");
        int number = 1;
        while (_instances.Any(pet => pet.Settings.InstanceName == $"角色 {number}")) number++;
        settings.InstanceName = $"角色 {number}";
        settings.FirstRun = false;
        if (source is not null)
        {
            settings.CenterX = source.Physics.X + settings.Size * .8;
            settings.CenterY = source.Physics.Y - settings.Size * .4;
        }
        var pet = Create(settings, source);
        RequestSave();
        if (showSettings) pet.ShowSettings();
        return pet;
    }

    public bool RemoveInstance(PetWindow pet)
    {
        if (_instances.Count <= 1 || !_instances.Contains(pet)) return false;
        _instances.Remove(pet); _animated.Remove(pet);
        if (ReferenceEquals(System.Windows.Application.Current.MainWindow, pet)) System.Windows.Application.Current.MainWindow = _instances[0];
        pet.Dispose(); pet.Close();
        NotifyRosterChanged(); RefreshAudio(); RequestSave(flush: true);
        return true;
    }

    internal void OnPetClosed(PetWindow pet)
    {
        if (_disposed || !_instances.Contains(pet)) return;
        if (_instances.Count == 1) { Exit(); return; }
        RemoveInstance(pet);
    }

    internal void SetAnimated(PetWindow pet, bool active)
    {
        if (active) _animated.Add(pet); else _animated.Remove(pet);
        if (_animated.Count == 0 && _rendering)
        {
            CompositionTarget.Rendering -= Render; _rendering = false; _lastFrame = default; _renderBucket = -1;
        }
    }

    internal void EnsureRendering()
    {
        if (_rendering || _disposed) return;
        CompositionTarget.Rendering += Render;
        _rendering = true;
    }

    private void Render(object? sender, EventArgs args)
    {
        if (_disposed || _inFrame || _animated.Count == 0) return;
        var stamp = ((RenderingEventArgs)args).RenderingTime;
        if (stamp == _lastFrame) return;
        int targetFps = 20;
        foreach (var pet in _animated) targetFps = Math.Max(targetFps, pet.Settings.Fps);
        if (_targetFps != targetFps) { _targetFps = targetFps; _renderBucket = -1; }
        long bucket = (long)Math.Floor(stamp.TotalSeconds * targetFps + .000001);
        if (bucket == _renderBucket) return;
        _renderBucket = bucket;
        double elapsed = _lastFrame == default ? 1d / 60 : Math.Clamp((stamp - _lastFrame).TotalSeconds, 0, .1);
        _lastFrame = stamp;
        _inFrame = true;
        try
        {
            var roster = _renderRoster;
            foreach (var pet in roster) pet.CaptureCollisionStart();
            foreach (var pet in roster)
                if (_animated.Contains(pet)) pet.RenderCoordinated(sender, args, elapsed);
            ResolveCollisions(elapsed);
        }
        finally { _inFrame = false; }
    }

    internal void ResolveCollisions(double elapsedSeconds)
    {
        var roster = _renderRoster;
        for (int i = 0; i < roster.Length; i++)
        {
            var a = roster[i];
            if (!a.IsCollisionActive) continue;
            for (int j = i + 1; j < roster.Length; j++)
            {
                var b = roster[j];
                if (!b.IsCollisionActive || (!a.CanMoveForCollision && !b.CanMoveForCollision)) continue;
                if (PeerCollision.ResolveSwept(a.Physics, a.GetCollisionVertices(), a.CanMoveForCollision, a.PreviousCollisionCenter,
                    b.Physics, b.GetCollisionVertices(), b.CanMoveForCollision, b.PreviousCollisionCenter,
                    elapsedSeconds, Math.Pow(a.Settings.Size / 220, 2), Math.Pow(b.Settings.Size / 220, 2)))
                {
                    a.RefreshAfterPeerCollision(); b.RefreshAfterPeerCollision();
                }
            }
        }
    }

    internal void RefreshAudio()
    {
        if (_disposed) return;
        if (_instances.Any(pet => pet.IsAudioActive)) AudioMeter.Start(); else AudioMeter.Stop();
    }

    internal void NotifyRosterChanged()
    {
        _renderRoster = _instances.ToArray();
        foreach (var pet in _instances) pet.SettingsPanel?.RefreshInstances();
    }

    internal void RequestSave(bool flush = false)
    {
        if (_testing || _disposed || _instances.Count == 0) return;
        try
        {
            foreach (var pet in _instances) pet.CaptureSettingsPosition();
            string profiles = PetProfiles.Serialize(_instances.Select(pet => pet.Settings).ToArray());
            string primary = JsonSerializer.Serialize(_instances[0].Settings, new JsonSerializerOptions { WriteIndented = true });
            _saveTask = _saveTask.ContinueWith(_ =>
            {
                try
                {
                    Directory.CreateDirectory(PetSettings.DataDirectory);
                    WriteAtomic(PetProfiles.FilePath, profiles);
                    WriteAtomic(PetSettings.FilePath, primary);
                }
                catch (Exception ex) { App.Log(ex); }
            }, TaskScheduler.Default);
        }
        catch (Exception ex) { App.Log(ex); }
        if (flush) _saveTask.GetAwaiter().GetResult();
    }

    private static void WriteAtomic(string path, string json)
    {
        File.WriteAllText(path + ".tmp", json);
        File.Move(path + ".tmp", path, true);
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var pet in _instances)
            {
                var item = new Forms.ToolStripMenuItem(pet.Settings.InstanceName);
                item.DropDownItems.Add("打开设置", null, (_, _) => pet.Dispatcher.Invoke(pet.ShowSettings));
                item.DropDownItems.Add("显示 / 隐藏", null, (_, _) => pet.Dispatcher.Invoke(pet.ToggleVisibility));
                menu.Items.Add(item);
            }
            menu.Items.Add("创建角色", null, (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                try { AddInstance(); } catch (Exception ex) { MessageBox.Show(ex.Message, "创建角色"); }
            }));
            menu.Items.Add("显示全部角色", null, (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(() => { foreach (var pet in _instances) pet.RestoreVisibility(); }));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出程序", null, (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(Exit));
        };
        _tray = new Forms.NotifyIcon
        {
            Text = "弹弹桌宠 · Tantan · 双击打开设置",
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "bonbon.ico")),
            ContextMenuStrip = menu, Visible = true
        };
        _tray.DoubleClick += (_, _) => { if (_instances.Count > 0) _instances[0].Dispatcher.Invoke(_instances[0].ShowSettings); };
    }

    public void Exit()
    {
        if (_disposed) return;
        RequestSave(flush: true); Dispose(); System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_rendering) CompositionTarget.Rendering -= Render;
        _animated.Clear();
        foreach (var pet in _instances) { pet.Dispose(); pet.Close(); }
        _instances.Clear(); AudioMeter.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Icon?.Dispose(); _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
    }
}
