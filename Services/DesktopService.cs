using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BonbonPet.Services;

/// <summary>Native helpers. Screen coordinates passed to these methods are physical pixels, not WPF DIPs.</summary>
public static class DesktopService
{
    private const int GwlExStyle = -20;
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExAppWindow = 0x00040000;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExTopmost = 0x00000008;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    public static void MakeToolWindow(IntPtr hwnd, bool clickThrough = false)
    {
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLongPointer(hwnd, GwlExStyle).ToInt64();
        style = (style | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow;
        if (clickThrough) style |= 0x20; // WS_EX_TRANSPARENT: layered bubble passes clicks to apps underneath.
        SetWindowLongPointer(hwnd, GwlExStyle, new IntPtr(style));
    }

    /// <summary>Moves without activating or resizing. left and top are physical virtual-screen pixels.</summary>
    public static void PositionWindow(IntPtr hwnd, double left, double top, bool topmost)
    {
        if (hwnd == IntPtr.Zero || !double.IsFinite(left) || !double.IsFinite(top)) return;
        var currentlyTopmost = (GetWindowLongPointer(hwnd, GwlExStyle).ToInt64() & WsExTopmost) != 0;
        var flags = SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder;
        // HWND_NOTOPMOST on every frame would keep promoting an ordinary pet
        // above other apps. Moving an unchanged layer must preserve Z order.
        if (currentlyTopmost == topmost) flags |= SwpNoZOrder;
        SetWindowPos(hwnd, topmost ? new IntPtr(-1) : new IntPtr(-2), (int)Math.Round(left), (int)Math.Round(top),
            0, 0, flags);
    }

    /// <summary>Full monitor bounds in physical virtual-screen pixels, including the taskbar area.</summary>
    public static (double Left, double Top, double Right, double Bottom) GetMonitorBounds(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            return (info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        return (0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
    }

    public static double GetWindowScale(IntPtr hwnd)
    {
        var dpi = hwnd != IntPtr.Zero ? GetDpiForWindow(hwnd) : 96u;
        return dpi == 0 ? 1 : dpi / 96.0;
    }

    /// <summary>Checks the foreground app against its own monitor; ignores our process and Explorer's desktop.</summary>
    public static bool IsOtherAppFullscreen(IntPtr ownWindowHandle)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == ownWindowHandle || !IsWindowVisible(foreground) || IsIconic(foreground))
            return false;
        GetWindowThreadProcessId(foreground, out var pid);
        if (pid == Environment.ProcessId) return false;

        var className = new StringBuilder(256);
        GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        // With an auto-hidden taskbar an ordinary maximized window can cover
        // the full monitor. A maximized captioned app is still windowed.
        if (IsZoomed(foreground) && (GetWindowLongPointer(foreground, GwlStyle).ToInt64() & WsCaption) == WsCaption)
            return false;

        if (!GetWindowRect(foreground, out var rect)) return false;
        var monitor = MonitorFromWindow(foreground, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
        var screen = info.Monitor;
        const int tolerance = 2;
        return rect.Left <= screen.Left + tolerance && rect.Top <= screen.Top + tolerance &&
               rect.Right >= screen.Right - tolerance && rect.Bottom >= screen.Bottom - tolerance &&
               rect.Right - rect.Left >= screen.Right - screen.Left - tolerance &&
               rect.Bottom - rect.Top >= screen.Bottom - screen.Top - tolerance;
    }

    private static IntPtr GetWindowLongPointer(IntPtr hwnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));
    private static IntPtr SetWindowLongPointer(IntPtr hwnd, int index, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, WorkArea; public uint Flags; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maximum);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
