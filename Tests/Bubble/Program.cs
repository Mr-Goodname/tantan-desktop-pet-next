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
    private static double _maximumNativeSizeDifference;
    private static double _coercedMaxWidth = double.PositiveInfinity, _coercedMaxHeight = double.PositiveInfinity;
    private static int _dimensionCoercions;
    [STAThread]
    private static void Main(string[] args)
    {
        InstallDimensionCoercionRegression();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string longPhrase = string.Concat(Enumerable.Repeat("朋友一起慢慢向前走，开心比什么都贵。", 8)).Substring(0, 110);
        foreach (double size in new[] { 10d, 13.5, 32d })
        foreach (double visualScale in new[] { .2, 1d, 5d })
        {
            var first = new BubbleWindow();
            first.Prepare(); first.SetAppearance("Arial", size, visualScale, 900);
            first.FitToBounds(1400, 900); first.Left = first.Top = -10000;
            first.Say(longPhrase);
            Validate(first, $"first show {size}/{visualScale}/900");
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
        // Scaling must apply to the text, strokes, body and dots together.
        bubble.SetAppearance("Microsoft YaHei UI", 13.5, 1, 400); bubble.Say("耶！");
        double naturalWidth = bubble.ActualWidth - 4, naturalHeight = bubble.ActualHeight - 4;
        foreach (double visualScale in new[] { .2, .5, 1d, 2.5, 5d })
        {
            bubble.SetAppearance("Microsoft YaHei UI", 13.5, visualScale, 400);
            Validate(bubble, $"uniform scale {visualScale}");
            double effectiveScale = bubble.CurrentVisualScale;
            Check(effectiveScale <= visualScale &&
                Math.Abs((bubble.ActualWidth - 4) - naturalWidth * effectiveScale) <= effectiveScale + 1 &&
                Math.Abs((bubble.ActualHeight - 4) - naturalHeight * effectiveScale) <= effectiveScale + 1,
                $"Whole visual scales at {visualScale}, including measured strokes and dots", false);
            Check(Math.Abs(bubble.CurrentFontSize - 13.5 * effectiveScale) < .0001,
                $"Effective font size scales at {visualScale}", false);
        }
        Snapshot((FrameworkElement)bubble.Content, Path.Combine(output, "bubble-scaled-large.png"));
        bubble.FitToBounds(1400, 900);
        int phraseIndex = 0;
        foreach (string font in new[] { "Microsoft YaHei UI", "Arial", "Times New Roman", "DFHanziPenW5-A", "FZMiaoWuS GB" }
            .Where(name => Fonts.SystemFontFamilies.Any(font => string.Equals(font.Source, name, StringComparison.OrdinalIgnoreCase))))
        foreach (double size in new[] { 10d, 13.5, 24, 32 })
        foreach (int weight in new[] { 100, 400, 900 })
        foreach (double visualScale in new[] { .2, .5, 1d, 2.5, 5d })
        {
            string phrase = new[] { "耶！", "昨日一去不复回，开心比什么都贵", longPhrase }[phraseIndex++ % 3];
            // Appearance changes while visible exercise the live font/weight
            // path before Say also changes the measured text dimensions.
            bubble.SetAppearance(font, size, visualScale, weight);
            bubble.Say(phrase);
            Validate(bubble, $"{font}/{size}/{visualScale}/{weight}/{phrase.Length}");
            Check(bubble.CurrentFontWeight == weight, "Live font weight is applied", false);
            Check(bubble.CurrentVisualScale <= visualScale && bubble.ActualWidth <= 1401 && bubble.ActualHeight <= 901,
                $"Oversized visual is uniformly fitted to the available screen: {font}/{size}/{visualScale}/{weight}, effective {bubble.CurrentVisualScale}, window {bubble.ActualWidth}x{bubble.ActualHeight}", false);
        }
        // A hidden HWND must not retain the old clipping rectangle when the
        // next bubble goes from tiny/light/short to huge/heavy/110 characters.
        foreach (double visualScale in new[] { 5d, .2, 2.5, .5 })
        {
            bubble.Hide(); bubble.SetAppearance("Times New Roman", visualScale > 1 ? 32 : 10, visualScale, visualScale > 1 ? 900 : 100);
            Check(!bubble.IsVisible, "Hidden appearance changes do not show the bubble", false);
            bubble.Say(visualScale > 1 ? longPhrase : "哦！");
            Validate(bubble, $"hidden resize {visualScale}");
        }
        Snapshot((FrameworkElement)bubble.Content, Path.Combine(output, "bubble-scaled-small.png"));
        bubble.SetAppearance("Arial", 32, 5, 900); bubble.Say(longPhrase);
        bubble.FitToBounds(420, 280); Validate(bubble, "small monitor fitting");
        Check(bubble.ActualWidth <= 421 && bubble.ActualHeight <= 281 && bubble.CurrentVisualScale < 5,
            "Long large text fits a smaller display without cutting any ink");
        double constrainedScale = bubble.CurrentVisualScale;
        bubble.FitToBounds(1400, 900); Validate(bubble, "larger monitor fitting");
        Check(bubble.CurrentVisualScale > constrainedScale && bubble.RequestedVisualScale == 5,
            "Moving to a larger display restores the requested scale as space permits");
        bubble.SetAppearance("Microsoft YaHei UI", 32, 5, 900);
        bubble.Say(new string('耶', 110));
        bubble.FitToBounds(420, 900); Validate(bubble, "110 repeated large characters stay within paper");
        Snapshot((FrameworkElement)bubble.Content, Path.Combine(output, "bubble-fitted.png"));
        // Model a real small client, independently of the screen budget passed
        // by the caller. Layout must respect the resulting HWND size and clip.
        bubble.MaxWidth = 310; bubble.MaxHeight = 220;
        bubble.FitToBounds(1400, 900); bubble.Say(new string('耶', 110));
        Validate(bubble, "native constrained client");
        Check(bubble.ActualWidth <= 311 && bubble.ActualHeight <= 221 && bubble.CurrentVisualScale < bubble.RequestedVisualScale,
            "Actual small client dimensions uniformly reduce the full bubble and text");
        double nativeLimitedScale = bubble.CurrentVisualScale;
        bubble.MaxWidth = bubble.MaxHeight = double.PositiveInfinity;
        bubble.Say(new string('耶', 110)); Validate(bubble, "native client constraint removed");
        Check(bubble.CurrentVisualScale > nativeLimitedScale && bubble.RequestedVisualScale == 5,
            "Removing the native size ceiling restores the requested scale as desktop space permits");
        // A native/WPF size correction can replace the Width/Height property
        // value itself. Comparing ActualHeight with that coerced getter would
        // miss the original request even though the visual still needs to fit.
        _dimensionCoercions = 0;
        _coercedMaxWidth = 310; _coercedMaxHeight = 220;
        double coercedScale;
        try
        {
            // The limits live outside the dependency property. A same-value
            // assignment can skip coercion, so explicitly invalidate both DP
            // effective values when activating this simulated native ceiling.
            bubble.CoerceValue(FrameworkElement.WidthProperty);
            bubble.CoerceValue(FrameworkElement.HeightProperty);
            bubble.Say(new string('耶', 110)); Validate(bubble, "coerced dimension properties");
            Check(_dimensionCoercions > 0 && bubble.Width <= 310 && bubble.Height <= 220 &&
                bubble.ActualWidth <= 311 && bubble.ActualHeight <= 221 && bubble.CurrentVisualScale < bubble.RequestedVisualScale,
                $"Coerced Width/Height getters cannot hide a too-large visual request: coercions={_dimensionCoercions}, properties={bubble.Width:F3}x{bubble.Height:F3}, actual={bubble.ActualWidth:F3}x{bubble.ActualHeight:F3}, scale={bubble.CurrentVisualScale:F6}, requestedScale={bubble.RequestedVisualScale:F3}");
            coercedScale = bubble.CurrentVisualScale;
        }
        finally
        {
            _coercedMaxWidth = _coercedMaxHeight = double.PositiveInfinity;
            bubble.CoerceValue(FrameworkElement.WidthProperty);
            bubble.CoerceValue(FrameworkElement.HeightProperty);
        }
        bubble.Say(new string('耶', 110)); Validate(bubble, "dimension property coercion removed");
        Check(bubble.CurrentVisualScale > coercedScale && bubble.RequestedVisualScale == 5,
            "Removing property coercion restores the full requested scale as desktop space permits");
        ValidateOrientations(bubble, output);
        bubble.Close(); app.Shutdown();
        Console.WriteLine($"{_checks} bubble checks passed; native size difference at most {_maximumNativeSizeDifference:F3}px. Preview: " + output);
    }
    private static void ValidateOrientations(BubbleWindow bubble, string output)
    {
        bubble.FitToBounds(1000, 700);
        foreach (double size in new[] { 13.5, 32d })
        foreach (double scale in new[] { .6, 1.8 })
        foreach (string phrase in new[] { "FIRST ABC", "FIRST ABC\nMIDDLE W\nLAST XYZ" })
        {
            bubble.SetPointsUp(false); bubble.SetAppearance("Arial", size, scale, 700); bubble.Say(phrase);
            Validate(bubble, $"points down {size}/{scale}/{phrase.Length}");
            var content = (FrameworkElement)bubble.Content;
            var surface = (ThoughtBubbleSurface)((System.Windows.Controls.Border)content).Child;
            var text = (System.Windows.Controls.TextBlock)surface.Child;
            var natural = surface.NaturalSize;
            var windowSize = new Size(bubble.ActualWidth, bubble.ActualHeight);
            double fontSize = bubble.CurrentFontSize, fittedScale = bubble.CurrentVisualScale;
            var downText = Capture(content);
            var foreground = text.Foreground;
            text.Foreground = Brushes.Transparent;
            var downArtwork = Capture(content);
            text.Foreground = foreground;

            bubble.SetPointsUp(true);
            Validate(bubble, $"points up {size}/{scale}/{phrase.Length}");
            Check(bubble.PointsUp && surface.NaturalSize == natural &&
                Math.Abs(bubble.ActualWidth - windowSize.Width) < .001 && Math.Abs(bubble.ActualHeight - windowSize.Height) < .001 &&
                bubble.CurrentFontSize == fontSize && bubble.CurrentVisualScale == fittedScale,
                "Changing thought-dot direction preserves native dimensions, artwork size and text scale", false);
            var textTransform = text.TransformToAncestor(content);
            var origin = textTransform.Transform(new Point(0, 0));
            var nextRow = textTransform.Transform(new Point(0, text.LineHeight));
            Check(((Transform)text.TransformToAncestor(bubble)).Value.M22 > 0 && nextRow.Y > origin.Y && text.Text == phrase,
                "Flipped artwork retains positive text-to-window vertical scale and original line order", false);
            var upText = Capture(content);
            text.Foreground = Brushes.Transparent;
            var upArtwork = Capture(content);
            text.Foreground = foreground;
            var surfaceTransform = surface.TransformToAncestor(content);
            double mirrorAxis = surfaceTransform.Transform(new Point(0, 0)).Y +
                surfaceTransform.Transform(new Point(0, surface.NaturalSize.Height)).Y;
            Check(MirrorMatches(downArtwork, upArtwork, mirrorAxis) > .995 && MirrorMatches(upArtwork, downArtwork, mirrorAxis) > .995,
                "Rendered paper and detached thought dots vertically mirror around the unchanged natural bounds", false);
            var downGlyphs = GlyphMask(downText, downArtwork);
            var upGlyphs = GlyphMask(upText, upArtwork);
            Check(TranslatedMaskMatches(downGlyphs, upGlyphs, downText.Width, downText.Height) > .98 &&
                TranslatedMaskMatches(upGlyphs, downGlyphs, upText.Width, upText.Height) > .98,
                "Rendered glyph shapes and multi-line reading order remain upright when the artwork points up", false);
            if (size == 32 && scale == 1.8 && phrase.Contains('\n'))
            {
                SaveCapture(downText.Bitmap, Path.Combine(output, "bubble-orientation-down.png"));
                SaveCapture(upText.Bitmap, Path.Combine(output, "bubble-orientation-up.png"));
            }
            bubble.SetPointsUp(false); Validate(bubble, $"restored direction {size}/{scale}/{phrase.Length}");
        }
        Check(!bubble.PointsUp, "Thought-dot direction can return below the bubble after crossing the screen edge");
    }
    private sealed record PixelCapture(int Width, int Height, byte[] Pixels, RenderTargetBitmap Bitmap);
    private static PixelCapture Capture(FrameworkElement content)
    {
        content.UpdateLayout();
        content.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        int width = (int)Math.Ceiling(content.ActualWidth), height = (int)Math.Ceiling(content.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        return new PixelCapture(width, height, pixels, bitmap);
    }
    private static double MirrorMatches(PixelCapture source, PixelCapture target, double axis)
    {
        int total = 0, matches = 0;
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            if (source.Pixels[(y * source.Width + x) * 4 + 3] <= 64) continue;
            total++;
            int reflectedY = (int)Math.Round(axis - y - 1);
            bool matched = false;
            for (int dy = -2; dy <= 2 && !matched; dy++)
            for (int dx = -1; dx <= 1 && !matched; dx++)
            {
                int targetX = x + dx, targetY = reflectedY + dy;
                matched = targetX >= 0 && targetX < target.Width && targetY >= 0 && targetY < target.Height &&
                    target.Pixels[(targetY * target.Width + targetX) * 4 + 3] > 64;
            }
            if (matched) matches++;
        }
        return total == 0 ? 0 : (double)matches / total;
    }
    private static bool[] GlyphMask(PixelCapture text, PixelCapture artwork)
    {
        var result = new bool[text.Width * text.Height];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = i * 4;
            result[i] = Math.Abs(text.Pixels[offset] - artwork.Pixels[offset]) +
                Math.Abs(text.Pixels[offset + 1] - artwork.Pixels[offset + 1]) +
                Math.Abs(text.Pixels[offset + 2] - artwork.Pixels[offset + 2]) > 30;
        }
        return result;
    }
    private static double TranslatedMaskMatches(bool[] source, bool[] target, int width, int height)
    {
        static (int X, int Y) Start(bool[] mask, int width, int height)
        {
            int left = width, top = height;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                if (mask[y * width + x]) { left = Math.Min(left, x); top = Math.Min(top, y); }
            return (left, top);
        }
        var sourceStart = Start(source, width, height); var targetStart = Start(target, width, height);
        int total = 0, matches = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            if (!source[y * width + x]) continue;
            total++; bool matched = false;
            for (int dy = -1; dy <= 1 && !matched; dy++) for (int dx = -1; dx <= 1 && !matched; dx++)
            {
                int tx = x - sourceStart.X + targetStart.X + dx, ty = y - sourceStart.Y + targetStart.Y + dy;
                matched = tx >= 0 && tx < width && ty >= 0 && ty < height && target[ty * width + tx];
            }
            if (matched) matches++;
        }
        return total == 0 ? 0 : (double)matches / total;
    }
    private static void SaveCapture(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void InstallDimensionCoercionRegression()
    {
        // Preserve Window's own native-size coercion; the test ceiling remains
        // disabled for every preceding real HWND/font/raster matrix scenario.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Window).TypeHandle);
        var widthCoercion = FrameworkElement.WidthProperty.GetMetadata(typeof(Window)).CoerceValueCallback;
        var heightCoercion = FrameworkElement.HeightProperty.GetMetadata(typeof(Window)).CoerceValueCallback;
        FrameworkElement.WidthProperty.OverrideMetadata(typeof(BubbleWindow),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.None, null, (owner, value) =>
            {
                var result = widthCoercion?.Invoke(owner, value) ?? value;
                if (result is double dimension && dimension > _coercedMaxWidth) { _dimensionCoercions++; return _coercedMaxWidth; }
                return result;
            }));
        FrameworkElement.HeightProperty.OverrideMetadata(typeof(BubbleWindow),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.None, null, (owner, value) =>
            {
                var result = heightCoercion?.Invoke(owner, value) ?? value;
                if (result is double dimension && dimension > _coercedMaxHeight) { _dimensionCoercions++; return _coercedMaxHeight; }
                return result;
            }));
    }
    private static void Validate(BubbleWindow bubble, string scenario)
    {
        bubble.UpdateLayout(); bubble.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)bubble.Content;
        var surface = (FrameworkElement)((System.Windows.Controls.Border)content).Child;
        var nativeHandle = new WindowInteropHelper(bubble).Handle;
        Check(GetClientRect(nativeHandle, out var client), "Get native client dimensions", false);
        double dpiScale = GetDpiForWindow(nativeHandle) / 96d;
        var ink = VisualTreeHelper.GetDescendantBounds(content);
        Check(!ink.IsEmpty && ink.Left >= 1 && ink.Top >= 1 &&
            ink.Right <= client.Right / dpiScale - 1 && ink.Bottom <= client.Bottom / dpiScale - 1,
            $"Native HWND clips descendant ink in {scenario}: {client.Right}x{client.Bottom}, ink {ink}", false);
        var surfaceInk = VisualTreeHelper.GetDescendantBounds(surface);
        var thoughtSurface = (ThoughtBubbleSurface)surface;
        Check(thoughtSurface.ContainsTextBounds(thoughtSurface.TextLayoutBounds),
            $"Text layout rectangle escapes the paper fill in {scenario}: {thoughtSurface.TextLayoutBounds}", false);
        var textInk = VisualTreeHelper.GetDescendantBounds(thoughtSurface.Child);
        if (!textInk.IsEmpty)
        {
            var textInkOnPaper = thoughtSurface.Child.TransformToAncestor(thoughtSurface).TransformBounds(textInk);
            Check(thoughtSurface.ContainsTextBounds(textInkOnPaper),
                $"Actual glyph ink escapes the paper fill in {scenario}: {textInkOnPaper}", false);
        }
        var clip = LayoutInformation.GetLayoutClip(surface);
        Check(clip == null || clip.Bounds.Contains(surfaceInk),
            $"WPF retains a stale child clip in {scenario}: clip {clip?.Bounds}, ink {surfaceInk}, actual {surface.ActualWidth}x{surface.ActualHeight}, natural {((ThoughtBubbleSurface)surface).NaturalSize}, scale {bubble.CurrentVisualScale}, HWND {client.Right}x{client.Bottom}, window {bubble.Width}x{bubble.Height} actual {bubble.ActualWidth}x{bubble.ActualHeight}, host {content.ActualWidth}x{content.ActualHeight} desired {content.DesiredSize}", false);
        _maximumNativeSizeDifference = Math.Max(_maximumNativeSizeDifference,
            Math.Max(Math.Abs(client.Right - content.ActualWidth * dpiScale), Math.Abs(client.Bottom - content.ActualHeight * dpiScale)));
        Check(client.Right + 1 >= content.ActualWidth * dpiScale && client.Bottom + 1 >= content.ActualHeight * dpiScale,
            $"Native HWND clips the layout size in {scenario}", false);
        var target = new RenderTargetBitmap(client.Right, client.Bottom, 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
        target.Render(content);
        var pixels = new byte[client.Right * client.Bottom * 4]; target.CopyPixels(pixels, client.Right * 4, 0);
        bool edgeInk = false;
        for (int x = 0; x < client.Right; x++) edgeInk |= pixels[x * 4 + 3] != 0 || pixels[((client.Bottom - 1) * client.Right + x) * 4 + 3] != 0;
        for (int y = 0; y < client.Bottom; y++) edgeInk |= pixels[(y * client.Right) * 4 + 3] != 0 || pixels[(y * client.Right + client.Right - 1) * 4 + 3] != 0;
        Check(!edgeInk, $"Rendered ink touches a native clipping edge in {scenario}", false);
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
