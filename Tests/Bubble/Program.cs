using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BonbonPet;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (double size in new[] { 13.5, 24d, 32d })
        foreach (string phrase in new[] { "也让我懂得了，要珍惜朋友的肩膀", "昨日一去不复回，开心比什么都贵" })
        {
            var first = new BubbleWindow();
            first.Prepare(); first.SetAppearance("Arial", size); first.Left = first.Top = -10000;
            first.Say(phrase); first.UpdateLayout();
            first.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var surface = (FrameworkElement)first.Content;
            var clip = LayoutInformation.GetLayoutClip(surface);
            var ink = VisualTreeHelper.GetContentBounds(surface);
            Check(clip == null || clip.Bounds.Contains(ink),
                $"First show has a stale clip: font {size}, clip {clip?.Bounds}, ink {ink}", false);
            Check(GetClientRect(new WindowInteropHelper(first).Handle, out var client), "Get first-show client bounds", false);
            double scale = GetDpiForWindow(new WindowInteropHelper(first).Handle) / 96d;
            Check(client.Right + 1 >= ink.Right * scale && client.Bottom + 1 >= ink.Bottom * scale,
                $"First-show HWND clips geometry: native {client.Right}x{client.Bottom}, ink {ink}", false);
            first.Close();
        }
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
        double maximumNativeSizeDifference = 0;
        foreach (string font in new[] { "Microsoft YaHei UI", "Arial", "Times New Roman" })
        foreach (double size in new[] { 10d, 13.5, 24, 32 })
        foreach (string phrase in new[] { "耶！", "昨日一去不复回，开心比什么都贵", "人生几十年，总会有风雨来陪。\n潇潇洒洒赴会，今不醉不归。\n阳光总会在风雨之后撒向苍茫。" })
        {
            bubble.SetAppearance(font, size); bubble.Say(phrase); bubble.UpdateLayout();
            bubble.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var content = (FrameworkElement)bubble.Content;
            var hwnd = new WindowInteropHelper(bubble).Handle;
            Check(GetClientRect(hwnd, out var client), "Get native client dimensions", false);
            double scale = GetDpiForWindow(hwnd) / 96d;
            maximumNativeSizeDifference = Math.Max(maximumNativeSizeDifference,
                Math.Max(Math.Abs(client.Right - content.ActualWidth * scale), Math.Abs(client.Bottom - content.ActualHeight * scale)));
            Check(client.Right + 1 >= content.ActualWidth * scale && client.Bottom + 1 >= content.ActualHeight * scale,
                $"Window clips content at {font}/{size}: native {client.Right}x{client.Bottom}, content {content.ActualWidth * scale}x{content.ActualHeight * scale}", false);
            var target = new RenderTargetBitmap(client.Right, client.Bottom, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            target.Render(content);
            var pixels = new byte[client.Right * client.Bottom * 4]; target.CopyPixels(pixels, client.Right * 4, 0);
            bool edgeInk = false;
            for (int x = 0; x < client.Right; x++) edgeInk |= pixels[x * 4 + 3] != 0 || pixels[((client.Bottom - 1) * client.Right + x) * 4 + 3] != 0;
            for (int y = 0; y < client.Bottom; y++) edgeInk |= pixels[(y * client.Right) * 4 + 3] != 0 || pixels[(y * client.Right + client.Right - 1) * 4 + 3] != 0;
            Check(!edgeInk, $"Shape reaches the native clipping edge at {font}/{size}", false);
        }
        bubble.Close(); app.Shutdown();
        Console.WriteLine($"{_checks} bubble checks passed; native size difference at most {maximumNativeSizeDifference:F3}px. Preview: " + output);
    }
    private static void Check(bool pass, string message, bool log = true) { if (!pass) throw new Exception(message); _checks++; if (log) Console.WriteLine("PASS: " + message); }
    private static void Snapshot(FrameworkElement visual, string path)
    {
        var target = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path); encoder.Save(file);
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
