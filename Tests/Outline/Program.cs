using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BonbonPet.Core;

int checks = 0;
void Check(bool pass, string message)
{
    if (!pass) throw new InvalidOperationException(message);
    checks++;
}
void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 1e-8, $"{message}: {actual} vs {expected}");
BitmapSource Make(int width, int height, Func<int, int, byte> alpha, double dpiX = 96, double dpiY = 96)
{
    var pixels = new byte[width * height * 4];
    for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) pixels[(y * width + x) * 4 + 3] = alpha(x, y);
    var image = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Bgra32, null, pixels, width * 4);
    image.Freeze(); return image;
}

// Transparent canvas, off-center character, and low-alpha shadow must not lead collisions.
var rectangle = Make(100, 80, (x, y) => x is >= 10 and < 60 && y is >= 20 and < 50 ? (byte)255 : y >= 60 ? (byte)20 : (byte)0);
var outline = new SpriteOutline(rectangle);
var bounds = outline.GetBounds(200, 160, 1, 1, 0, 0);
Near(bounds.Left, -80, "Transparent left margin"); Near(bounds.Right, 20, "Transparent right margin");
Near(bounds.Top, -40, "Transparent top margin"); Near(bounds.Bottom, 20, "Faded shadow excluded");
Check(outline.VertexCount == 4, "Rectangular outline should have only four vertices");
var reusablePixels = new byte[100 * 80 * 4 + 1024]; Array.Fill(reusablePixels, (byte)255);
var reusableOutline = new SpriteOutline(rectangle, scratch: reusablePixels);
Check(reusableOutline.GetBounds(200, 160, 1, 1, 0, 0) == bounds, "Reusable larger scratch buffer preserves exact bounds");
Check(new SpriteOutline(rectangle, scratch: new byte[1]).GetBounds(200, 160, 1, 1, 0, 0) == bounds, "Too-small scratch buffer safely falls back");
bounds = outline.GetBounds(200, 160, 1, 1, 90, 7);
Near(bounds.Left, -20, "Rotated left"); Near(bounds.Right, 40, "Rotated right");
Near(bounds.Top, -73, "Rotated top with bob"); Near(bounds.Bottom, 27, "Rotated bottom with bob");
Check(new SpriteOutline(Make(8, 8, (_, _) => 0)).GetBounds(80, 80, 1, 1, 0, 0).IsEmpty, "Wholly transparent frame");
Check(outline.GetBounds(0, 10, 1, 1, 0, 0).IsEmpty, "Unavailable image layout");

// Image Stretch=Uniform fits the source's DIP dimensions, including anisotropic
// DPI metadata. Using the raw pixel aspect incorrectly doubles this image width.
var unequalDpi = Make(100, 80, (_, _) => 255, 192, 96);
var unequalDpiBounds = new SpriteOutline(unequalDpi).GetBounds(200, 160, 1, 1, 0, 0);
Near(unequalDpiBounds.Left, -50, "Unequal DPI visible left");
Near(unequalDpiBounds.Right, 50, "Unequal DPI visible right");
Near(unequalDpiBounds.Top, -80, "Unequal DPI visible top");
Near(unequalDpiBounds.Bottom, 80, "Unequal DPI visible bottom");

// Compare hull extrema against all four corners of every independently generated
// visible pixel. WPF's Matrix performs the transforms in the same documented order.
var random = new Random(114);
for (int sample = 0; sample < 25; sample++)
{
    const int width = 37, height = 29;
    var alpha = new byte[width * height];
    for (int i = 0; i < alpha.Length; i++) alpha[i] = random.NextDouble() > .65 ? (byte)255 : (byte)0;
    var bitmap = Make(width, height, (x, y) => alpha[y * width + x]);
    var shape = new SpriteOutline(bitmap);
    for (int angle = 0; angle < 360; angle += 7)
    {
        double displayWidth = 93, displayHeight = 117, sx = .82, sy = 1.19, bob = -11;
        double fit = Math.Min(displayWidth / width, displayHeight / height);
        var matrix = Matrix.Identity; matrix.Scale(fit * sx, fit * sy); matrix.Rotate(angle); matrix.Translate(0, bob);
        double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (alpha[y * width + x] < 32) continue;
                for (int cy = 0; cy <= 1; cy++)
                    for (int cx = 0; cx <= 1; cx++)
                    {
                        Point p = matrix.Transform(new Point(x + cx - width * .5, y + cy - height * .5));
                        left = Math.Min(left, p.X); top = Math.Min(top, p.Y);
                        right = Math.Max(right, p.X); bottom = Math.Max(bottom, p.Y);
                    }
            }
        var actual = shape.GetBounds(displayWidth, displayHeight, sx, sy, angle, bob);
        Near(actual.Left, left, "Hull vs pixels left"); Near(actual.Top, top, "Hull vs pixels top");
        Near(actual.Right, right, "Hull vs pixels right"); Near(actual.Bottom, bottom, "Hull vs pixels bottom");
    }
}

