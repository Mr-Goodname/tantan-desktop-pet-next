using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BonbonPet.Controls;
using BonbonPet.Core;
using BonbonPet.Services;
using Forms = System.Windows.Forms;

namespace BonbonPet;

public partial class PetWindow : Window, IDisposable
{
    private readonly AudioService _audio;
    private readonly PetCoordinator? _coordinator;
    private static readonly ConditionalWeakTable<BitmapSource, SpriteOutline> OutlineCache = new();
    private Point[] _collisionVertices = Array.Empty<Point>();
    private readonly PetPhysics _physics = new();
    private bool _renderSubscribed;
    private long _lastRenderBucket = -1;
    private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
    private readonly Dictionary<BitmapSource, SpriteOutline> _outlines = new();
    private readonly PointerVelocity _pointerVelocity = new();
    private readonly PressSpring _pressSpring = new();
    private double _previewReleaseAt;
    private readonly List<double> _frameIntervals = new(400);
    private double _previousFrameTime;
    private double _spriteAdvanceAge;
    private double _previewNext;
    private bool _collectFrameIntervals;
    private readonly DispatcherTimer _housekeeping;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private SpriteAnimation _sprite;
    private SettingsWindow? _settingsWindow;
    private readonly BubbleWindow _bubble = new();
    private Forms.NotifyIcon? _tray;
    private IntPtr _handle;
    private double _scale = 1, _lastTime, _kickAge;
    private double _bubbleDeadline = 12, _bubbleUntil;
    private double _saveAfter = double.PositiveInfinity;
    private Task _settingsSaveTask = Task.CompletedTask;
    private static readonly JsonSerializerOptions SettingsJsonOptions = new() { WriteIndented = true };
    private double _configuredSpeed = double.NaN;
    private double _audioAverage, _beatPulse, _beatAge = 1, _swayDirection = 1;
    private double _placedLeft = double.NaN, _placedTop = double.NaN;
    private bool _placedTopmost;
    private bool _wasPhysics, _suspended, _manualHidden, _disposed, _grabbed, _dragged;
    private System.Drawing.Point _grabPoint;
    private double _grabX, _grabY;
    private BitmapSource? _hitFrame;
    private readonly bool _testing;
    private (double Left, double Top, double Right, double Bottom) _bounds;
    private string[] _phrases = Array.Empty<string>();
    public PetSettings Settings { get; }
    public BitmapSource CurrentFrame => _sprite.CurrentFrame;
    internal PetCoordinator? Coordinator => _coordinator;
    internal bool IsAudioActive => !_disposed && !_suspended && Settings.AudioEnabled;
    internal bool IsCollisionActive => !_disposed && !_suspended;
    internal bool CanMoveForCollision => Settings.PhysicsEnabled && !_grabbed;
    internal Point PreviousCollisionCenter { get; private set; }
    internal SpriteAnimation ForkSprite() => _sprite.Fork();
    internal void CaptureSettingsPosition() { Settings.CenterX = _physics.X; Settings.CenterY = _physics.Y; }
    internal void CaptureCollisionStart() => PreviousCollisionCenter = new Point(_physics.X, _physics.Y);
    internal ReadOnlySpan<Point> GetCollisionVertices()
    {
        var outline = _outlines[_sprite.CurrentFrame];
        int count = outline.CopyVertices(_collisionVertices, Settings.Size, Settings.Size, Squash.ScaleX, Squash.ScaleY,
            Spin.Angle, Bounce.Y, _scale, _physics.X, _physics.Y);
        return _collisionVertices.AsSpan(0, count);
    }
    internal void RefreshAfterPeerCollision() { if (!_grabbed) ConstrainToScreen(); PlacePet(); if (_bubble.IsVisible) PlaceBubble(); }
    internal void RenderCoordinated(object? sender, EventArgs e, double dt) => Animate(sender, e, dt);
    internal AudioService Audio => _audio;
    internal PetPhysics Physics => _physics;
    internal SettingsWindow? SettingsPanel => _settingsWindow;
    internal BubbleWindow BubblePanel => _bubble;
    internal int FullSettingsApplyCount { get; private set; }
    internal void BeginFrameMeasurement() { _frameIntervals.Clear(); _previousFrameTime = 0; _collectFrameIntervals = true; }
    internal double[] EndFrameMeasurement() { _collectFrameIntervals = false; return _frameIntervals.ToArray(); }
    internal void ValidateTrayForTest() { if (_tray == null) CreateTray(); }

