using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BonbonPet;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var bubble = new BubbleWindow();
        var foreground = GetForegroundWindow();
        bubble.Prepare();
        Check(!bubble.IsVisible, "Preparation does not show bubble");
        Check(GetForegroundWindow() == foreground, "Preparation never activates window");
        var style = GetWindowLongPtr(new WindowInteropHelper(bubble).Handle, -20).ToInt64();
        Check((style & 0x20) != 0 && (style & 0x08000000) != 0, "Bubble has native click-through and noactivate styles");
        var output = args.FirstOrDefault() ?? Path.GetFullPath("artifacts/bubble-reference");
        Directory.CreateDirectory(output);
        bubble.Left = -10000; bubble.Top = -10000;
        bubble.Say("工作一段时间后，可以起来活动一下。");
        Check(bubble.ActualWidth >= 210 && bubble.ActualWidth <= 300, "Short phrase bubble remains compact");
        Snapshot((FrameworkElement)bubble.Content, Path.Combine(output, "bubble-short.png"));
        var shortHeight = bubble.ActualHeight;
        bubble.Say("工作一段时间后，可以起来活动一下。\n喝一点水，放松眼睛，再继续手上的事情。\n桌宠可以留在屏幕上，也可以暂时隐藏起来。\n设置会自动保存，下次启动时继续使用。");
        Check(bubble.ActualHeight > shortHeight, "Bubble grows to fit wrapped text");
        Check(GetForegroundWindow() == foreground, "Showing text does not steal activation");
        Snapshot((FrameworkElement)bubble.Content, Path.Combine(output, "bubble-long.png"));
        bubble.Close(); app.Shutdown();
        Console.WriteLine("6 bubble checks passed. Preview: " + output);
    }
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    private static void Snapshot(FrameworkElement visual, string path)
    {
        var target = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path); encoder.Save(file);
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
}