int largestHull = 0;
var defaultFrames = new List<BitmapSource>();
foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Assets"), "frame-*.png"))
{
    var bitmap = new BitmapImage(new Uri(file)); bitmap.Freeze();
    defaultFrames.Add(bitmap);
    var shape = new SpriteOutline(bitmap); largestHull = Math.Max(largestHull, shape.VertexCount);
    Check(!shape.IsEmpty, "Default GIF frame outline missing");
    var actual = shape.GetBounds(220 * bitmap.PixelWidth / bitmap.PixelHeight, 220, 1, 1, 0, 0);
    Check(actual.Width < 220 * 1.1 && actual.Height <= 220, "Default GIF outline larger than visible image");
}

// Exercise WPF's actual Image arrangement and rasterization, not only a second
// hand-written matrix. This catches a mismatch between source DPI, the arranged
// image size, its transform origin, and the offsets used for desktop contact.
Exception? renderingFailure = null;
double largestRasterDifference = 0;
var renderThread = new Thread(() =>
{
    try
    {
        void RenderAndCompare(BitmapSource source, double size, double scaleX, double scaleY, double angle, double bob)
        {
            double canvasSize = Math.Ceiling(size * 1.85);
            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(scaleX, scaleY));
            transform.Children.Add(new RotateTransform(angle));
            transform.Children.Add(new TranslateTransform(0, bob));
            var image = new Image
            {
                Source = source, Width = size, Height = size, Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = transform
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.LowQuality);
            var grid = new Grid { Width = canvasSize, Height = canvasSize, UseLayoutRounding = true };
            grid.Children.Add(image);
            grid.Measure(new Size(canvasSize, canvasSize));
            grid.Arrange(new Rect(0, 0, canvasSize, canvasSize));
            grid.UpdateLayout();
            int side = (int)canvasSize;
            var target = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
            target.Render(grid);
            var pixels = new byte[side * side * 4];
            target.CopyPixels(pixels, side * 4, 0);
            int left = side, top = side, right = -1, bottom = -1;
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                    if (pixels[(y * side + x) * 4 + 3] >= 32)
                    {
                        left = Math.Min(left, x); top = Math.Min(top, y);
                        right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                    }
            Check(right >= left, "WPF render unexpectedly transparent");
            var expected = new SpriteOutline(source).GetBounds(size, size, scaleX, scaleY, angle, bob);
            double center = canvasSize / 2;
            var rendered = new Rect(left - center, top - center, right - left + 1, bottom - top + 1);
            double difference = new[]
            {
                Math.Abs(expected.Left - rendered.Left), Math.Abs(expected.Top - rendered.Top),
                Math.Abs(expected.Right - rendered.Right), Math.Abs(expected.Bottom - rendered.Bottom)
            }.Max();
            largestRasterDifference = Math.Max(largestRasterDifference, difference);
            Check(difference <= 2.01, $"WPF rendered bounds differ from collision offsets by {difference:F3} DIP (angle {angle}, DPI {source.DpiX}/{source.DpiY}, arranged {image.ActualWidth}/{image.ActualHeight})");
        }
        RenderAndCompare(unequalDpi, 220, 1, 1, 0, 0);
        RenderAndCompare(unequalDpi, 220, 1.25, .65, 41, -8);
        RenderAndCompare(rectangle, 220, 1.1, .8, 27, -4);
        foreach (var frame in defaultFrames)
            foreach (int angle in new[] { 0, 37, 90, 163, 251 })
            {
                RenderAndCompare(frame, 220, 1, 1, angle, 0);
                RenderAndCompare(frame, 220, 1.34, .6, angle, -9);
            }
    }
    catch (Exception ex) { renderingFailure = ex; }
});
renderThread.SetApartmentState(ApartmentState.STA);
renderThread.Start(); renderThread.Join();
if (renderingFailure is not null) throw new InvalidOperationException("WPF rendered collision verification failed", renderingFailure);

long before = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 10000; i++) _ = outline.GetBounds(200, 160, 1, 1, i % 360, 0);
Check(GC.GetAllocatedBytesForCurrentThread() == before, "Hot path allocated memory");
Console.WriteLine($"Outline checks passed: {checks:N0}; largest default-frame hull: {largestHull} vertices; maximum WPF raster difference: {largestRasterDifference:F3} DIP; no allocations for 10,000 queries.");