    public PetWindow(PetSettings settings, bool testing = false, PetCoordinator? coordinator = null, SpriteAnimation? sprite = null)
    {
        Settings = settings; _testing = testing; _coordinator = coordinator;
        _audio = coordinator is null ? new AudioService() : new AudioService(coordinator.AudioMeter);
        InitializeComponent();
        try { _sprite = sprite ?? (settings.CharacterPath != null ? SpriteAnimation.Load(settings.CharacterPath) : SpriteAnimation.LoadDefault()); }
        catch { Settings.CharacterPath = null; _sprite = SpriteAnimation.LoadDefault(); }
        BuildOutlines();
        PetImage.Source = _sprite.CurrentFrame;
        _housekeeping = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _housekeeping.Tick += Housekeeping;
        SourceInitialized += OnSourceInitialized;
        PetImage.MouseLeftButtonDown += PointerDown;
        MouseMove += PointerMove;
        MouseLeftButtonUp += PointerUp;
        LostMouseCapture += (_, _) => { if (_grabbed) ReleaseGrab(); };
        PetImage.MouseRightButtonUp += (_, e) => { OpenMenu(); e.Handled = true; };
        Closed += (_, _) =>
        {
            if (_coordinator is not null) _coordinator.OnPetClosed(this);
            else { if (!_testing) SaveSettings(); Dispose(); System.Windows.Application.Current.Shutdown(); }
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        DesktopService.MakeToolWindow(_handle);
        HwndSource.FromHwnd(_handle)?.AddHook(WindowHook);
        _scale = DesktopService.GetWindowScale(_handle);
        _bounds = DesktopService.GetMonitorBounds(_handle);
        _physics.X = Settings.CenterX ?? (_bounds.Right - 220 * _scale);
        _physics.Y = Settings.CenterY ?? (_bounds.Bottom - 240 * _scale);
        if (Settings.CenterX.HasValue && Settings.CenterY.HasValue)
        {
            var savedScreen = Forms.Screen.FromPoint(new System.Drawing.Point((int)_physics.X, (int)_physics.Y)).Bounds;
            _bounds = (savedScreen.Left, savedScreen.Top, savedScreen.Right, savedScreen.Bottom);
        }
        ApplySettings();
        if (!_testing && _coordinator is null) CreateTray();
        _lastTime = _clock.Elapsed.TotalSeconds;
        StartAnimation(); _housekeeping.Start();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => { if (!_disposed) _bubble.Prepare(); }));
    }

    public void ApplySettings()
    {
        if (_disposed) return;
        FullSettingsApplyCount++;
        Title = $"弹弹桌宠 · Tantan · {Settings.InstanceName}";
        Width = Height = Math.Ceiling(Settings.Size * 1.85);
        PetImage.Width = PetImage.Height = Settings.Size;
        Topmost = Settings.Topmost;
        RefreshDialoguePool();
        ApplyBubbleAppearance();
        if (Settings.SqueakSound == "duck" && !AudioService.HasDuckAssets) Settings.SqueakSound = "original";
        _audio.SqueakVolume = (float)Settings.SqueakVolume;
        try { _audio.ConfigureSqueak(Settings.SqueakSound, Settings.CustomSoundPath); }
        catch (Exception ex) { App.Log(ex); Settings.SqueakSound = "original"; _audio.ConfigureSqueak("original", null); }
        _audio.SetSoundActive(!_suspended);
        if (Settings.AudioEnabled && !_suspended) _audio.Start(); else _audio.Stop();
        _coordinator?.RefreshAudio();
        _lastRenderBucket = -1;
        if (Settings.PhysicsEnabled && !_wasPhysics) _physics.Kick(ChooseKickSpeed() * _scale);
        if (!Settings.PhysicsEnabled && _wasPhysics) { _physics.Stop(); _physics.Angle = 0; }
        if (Settings.PhysicsEnabled && _wasPhysics && Settings.Speed != _configuredSpeed)
        {
            double speed = Math.Sqrt(_physics.VelocityX * _physics.VelocityX + _physics.VelocityY * _physics.VelocityY);
            if (speed > .01 && (!Settings.RandomKickSpeed || speed > Settings.Speed * _scale))
            { double ratio = Settings.Speed * _scale / speed; _physics.VelocityX *= ratio; _physics.VelocityY *= ratio; }
        }
        _wasPhysics = Settings.PhysicsEnabled;
        _configuredSpeed = Settings.Speed;
        if (!Settings.BubblesEnabled) _bubble.Hide();
        ConstrainToScreen(); PlacePet();
        ScheduleSave();
    }

    public void ScheduleSave() => _saveAfter = _clock.Elapsed.TotalSeconds + .75;
    public void ApplyBubbleAppearance()
    {
        if (_disposed) return;
        _bubble.SetAppearance(Settings.BubbleFontFamily, Settings.BubbleFontSize, Settings.Size / 220d * Settings.BubbleScale, Settings.BubbleFontWeight);
        FitBubbleToScreen();
        if (_bubble.IsVisible) PlaceBubble();
        ScheduleSave();
    }
    private void FitBubbleToScreen()
    {
        var work = Forms.Screen.FromPoint(new System.Drawing.Point((int)_physics.X, (int)_physics.Y)).WorkingArea;
        _bubble.FitToBounds(Math.Max(8, work.Width - 24) / _scale, Math.Max(8, work.Height - 24) / _scale);
    }

    // A text edit must not reconfigure audio, resize the pet HWND or constrain
    // its motion. The phrase pool changes immediately; persistence is debounced.
    internal void ApplyDialogueSettings()
    {
        if (_disposed) return;
        RefreshDialoguePool();
        ScheduleSave();
    }

    private void RefreshDialoguePool()
    {
        _phrases = PhraseBook.GetPhrases(Settings);
        if (_phrases.Length == 0) _bubble.Hide();
    }

    private void StartAnimation()
    {
        if (_renderSubscribed || _disposed) return;
        _lastRenderBucket = -1; _lastRenderingTime = TimeSpan.MinValue; _lastTime = _clock.Elapsed.TotalSeconds;
        if (_coordinator is null) CompositionTarget.Rendering += OnRendering;
        else { _coordinator.SetAnimated(this, true); _coordinator.EnsureRendering(); }
        _renderSubscribed = true;
    }
    private void StopAnimation()
    {
        if (!_renderSubscribed) return;
        if (_coordinator is null) CompositionTarget.Rendering -= OnRendering; else _coordinator.SetAnimated(this, false);
        _renderSubscribed = false;
    }
    private void OnRendering(object? sender, EventArgs e)
    {
        if (_disposed || _suspended) return;
        var stamp = ((RenderingEventArgs)e).RenderingTime;
        if (stamp == _lastRenderingTime) return;
        _lastRenderingTime = stamp;
        long bucket = (long)Math.Floor(stamp.TotalSeconds * Settings.Fps + .000001);
        if (bucket == _lastRenderBucket) return;
        _lastRenderBucket = bucket; Animate(sender, e);
    }

    private void Animate(object? sender, EventArgs e, double? coordinatedDt = null)
    {
        double now = _clock.Elapsed.TotalSeconds, dt = coordinatedDt ?? Math.Clamp(now - _lastTime, 0, .1); _lastTime = now;
        if (_suspended) return;
        if (_collectFrameIntervals)
        {
            if (_previousFrameTime > 0) _frameIntervals.Add((now - _previousFrameTime) * 1000);
            _previousFrameTime = now;
        }
        _spriteAdvanceAge += dt;
        long spriteBucket = (long)Math.Floor(((RenderingEventArgs)e).RenderingTime.TotalSeconds * Settings.Fps + .000001);
        if (_coordinator is null || spriteBucket != _lastRenderBucket)
        {
            _lastRenderBucket = spriteBucket;
            _sprite.Advance(_spriteAdvanceAge); _spriteAdvanceAge = 0;
            if (!ReferenceEquals(PetImage.Source, _sprite.CurrentFrame)) { PetImage.Source = _sprite.CurrentFrame; _hitFrame = null; }
        }
        if (_previewReleaseAt > 0 && now >= _previewReleaseAt) { _previewReleaseAt = 0; _pressSpring.Release(); }
        _pressSpring.Step(dt, Settings.SqueezeStrength);
        double squeeze = _pressSpring.Compression;
        double raw = Settings.AudioEnabled ? _audio.Level : 0;
        _audioAverage += (raw - _audioAverage) * (1 - Math.Exp(-2.2 * dt));
        double accent = Math.Clamp((raw - _audioAverage - .007) * 12 * Settings.Sensitivity, 0, 1);
        _beatAge += dt;
        if (accent > .18 && accent > _beatPulse + .035 && _beatAge > .2) { _swayDirection = -_swayDirection; _beatAge = 0; }
        _beatPulse = Math.Max(accent, _beatPulse * Math.Exp(-9 * dt));
        if (!Settings.AudioEnabled) _beatPulse = _audioAverage = 0;
        double activity = Math.Clamp(raw * .65 * Settings.Sensitivity, 0, 1);
        float level = (float)Math.Clamp(raw * Settings.Sensitivity, 0, 1);
        double sx = 1 + squeeze * .85 - activity * .024 - _beatPulse * .035, sy = 1 - squeeze + activity * .065 + _beatPulse * .095;
        SetNear(Squash, ScaleTransform.ScaleXProperty, sx); SetNear(Squash, ScaleTransform.ScaleYProperty, sy);
        SetNear(Bounce, TranslateTransform.YProperty, -Settings.Size * (.012 * activity + .055 * _beatPulse));
        if (Settings.PhysicsEnabled && !_grabbed)
        {
            var edge = VisibleOffsets(_physics.AngleAfter(dt, Settings.Decay));
            _physics.Step(dt, _bounds.Left, _bounds.Top, _bounds.Right, _bounds.Bottom, edge.Left, edge.Top, edge.Right, edge.Bottom, Settings.Decay);
            PlacePet(); _kickAge += dt;
            if (Settings.AutoKick && _kickAge >= Settings.KickInterval) { _physics.Kick(ChooseKickSpeed() * _scale); _kickAge = 0; }
        }
        SetNear(Spin, RotateTransform.AngleProperty, Settings.PhysicsEnabled ? _physics.Angle : _swayDirection * (_beatPulse * 4 + activity * Math.Sin(now * 1.8) * 1.2));
        // Rotation, GIF frame changes and squeezing can change the visible extent even at rest.
        if (!_grabbed) { ConstrainToScreen(); PlacePet(); }
        if (_bubble.IsVisible) { if (now >= _bubbleUntil) _bubble.Hide(); else PlaceBubble(); }
        if (Settings.BubblesEnabled && now >= _bubbleDeadline) SayRandom();
        if (now >= _previewNext) { _settingsWindow?.UpdatePreview(_sprite.CurrentFrame, level, _audio.Status, sx, sy); _previewNext = now + 1d / 15; }
    }

    private static void SetNear(DependencyObject obj, DependencyProperty property, double value)
    {
        if (Math.Abs((double)obj.GetValue(property) - value) > .001) obj.SetValue(property, value);
    }
    private void BuildOutlines()
    {
        _outlines.Clear();
        int bytes = 0;
        foreach (var frame in _sprite.Frames) bytes = Math.Max(bytes, checked(frame.PixelWidth * frame.PixelHeight * 4));
        var scratch = new byte[bytes];
        int vertices = 0;
        foreach (var frame in _sprite.Frames)
        {
            _outlines[frame] = OutlineCache.GetValue(frame, image => new SpriteOutline(image, 32, scratch));
            vertices = Math.Max(vertices, _outlines[frame].VertexCount);
        }
        _collisionVertices = new Point[vertices];
    }
    private Rect VisibleOffsets(double angle)
    {
        var rect = _outlines[_sprite.CurrentFrame].GetBounds(Settings.Size, Settings.Size, Squash.ScaleX, Squash.ScaleY, angle, Bounce.Y);
        if (rect.IsEmpty) return new Rect(0, 0, 0, 0);
        return new Rect(rect.X * _scale, rect.Y * _scale, rect.Width * _scale, rect.Height * _scale);
    }
    private void ConstrainToScreen()
    {
        var edge = VisibleOffsets(Spin.Angle);
        double minX = _bounds.Left - edge.Left, maxX = _bounds.Right - edge.Right;
        double minY = _bounds.Top - edge.Top, maxY = _bounds.Bottom - edge.Bottom;
        _physics.X = maxX >= minX ? Math.Clamp(_physics.X, minX, maxX) : (minX + maxX) / 2;
        _physics.Y = maxY >= minY ? Math.Clamp(_physics.Y, minY, maxY) : (minY + maxY) / 2;
    }
    private void PlacePet()
    {
        double left = Math.Round(_physics.X - Width * _scale / 2), top = Math.Round(_physics.Y - Height * _scale / 2);
        if (_handle == IntPtr.Zero || (left == _placedLeft && top == _placedTop && Settings.Topmost == _placedTopmost)) return;
        DesktopService.PositionWindow(_handle, left, top, Settings.Topmost);
        _placedLeft = left; _placedTop = top; _placedTopmost = Settings.Topmost;
    }

    private void Housekeeping(object? sender, EventArgs e)
    {
        bool hide = _manualHidden || (Settings.HideOnFullscreen && DesktopService.IsOtherAppFullscreen(_handle));
        SetSuspended(hide);
        double newScale = DesktopService.GetWindowScale(_handle);
        var bounds = DesktopService.GetMonitorBounds(_handle);
        if (newScale != _scale || bounds != _bounds) { _scale = newScale; _bounds = bounds; if (!_grabbed) ConstrainToScreen(); PlacePet(); }
        if (!_testing && _clock.Elapsed.TotalSeconds >= _saveAfter) SaveSettings(flush: false);
    }

    private void SetSuspended(bool hide)
    {
        if (_suspended == hide) return;
        _suspended = hide;
        if (hide) { Hide(); _bubble.Hide(); _audio.Stop(); _audio.SetSoundActive(false); StopAnimation(); }
        else { Show(); _placedLeft = double.NaN; PlacePet(); _audio.SetSoundActive(true); StartAnimation(); if (Settings.AudioEnabled) _audio.Start(); }
        if (_settingsWindow?.IsVisible == true) _settingsWindow.UpdatePreview(CurrentFrame, 0, hide ? "桌宠已隐藏，音频分析已暂停" : _audio.Status, 1, 1);
        _coordinator?.RefreshAudio();
    }

    public void Squeeze()
    {
        _pressSpring.Press(Settings.SqueezeStrength);
        _previewReleaseAt = _clock.Elapsed.TotalSeconds + .12;
        _audio.PlaySqueak();
    }
    internal void BeginPressFeedback()
    {
        _previewReleaseAt = 0; _pressSpring.Press(Settings.SqueezeStrength); _audio.PressSqueak();
    }
    internal void EndPressFeedback() { _previewReleaseAt = 0; _pressSpring.Release(); _audio.ReleaseSqueak(); }
    public void Kick()
    {
        Settings.PhysicsEnabled = true; _wasPhysics = true; _physics.Kick(ChooseKickSpeed() * _scale); _kickAge = 0; ScheduleSave();
    }
    private double ChooseKickSpeed() => Settings.RandomKickSpeed ? 25 + Random.Shared.NextDouble() * Math.Max(0, Settings.Speed - 25) : Settings.Speed;
    public void SayRandom()
    {
        _bubbleDeadline = _clock.Elapsed.TotalSeconds + Settings.BubbleInterval * (.7 + Random.Shared.NextDouble() * .6);
        if (_phrases.Length == 0)
        {
            _bubble.Hide(); _settingsWindow?.ShowPhrase("请填写预设或自定义对话。"); return;
        }
        Say(_phrases[Random.Shared.Next(_phrases.Length)], 4.5);
    }
    private void Say(string text, double seconds)
    {
        _settingsWindow?.ShowPhrase(text);
        if (_suspended) return;
        // Showing a WPF window can render reentrantly. Arm the lifetime before
        // Show(), so the animation callback cannot hide a newly opened bubble.
        _bubbleUntil = _clock.Elapsed.TotalSeconds + seconds;
        FitBubbleToScreen(); _bubble.Topmost = Settings.Topmost; _bubble.Say(text); PlaceBubble();
    }
    private void PlaceBubble()
    {
        FitBubbleToScreen();
        double bubbleScale = DesktopService.GetWindowScale(new WindowInteropHelper(_bubble).Handle);
        double bw = _bubble.ActualWidth * bubbleScale, bh = _bubble.ActualHeight * bubbleScale;
        var edge = VisibleOffsets(Spin.Angle);
        double x = _physics.X - bw / 2, y = _physics.Y + edge.Top - bh - 7;
        if (y < _bounds.Top + 8) y = _physics.Y + edge.Bottom + 7;
        x = Math.Clamp(x, _bounds.Left + 8, Math.Max(_bounds.Left + 8, _bounds.Right - bw - 8));
        y = Math.Clamp(y, _bounds.Top + 8, Math.Max(_bounds.Top + 8, _bounds.Bottom - bh - 8));
        _bubble.Place(x, y, Settings.Topmost);
    }

    private void PointerDown(object sender, MouseButtonEventArgs e)
    {
        _grabbed = true; _dragged = false; _grabPoint = Forms.Cursor.Position;
        _grabX = _physics.X; _grabY = _physics.Y;
        _pointerVelocity.Reset(_clock.Elapsed.TotalSeconds, _grabPoint.X, _grabPoint.Y);
        BeginPressFeedback();
        CaptureMouse(); e.Handled = true;
    }
    private void PointerMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_grabbed) return;
        var point = Forms.Cursor.Position;
        int dx = point.X - _grabPoint.X, dy = point.Y - _grabPoint.Y;
        _pointerVelocity.Add(_clock.Elapsed.TotalSeconds, point.X, point.Y);
        if (!_dragged && dx * dx + dy * dy < 25) return;
        _dragged = true;
        _physics.X = _grabX + dx; _physics.Y = _grabY + dy;
        // Allow crossing monitors while dragging; use the monitor under the cursor.
        var b = Forms.Screen.FromPoint(point).Bounds; _bounds = (b.Left, b.Top, b.Right, b.Bottom);
        PlacePet();
    }
    private void PointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!_grabbed) return;
        ReleaseGrab(); e.Handled = true;
    }
    private void ReleaseGrab()
    {
        _grabbed = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (_dragged && Settings.PhysicsEnabled)
        {
            var point = Forms.Cursor.Position; double now = _clock.Elapsed.TotalSeconds;
            _physics.X = _grabX + point.X - _grabPoint.X; _physics.Y = _grabY + point.Y - _grabPoint.Y;
            _pointerVelocity.Add(now, point.X, point.Y);
            var velocity = _pointerVelocity.Estimate(now);
            double speed = Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
            double ratio = speed > 20000 ? 20000 / speed : 1;
            _physics.VelocityX = velocity.X * ratio; _physics.VelocityY = velocity.Y * ratio;
            _physics.AngularVelocity = Math.Clamp(velocity.X * .035, -240, 240);
        }
        EndPressFeedback();
        ConstrainToScreen(); PlacePet();
        ScheduleSave();
    }

    public void ShowSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            if (_testing)
            {
                _settingsWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _settingsWindow.Left = _settingsWindow.Top = -10000;
                _settingsWindow.ShowActivated = false;
            }
            _settingsWindow.Closed += (_, _) => { _settingsWindow = null; if (!_testing) SaveSettings(); };
        }
        else _settingsWindow.Reload();
        _settingsWindow.Show(); _settingsWindow.Activate();
    }
    public bool ImportCharacter(Window owner)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择角色素材", Filter = "角色素材|*.gif;*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        if (dialog.ShowDialog(owner) != true) return false;
        try
        {
            var next = SpriteAnimation.Load(dialog.FileName);
            string dir = Path.Combine(PetSettings.DataDirectory, "Characters"); Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, Path.GetFileNameWithoutExtension(dialog.FileName) + "-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(dialog.FileName));
            try { File.Copy(dialog.FileName, target); } catch { next.Dispose(); throw; }
            _sprite.Dispose(); _sprite = next; BuildOutlines(); Settings.CharacterPath = target;
            PetImage.Source = _sprite.CurrentFrame; _hitFrame = null; ScheduleSave(); return true;
        }
        catch (Exception ex) { MessageBox.Show(owner, "无法加载素材。\n" + ex.Message, "素材导入失败", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
    }
    public bool ImportSound(Window owner)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择点击音效", Filter = "短音效 WAV / MP3|*.wav;*.mp3", CheckFileExists = true };
        if (dialog.ShowDialog(owner) != true) return false;
        string? target = null; bool copied = false;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 20 * 1024 * 1024) throw new InvalidDataException("请选择 20 MB 以内的短音效。");
            string dir = Path.Combine(PetSettings.DataDirectory, "Sounds"); Directory.CreateDirectory(dir);
            target = Path.Combine(dir, Path.GetFileNameWithoutExtension(dialog.FileName) + "-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(dialog.FileName));
            File.Copy(dialog.FileName, target); copied = true;
            _audio.ConfigureSqueak("custom", target);
            Settings.CustomSoundPath = target; Settings.SqueakSound = "custom"; ScheduleSave(); return true;
        }
        catch (Exception ex)
        {
            if (copied && target != null) { try { File.Delete(target); } catch { } }
            MessageBox.Show(owner, "无法加载音效。\n" + ex.Message, "音效导入失败", MessageBoxButton.OK, MessageBoxImage.Information); return false;
        }
    }
    public void ResetCharacter()
    {
        var next = SpriteAnimation.LoadDefault(); _sprite.Dispose(); _sprite = next; BuildOutlines(); Settings.CharacterPath = null;
        PetImage.Source = next.CurrentFrame; _hitFrame = null; ScheduleSave();
    }
    internal void SetCharacterForTest(string path)
    {
        var next = SpriteAnimation.Load(path); _sprite.Dispose(); _sprite = next; BuildOutlines(); Settings.CharacterPath = path;
        PetImage.Source = next.CurrentFrame; _hitFrame = null; ScheduleSave();
    }
    private void OpenMenu()
    {
        var menu = new ContextMenu();
        AddMenu(menu, "打开设置", ShowSettings);
        AddMenu(menu, "点击反馈", Squeeze); AddMenu(menu, "显示随机对话", SayRandom);
        menu.Items.Add(new Separator());
        AddMenu(menu, "物理运动", () => { Settings.PhysicsEnabled = !Settings.PhysicsEnabled; ApplySettings(); _settingsWindow?.Reload(); }, Settings.PhysicsEnabled);
        AddMenu(menu, "音频律动", () => { Settings.AudioEnabled = !Settings.AudioEnabled; ApplySettings(); _settingsWindow?.Reload(); }, Settings.AudioEnabled);
        AddMenu(menu, "置顶", () => { Settings.Topmost = !Settings.Topmost; ApplySettings(); _settingsWindow?.Reload(); }, Settings.Topmost);
        AddMenu(menu, "随机推进", () => { Kick(); _settingsWindow?.Reload(); });
        if (_coordinator is not null)
        {
            AddMenu(menu, "创建角色", () => { try { _coordinator.AddInstance(this); } catch (Exception ex) { MessageBox.Show(ex.Message, "创建角色"); } });
            AddMenu(menu, "删除此角色", () => { if (!_coordinator.RemoveInstance(this)) MessageBox.Show("至少保留一个角色。", "删除角色"); });
        }
        menu.Items.Add(new Separator()); AddMenu(menu, "隐藏桌宠（从托盘恢复）", ToggleHidden); AddMenu(menu, "退出程序", Exit);
        menu.PlacementTarget = this; menu.IsOpen = true;
    }
    private static void AddMenu(ContextMenu menu, string text, Action action, bool? check = null)
    {
        var item = new MenuItem { Header = text, IsCheckable = check.HasValue, IsChecked = check ?? false }; item.Click += (_, _) => action(); menu.Items.Add(item);
    }
    private void CreateTray()
    {
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("打开设置", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        trayMenu.Items.Add("点击反馈", null, (_, _) => Dispatcher.Invoke(Squeeze));
        trayMenu.Items.Add("显示 / 隐藏桌宠", null, (_, _) => Dispatcher.Invoke(ToggleHidden));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Exit));
        _tray = new Forms.NotifyIcon { Text = "弹弹桌宠 · Tantan · 双击打开设置", Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "bonbon.ico")), ContextMenuStrip = trayMenu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowSettings);
    }
    private void ToggleHidden() { _manualHidden = !_manualHidden; SetSuspended(_manualHidden || (Settings.HideOnFullscreen && DesktopService.IsOtherAppFullscreen(_handle))); }
    internal void ToggleVisibility() => ToggleHidden();
    internal void RestoreVisibility() { _manualHidden = false; SetSuspended(Settings.HideOnFullscreen && DesktopService.IsOtherAppFullscreen(_handle)); }
    public void Exit() { if (_coordinator is not null) { _coordinator.Exit(); return; } if (!_testing) SaveSettings(); Dispose(); System.Windows.Application.Current.Shutdown(); }
    private void SaveSettings(bool flush = true)
    {
        if (_testing) return;
        Settings.CenterX = _physics.X; Settings.CenterY = _physics.Y; _saveAfter = double.PositiveInfinity;
        if (_coordinator is not null) { _coordinator.RequestSave(flush); return; }
        // Snapshot on the UI thread, then write in order off-thread. Serializing
        // the live settings in a worker could race the next edit. Closing waits
        // for the final snapshot so an older autosave cannot overwrite it.
        try
        {
            string snapshot = JsonSerializer.Serialize(Settings, SettingsJsonOptions);
            _settingsSaveTask = _settingsSaveTask.ContinueWith(_ =>
            {
                try
                {
                    Directory.CreateDirectory(PetSettings.DataDirectory);
                    string temporary = PetSettings.FilePath + ".tmp";
                    File.WriteAllText(temporary, snapshot);
                    File.Move(temporary, PetSettings.FilePath, true);
                }
                catch (Exception ex) { App.Log(ex); }
            }, TaskScheduler.Default);
        }
        catch (Exception ex) { App.Log(ex); }
        if (flush)
        {
            try { _settingsSaveTask.GetAwaiter().GetResult(); }
            catch (Exception ex) { App.Log(ex); }
        }
    }
    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        if (msg == 0x0084 && !_grabbed && !IsOpaqueAt(lParam)) { handled = true; return new IntPtr(-1); }
        if (msg == 0x02E0) // WM_DPICHANGED: let WPF apply the DPI, then maintain the physical center.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_disposed) return;
                _scale = DesktopService.GetWindowScale(_handle); _placedLeft = double.NaN; PlacePet();
            }));
        return IntPtr.Zero;
    }
    private bool IsOpaqueAt(IntPtr packed)
    {
        try
        {
            long val = packed.ToInt64(); var point = PointFromScreen(new Point((short)(val & 0xffff), (short)((val >> 16) & 0xffff)));
            var transform = PetImage.TransformToAncestor(this).Inverse;
            if (transform == null) return false;
            point = transform.Transform(point);
            var frame = _sprite.CurrentFrame;
            double fit = Math.Min(PetImage.ActualWidth / frame.PixelWidth, PetImage.ActualHeight / frame.PixelHeight);
            int x = (int)Math.Floor((point.X - (PetImage.ActualWidth - frame.PixelWidth * fit) / 2) / fit);
            int y = (int)Math.Floor((point.Y - (PetImage.ActualHeight - frame.PixelHeight * fit) / 2) / fit);
            if (x < 0 || y < 0 || x >= frame.PixelWidth || y >= frame.PixelHeight) return false;
            _hitFrame ??= frame.Format == PixelFormats.Pbgra32 || frame.Format == PixelFormats.Bgra32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4]; _hitFrame.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel[3] >= 16;
        }
        catch { return true; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        StopAnimation(); _housekeeping.Stop(); _audio.Dispose(); _sprite.Dispose(); _outlines.Clear();
        if (_tray != null) { _tray.Visible = false; _tray.Icon?.Dispose(); _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _bubble.Close(); _settingsWindow?.Close();
    }
}
