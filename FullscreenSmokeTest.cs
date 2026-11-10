using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BonbonPet.Core;
using BonbonPet.Services;

namespace BonbonPet;

internal static class FullscreenSmokeTest
{
    internal const string HostArgument = "--fullscreen-test-host";
    private const uint RaiseMessage = 0x8001;
    private static IntPtr _hostWindow, _hostInstance;
    private static string? _hostClass;
    private static Native.WindowProcedure? _hostProcedure;
    private static DispatcherTimer? _hostTimer;
    private static Process? _hostParent;

    internal static async Task<List<string>> Run(PetWindow pet, string output)
    {
        if (!App.IsTesting || !(bool)typeof(PetWindow).GetField("_testing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pet)!)
            throw new InvalidOperationException("Fullscreen smoke checks require an isolated self-test process.");
        var checks = new List<string>();
        var settings = pet.Settings.Copy();
        var position = new Point(pet.Physics.X, pet.Physics.Y);
        var velocity = new Point(pet.Physics.VelocityX, pet.Physics.VelocityY);
        double angle = pet.Physics.Angle, angularVelocity = pet.Physics.AngularVelocity;
        bool bubbleWasVisible = pet.BubblePanel.IsVisible;
        string bubbleText = pet.BubblePanel.CurrentText;
        Process? host = null;
        IntPtr fixture = IntPtr.Zero;
        Directory.CreateDirectory(output);
        var housekeeping = (DispatcherTimer)typeof(PetWindow).GetField("_housekeeping", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pet)!;
        bool housekeepingWasEnabled = housekeeping.IsEnabled;
        // Drive the shared policy with the external fixture HWND, so neither the
        // user's current foreground nor the test role's timer can race a setup.
        housekeeping.Stop();
        try
        {
            var petHandle = new WindowInteropHelper(pet).Handle;
            var screen = DesktopService.GetMonitorBounds(petHandle);
            string stateFile = Path.Combine(output, "fullscreen-host-" + Guid.NewGuid().ToString("N") + ".json");
            var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("Test executable path unavailable."))
            {
                UseShellExecute = false, CreateNoWindow = true
            };
            foreach (string argument in new[]
            {
                HostArgument, stateFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                screen.Left.ToString(CultureInfo.InvariantCulture), screen.Top.ToString(CultureInfo.InvariantCulture),
                (screen.Right - screen.Left).ToString(CultureInfo.InvariantCulture),
                (screen.Bottom - screen.Top).ToString(CultureInfo.InvariantCulture)
            }) start.ArgumentList.Add(argument);
            host = Process.Start(start) ?? throw new IOException("Fullscreen fixture could not start.");
            var readyWatch = Stopwatch.StartNew();
            while (!File.Exists(stateFile) && !host.HasExited && readyWatch.Elapsed.TotalSeconds < 10) await Task.Delay(40);
            Require(File.Exists(stateFile), "the isolated fullscreen host starts without loading profiles or startup registration", checks);
            using (var document = JsonDocument.Parse(File.ReadAllText(stateFile)))
            {
                if (document.RootElement.TryGetProperty("Error", out var error)) throw new InvalidOperationException(error.GetString());
                fixture = new IntPtr(document.RootElement.GetProperty("Handle").GetInt64());
                Require(document.RootElement.GetProperty("ProcessId").GetInt32() == host.Id,
                    "fullscreen detection is exercised against a separate process HWND", checks);
            }
            Require(DesktopService.IsFullscreenWindow(fixture, petHandle) && Native.IsWindowVisible(fixture) &&
                (Native.GetWindowLongPtr(fixture, -20).ToInt64() & 0x08000000) != 0,
                "the real borderless fullscreen fixture is visible natively and cannot activate", checks);
            Require(!DesktopService.IsFullscreenWindow(petHandle, IntPtr.Zero),
                "fullscreen classification continues to exempt the desktop pet's own process", checks);

            pet.Settings.PhysicsEnabled = false; pet.Settings.AudioEnabled = false;
            pet.Settings.Topmost = true; pet.Settings.HideOnFullscreen = false;
            pet.Settings.BubblesEnabled = false; pet.Settings.SqueakVolume = 0;
            pet.Settings.PresetPhrases = "全屏显示检查"; pet.Settings.CustomPhrases = "";
            pet.ApplySettings();
            pet.RefreshDesktopForTest(IntPtr.Zero);
            pet.Physics.X = (screen.Left + screen.Right) / 2;
            pet.Physics.Y = screen.Top + (screen.Bottom - screen.Top) * .58;
            pet.RefreshAfterPeerCollision(); pet.SayRandom();
            var bubbleHandle = new WindowInteropHelper(pet.BubblePanel).Handle;

            // Reproduce the reported split: an already-topmost pet remains below
            // a newly raised fullscreen window while its dialog is above it.
            Native.PostMessage(fixture, RaiseMessage, IntPtr.Zero, IntPtr.Zero); await Task.Delay(60);
            DesktopService.ReassertTopmost(bubbleHandle);
            Require(Above(fixture, petHandle) && Above(bubbleHandle, fixture),
                "the fixture reproduces a covered cached pet with a dialog still above fullscreen", checks);
            var foreground = Native.GetForegroundWindow();
            pet.RefreshDesktopForTest(fixture);
            Require(VisibleTopmost(petHandle) && VisibleTopmost(bubbleHandle) && Above(petHandle, fixture) && Above(bubbleHandle, fixture),
                "fullscreen maintenance raises both native pet and dialog above the fullscreen window", checks);
            Require(Native.GetForegroundWindow() == foreground,
                "repairing fullscreen overlay order does not steal foreground activation", checks);

            Native.PostMessage(fixture, RaiseMessage, IntPtr.Zero, IntPtr.Zero); await Task.Delay(60);
            Require(Above(fixture, petHandle), "a later fullscreen promotion can cover the stationary cached pet again", checks);
            pet.RefreshDesktopForTest(fixture);
            Require(Above(petHandle, fixture) && Above(bubbleHandle, fixture),
                "repeated fullscreen maintenance repairs layering without requiring movement or a new dialog", checks);

            pet.Settings.Topmost = false; pet.ApplySettings();
            var previousSibling = Native.GetWindow(petHandle, 3);
            foreground = Native.GetForegroundWindow();
            pet.RefreshDesktopForTest(fixture);
            Require((Native.GetWindowLongPtr(petHandle, -20).ToInt64() & 8) == 0 &&
                (Native.GetWindowLongPtr(bubbleHandle, -20).ToInt64() & 8) == 0 &&
                Native.GetWindow(petHandle, 3) == previousSibling && Above(fixture, petHandle) &&
                Native.GetForegroundWindow() == foreground,
                "ordinary-layer pets and dialogs remain ordinary without promotion or activation", checks);

            pet.Settings.Topmost = true; pet.Settings.HideOnFullscreen = true; pet.ApplySettings();
            pet.SayRandom(); pet.RefreshDesktopForTest(fixture);
            Require(!pet.IsVisible && !pet.BubblePanel.IsVisible && !Native.IsWindowVisible(petHandle) && !Native.IsWindowVisible(bubbleHandle),
                "enabling fullscreen auto-hide hides both actual native overlay windows", checks);
            pet.Settings.HideOnFullscreen = false; pet.ApplySettings();
            foreground = Native.GetForegroundWindow();
            pet.RefreshDesktopForTest(fixture); pet.SayRandom(); pet.RefreshDesktopForTest(fixture);
            Require(pet.IsVisible && pet.BubblePanel.IsVisible && VisibleTopmost(petHandle) && VisibleTopmost(bubbleHandle) &&
                Above(petHandle, fixture) && Above(bubbleHandle, fixture) && Native.GetForegroundWindow() == foreground,
                "disabling auto-hide restores both overlays above fullscreen without activation", checks);

            pet.Settings.HideOnFullscreen = true; pet.ApplySettings(); pet.RefreshDesktopForTest(fixture);
            pet.RefreshDesktopForTest(IntPtr.Zero);
            Require(pet.IsVisible && Native.IsWindowVisible(petHandle),
                "leaving fullscreen restores the pet when fullscreen auto-hide remains enabled", checks);
            File.WriteAllText(Path.Combine(output, "fullscreen-result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
            return checks;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "fullscreen-result.json"), JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
        finally
        {
            if (host is not null)
            {
                if (!host.HasExited)
                {
                    if (fixture != IntPtr.Zero) Native.PostMessage(fixture, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    var exitWatch = Stopwatch.StartNew();
                    while (!host.HasExited && exitWatch.Elapsed.TotalSeconds < 2) await Task.Delay(40);
                    if (!host.HasExited) host.Kill();
                }
                host.Dispose();
            }
            foreach (var property in typeof(PetSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(property => property.CanWrite))
                property.SetValue(pet.Settings, property.GetValue(settings));
            pet.RefreshDesktopForTest(IntPtr.Zero); pet.ApplySettings();
            pet.Physics.X = position.X; pet.Physics.Y = position.Y;
            pet.Physics.VelocityX = velocity.X; pet.Physics.VelocityY = velocity.Y;
            pet.Physics.Angle = angle; pet.Physics.AngularVelocity = angularVelocity;
            if (bubbleWasVisible) pet.BubblePanel.Say(bubbleText); else pet.BubblePanel.Hide();
            pet.RefreshAfterPeerCollision(); pet.SettingsPanel?.Reload();
            if (housekeepingWasEnabled) housekeeping.Start();
        }
    }

    // App dispatches this branch before its mutex, profiles and registry paths.
    internal static void RunHost(string[] args)
    {
        int index = Array.IndexOf(args, HostArgument);
        string? stateFile = index >= 0 && args.Length > index + 1 ? args[index + 1] : null;
        try
        {
            if (index < 0 || args.Length < index + 7) throw new ArgumentException("Invalid fullscreen host arguments.");
            int parentId = int.Parse(args[index + 2], CultureInfo.InvariantCulture);
            int left = int.Parse(args[index + 3], CultureInfo.InvariantCulture), top = int.Parse(args[index + 4], CultureInfo.InvariantCulture);
            int width = int.Parse(args[index + 5], CultureInfo.InvariantCulture), height = int.Parse(args[index + 6], CultureInfo.InvariantCulture);
            if (parentId == Environment.ProcessId || width <= 0 || height <= 0) throw new ArgumentException("Invalid fullscreen host dimensions or parent.");
            _hostParent = Process.GetProcessById(parentId);
            if (_hostParent.HasExited) throw new InvalidOperationException("Fullscreen test parent has exited.");
            _hostInstance = Native.GetModuleHandle(null);
            _hostClass = "TantanFullscreenFixture" + Environment.ProcessId;
            _hostProcedure = HostWindowProcedure;
            var windowClass = new Native.WindowClass
            {
                Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = Marshal.GetFunctionPointerForDelegate(_hostProcedure),
                Instance = _hostInstance, ClassName = _hostClass
            };
            if (Native.RegisterClassEx(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            const uint extendedStyle = 0x08000000 | 0x00080000 | 0x00000080 | 0x00000020 | 8;
            _hostWindow = Native.CreateWindowEx(extendedStyle, _hostClass, "Tantan isolated fullscreen fixture", 0x80000000,
                left, top, width, height, IntPtr.Zero, IntPtr.Zero, _hostInstance, IntPtr.Zero);
            if (_hostWindow == IntPtr.Zero || !Native.SetLayeredWindowAttributes(_hostWindow, 0, 0, 2) ||
                !Native.SetWindowPos(_hostWindow, new IntPtr(-1), left, top, width, height, 0x0040 | 0x0010))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            WriteHostState(stateFile!, new { Handle = _hostWindow.ToInt64(), ProcessId = Environment.ProcessId });
            var age = Stopwatch.StartNew();
            _hostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _hostTimer.Tick += (_, _) =>
            {
                try { if (_hostParent?.HasExited != false || age.Elapsed.TotalSeconds > 60) StopHost(); }
                catch { StopHost(); }
            };
            _hostTimer.Start();
        }
        catch (Exception error)
        {
            if (stateFile is not null)
            {
                try { WriteHostState(stateFile, new { Error = error.ToString() }); } catch { }
            }
            StopHost(1);
        }
    }

    private static void WriteHostState(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value));
        File.Move(path + ".tmp", path, true);
    }
    private static IntPtr HostWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == RaiseMessage)
        {
            Native.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
            return IntPtr.Zero;
        }
        if (message == 0x0010) { StopHost(); return IntPtr.Zero; }
        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }
    private static void StopHost(int exitCode = 0)
    {
        _hostTimer?.Stop();
        var hwnd = _hostWindow; _hostWindow = IntPtr.Zero;
        if (hwnd != IntPtr.Zero) Native.DestroyWindow(hwnd);
        if (_hostClass is not null) Native.UnregisterClass(_hostClass, _hostInstance);
        _hostParent?.Dispose();
        System.Windows.Application.Current.Shutdown(exitCode);
    }
    private static bool Above(IntPtr window, IntPtr other)
    {
        if (window == IntPtr.Zero || other == IntPtr.Zero) return false;
        for (var current = Native.GetWindow(window, 0); current != IntPtr.Zero; current = Native.GetWindow(current, 2))
        {
            if (current == window) return true;
            if (current == other) return false;
        }
        return false;
    }
    private static bool VisibleTopmost(IntPtr hwnd) => Native.IsWindowVisible(hwnd) && (Native.GetWindowLongPtr(hwnd, -20).ToInt64() & 8) != 0;
    private static void Require(bool passed, string message, List<string> checks)
    {
        if (!passed) throw new InvalidOperationException(message);
        checks.Add(message);
    }

    private static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WindowClass
        {
            public uint Size, Style;
            public IntPtr Procedure;
            public int ClassExtra, WindowExtra;
            public IntPtr Instance, Icon, Cursor, Background;
            public string? MenuName;
            public string ClassName;
            public IntPtr SmallIcon;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WindowClass windowClass);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowEx(uint style, string className, string title, uint windowStyle, int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] internal static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool UnregisterClass(string className, IntPtr instance);
    }
}
