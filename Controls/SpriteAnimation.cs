using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace BonbonPet.Controls;

/// <summary>A small immutable frame cache. Advances only when the pet render loop asks.</summary>
public sealed class SpriteAnimation : IDisposable
{
    private const int MaxFileBytes = 32 * 1024 * 1024;
    private const int MaxFrames = 180;
    private const int MaxCachedPixels = 10_000_000;
    private readonly BitmapSource[] _frames;
    private readonly double[] _durations;
    private readonly double _cycleSeconds;
    private int _index;
    private double _elapsed;
    private static readonly Lazy<SpriteAnimation> DefaultCache = new(LoadDefaultFrames);

    public IReadOnlyList<BitmapSource> Frames => _frames;
    public BitmapSource CurrentFrame => _frames[_index];
    public double AspectRatio => (double)_frames[0].PixelWidth / _frames[0].PixelHeight;
    public bool IsAnimated => _frames.Length > 1;
    public string SourcePath { get; }

    private SpriteAnimation(BitmapSource[] frames, double[] durations, string path)
    {
        _frames = frames;
        _durations = durations;
        _cycleSeconds = durations.Sum();
        SourcePath = path;
    }

    public void Advance(double deltaSeconds)
    {
        if (!IsAnimated || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        _elapsed += deltaSeconds % _cycleSeconds;
        while (_elapsed >= _durations[_index])
        {
            _elapsed -= _durations[_index];
            _index = (_index + 1) % _frames.Length;
        }
    }

    public SpriteAnimation Fork() => new(_frames, _durations, SourcePath);
    public static SpriteAnimation LoadDefault() => DefaultCache.Value.Fork();
    private static SpriteAnimation LoadDefaultFrames()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "default");
        var manifest = JsonSerializer.Deserialize<FrameManifest>(File.ReadAllText(Path.Combine(folder, "manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("默认角色素材损坏。请重新解压桌宠文件夹。");
        if (manifest.Frames.Length is 0 or > MaxFrames || manifest.Frames.Length != manifest.DurationsMs.Length)
            throw new InvalidDataException("默认动画帧数据不完整。");
        var frames = new BitmapSource[manifest.Frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            using var stream = File.OpenRead(Path.Combine(folder, manifest.Frames[i]));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            frames[i] = image;
        }
        return new SpriteAnimation(frames, manifest.DurationsMs.Select(ms => Math.Clamp(ms / 1000.0, .02, 10)).ToArray(),
            Path.Combine(AppContext.BaseDirectory, "Assets", "default-original.gif"));
    }

    /// <summary>PNG, animated GIF, JPEG and BMP. GIF composition is performed by the Windows GDI+ decoder.</summary>
    public static SpriteAnimation Load(string path)
    {
        path = Path.GetFullPath(path);
        if (string.Equals(path, Path.Combine(AppContext.BaseDirectory, "Assets", "default-original.gif"), StringComparison.OrdinalIgnoreCase))
            return LoadDefault();
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("找不到这个角色素材。", path);
        if (info.Length > MaxFileBytes) throw new InvalidDataException("素材超过 32 MB，请选择较小的 GIF 或图片。");
        string extension = info.Extension.ToLowerInvariant();
        if (extension is not (".gif" or ".png" or ".jpg" or ".jpeg" or ".bmp"))
            throw new InvalidDataException("支持透明 PNG、动态 GIF、JPG 和 BMP 素材。");

        using var stream = File.OpenRead(path);
        using var source = Drawing.Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
        if (source.Width > 4096 || source.Height > 4096)
            throw new InvalidDataException("素材长宽需要小于 4096 像素，请先缩小图片。");
        bool animated = source.FrameDimensionsList.Contains(FrameDimension.Time.Guid);
        int count = animated ? source.GetFrameCount(FrameDimension.Time) : 1;
        if (count > MaxFrames)
            throw new InvalidDataException($"这个 GIF 有 {count} 帧。为节约内存，第一版支持最多 {MaxFrames} 帧，请截取较短片段。");
        int maxSide = Math.Min(384, (int)Math.Sqrt((double)MaxCachedPixels / count));
        double scale = Math.Min(1, (double)maxSide / Math.Max(source.Width, source.Height));
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var frames = new BitmapSource[count];
        var durations = Enumerable.Repeat(.1, count).ToArray();
        if (animated)
        {
            try
            {
                byte[]? delay = source.GetPropertyItem(0x5100)?.Value;
                if (delay is not null)
                    for (int i = 0; i < count && i * 4 + 4 <= delay.Length; i++)
                        durations[i] = Math.Clamp(BitConverter.ToUInt32(delay, i * 4) / 100.0, .02, 10);
            }
            catch (ArgumentException) { /* Missing GIF delay metadata uses 100 ms. */ }
        }
        using var canvas = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Drawing.Graphics.FromImage(canvas);
        graphics.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.CompositingQuality = Drawing.Drawing2D.CompositingQuality.HighQuality;
        graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < count; i++)
        {
            if (animated) source.SelectActiveFrame(FrameDimension.Time, i);
            graphics.Clear(Drawing.Color.Transparent);
            graphics.DrawImage(source, new Drawing.Rectangle(0, 0, width, height));
            var data = canvas.LockBits(new Drawing.Rectangle(0, 0, width, height), ImageLockMode.ReadOnly,
                Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                for (int row = 0; row < height; row++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), pixels, row * width * 4, width * 4);
            }
            finally { canvas.UnlockBits(data); }
            var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
            frame.Freeze();
            frames[i] = frame;
        }
        return new SpriteAnimation(frames, durations, path);
    }

    public void Dispose() { /* Frozen managed frames are reclaimed when this cache is released. */ }

    private sealed class FrameManifest
    {
        public FrameManifest() { }
        public string[] Frames { get; set; } = [];
        public double[] DurationsMs { get; set; } = [];
    }
}
