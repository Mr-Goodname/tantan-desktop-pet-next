using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using BonbonPet.Services;

// Hidden native windows exercise the helper without displaying anything or
// changing the user's foreground app, monitor mode, or fullscreen state.
Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
var foreground = Native.GetForegroundWindow();
var instance = Native.GetModuleHandle(null);
var className = "BonbonPetNativeTest" + Environment.ProcessId;
Native.WindowProcedure callback = Native.DefWindowProc;
var windowClass = new Native.WindowClass
{
    Size = (uint)Marshal.SizeOf<Native.WindowClass>(),
    Procedure = Marshal.GetFunctionPointerForDelegate(callback),
    Instance = instance,
    ClassName = className
};
if (Native.RegisterClassEx(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
var hwnd = Native.CreateWindowEx(0, className, "Hidden native helper test", 0x80000000, 50, 50, 100, 80,
    IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
if (hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
var companion = Native.CreateWindowEx(0, className, "Hidden Z order companion", 0x80000000, 30, 30, 40, 40,
    IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
if (companion == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    assertions++;
}
try
{
    DesktopService.MakeToolWindow(hwnd);
    var style = Native.GetWindowLongPtr(hwnd, -20).ToInt64();
    Check((style & 0x80) != 0, "Tool window style missing");
    Check((style & 0x08000000) != 0, "Noactivate style missing");
    Check((style & 0x40000) == 0, "Appwindow style remains");
    DesktopService.PositionWindow(hwnd, 73, 89, false);
    Native.GetWindowRect(hwnd, out var rect);
    Check(rect.Left == 73 && rect.Top == 89, "Physical pixel movement failed");
    Check(rect.Right - rect.Left == 100 && rect.Bottom - rect.Top == 80, "Movement resized the window");
    Check(Native.GetForegroundWindow() == foreground, "Movement stole foreground activation");
    DesktopService.PositionWindow(hwnd, 74, 90, true);
    Check((Native.GetWindowLongPtr(hwnd, -20).ToInt64() & 8) != 0, "Topmost toggle failed");
    DesktopService.PositionWindow(hwnd, 75, 91, false);
    Check((Native.GetWindowLongPtr(hwnd, -20).ToInt64() & 8) == 0, "Ordinary layer toggle failed");
    Check(Native.GetForegroundWindow() == foreground, "Layer toggle stole foreground activation");
    Native.SetWindowPos(companion, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    var previousSibling = Native.GetWindow(hwnd, 3);
    DesktopService.PositionWindow(hwnd, 76, 92, false);
    Check(Native.GetWindow(hwnd, 3) == previousSibling, "Ordinary movement changed Z order");
    Check(Native.GetForegroundWindow() == foreground, "Ordinary movement stole foreground activation");
    var bounds = DesktopService.GetMonitorBounds(hwnd);
    Check(bounds.Right > bounds.Left && bounds.Bottom > bounds.Top, "Monitor bounds empty");
    var scale = DesktopService.GetWindowScale(hwnd);
    Check(scale is >= 0.75 and <= 5, "Invalid monitor DPI scale");
    Check(!DesktopService.IsOtherAppFullscreen(foreground), "Explicitly exempted foreground window considered another app");
    Check(!Native.IsWindowVisible(hwnd), "Test window accidentally became visible");

    var foregroundClass = new StringBuilder(256);
    Native.GetClassName(foreground, foregroundClass, foregroundClass.Capacity);
    Native.GetWindowRect(foreground, out var foregroundRect);
    var foregroundBounds = DesktopService.GetMonitorBounds(foreground);
    Console.WriteLine($"Native checks passed: {assertions} assertions (hidden window, noactivate, bounds, DPI, layer toggles).");
    Console.WriteLine($"Monitor bounds: ({bounds.Left}, {bounds.Top})..({bounds.Right}, {bounds.Bottom}), DPI scale: {scale:F2}");
    Console.WriteLine($"Foreground class: {foregroundClass}, rect: ({foregroundRect.Left}, {foregroundRect.Top})..({foregroundRect.Right}, {foregroundRect.Bottom})");
    Console.WriteLine($"Foreground monitor: ({foregroundBounds.Left}, {foregroundBounds.Top})..({foregroundBounds.Right}, {foregroundBounds.Bottom})");
    Console.WriteLine($"Current foreground fullscreen detection: {DesktopService.IsOtherAppFullscreen(hwnd)}");
}
finally
{
    Native.DestroyWindow(companion);
    Native.DestroyWindow(hwnd);
    Native.UnregisterClass(className, instance);
    GC.KeepAlive(callback);
}

internal static class Native
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WindowClass
    {
        public uint Size, Style;
        public IntPtr Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool UnregisterClass(string className, IntPtr instance);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hwnd, StringBuilder className, int count);
}
