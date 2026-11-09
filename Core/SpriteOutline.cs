using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BonbonPet.Core;

/// <summary>
/// The convex hull of visible pixel edges in one immutable sprite frame.
/// Built once when loading a character; each rotation query visits only hull vertices.
/// </summary>
public sealed class SpriteOutline
{
    private readonly Point[] _vertices;
    private readonly int _width, _height;
    private readonly double _dipWidth, _dipHeight;
    public int VertexCount => _vertices.Length;
    public bool IsEmpty => _vertices.Length == 0;
    public byte AlphaThreshold { get; }

    public SpriteOutline(BitmapSource frame, byte alphaThreshold = 32, byte[]? scratch = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _width = frame.PixelWidth;
        _height = frame.PixelHeight;
        // WPF fits Image.Source in its natural DIP size. Imported images can
        // have different horizontal and vertical DPI, so pixel aspect alone
        // does not describe the image that WPF actually draws.
        _dipWidth = frame.Width;
        _dipHeight = frame.Height;
        AlphaThreshold = Math.Max((byte)1, alphaThreshold);
        BitmapSource pixelsSource = frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Pbgra32
            ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int stride = checked(_width * 4);
        int needed = checked(stride * _height);
        var pixels = scratch is not null && scratch.Length >= needed ? scratch : new byte[needed];
        pixelsSource.CopyPixels(pixels, stride, 0);

        // Four corners of the visible span of each scanline have exactly the
        // same support in any direction as every visible pixel corner on it.
        // This keeps sorting bounded by image height, rather than opaque area.
        var candidates = new List<Point>(_height * 4);
        for (int y = 0; y < _height; y++)
        {
            int left = _width, right = -1;
            int row = y * stride;
            for (int x = 0; x < _width; x++)
            {
                if (pixels[row + x * 4 + 3] < AlphaThreshold) continue;
                left = Math.Min(left, x);
                right = x;
            }
            if (right < left) continue;
            candidates.Add(new Point(left, y));
            candidates.Add(new Point(right + 1, y));
            candidates.Add(new Point(left, y + 1));
            candidates.Add(new Point(right + 1, y + 1));
        }
        _vertices = BuildHull(candidates);
        // Pixel-edge coordinates centered on the actual image content. Its
        // letterboxing is centered too, so no separate padding translation is needed.
        for (int i = 0; i < _vertices.Length; i++)
            _vertices[i] = new Point(_vertices[i].X - _width * .5, _vertices[i].Y - _height * .5);
    }

    /// <summary>
    /// Returns visible edge offsets around the pet's center in display DIPs.
    /// Matches Image Stretch=Uniform, followed by scale, rotation, and vertical bob.
    /// Returns Rect.Empty for a wholly transparent sprite or an unavailable layout.
    /// </summary>
    public Rect GetBounds(double displayWidth, double displayHeight, double scaleX,
        double scaleY, double angleDegrees, double bobY)
    {
        if (IsEmpty || !double.IsFinite(displayWidth) || !double.IsFinite(displayHeight) ||
            displayWidth <= 0 || displayHeight <= 0) return Rect.Empty;
        if (!double.IsFinite(scaleX)) scaleX = 1;
        if (!double.IsFinite(scaleY)) scaleY = 1;
        if (!double.IsFinite(angleDegrees)) angleDegrees = 0;
        if (!double.IsFinite(bobY)) bobY = 0;
        double fit = Math.Min(displayWidth / _dipWidth, displayHeight / _dipHeight);
        double fx = fit * _dipWidth / _width * scaleX;
        double fy = fit * _dipHeight / _height * scaleY;
        var (sin, cos) = Math.SinCos(angleDegrees * Math.PI / 180);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (Point point in _vertices)
        {
            double x = point.X * fx, y = point.Y * fy;
            double rotatedX = x * cos - y * sin;
            double rotatedY = x * sin + y * cos + bobY;
            minX = Math.Min(minX, rotatedX); maxX = Math.Max(maxX, rotatedX);
            minY = Math.Min(minY, rotatedY); maxY = Math.Max(maxY, rotatedY);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static Point[] BuildHull(List<Point> points)
    {
        if (points.Count == 0) return Array.Empty<Point>();
        points.Sort(static (a, b) =>
        {
            int compare = a.X.CompareTo(b.X);
            return compare != 0 ? compare : a.Y.CompareTo(b.Y);
        });
        int unique = 0;
        for (int i = 0; i < points.Count; i++)
            if (unique == 0 || points[i] != points[unique - 1]) points[unique++] = points[i];
        var hull = new Point[unique * 2];
        int count = 0;
        for (int i = 0; i < unique; i++)
        {
            while (count >= 2 && Cross(hull[count - 2], hull[count - 1], points[i]) <= 0) count--;
            hull[count++] = points[i];
        }
        int upperStart = count + 1;
        for (int i = unique - 2; i >= 0; i--)
        {
            while (count >= upperStart && Cross(hull[count - 2], hull[count - 1], points[i]) <= 0) count--;
            hull[count++] = points[i];
        }
        if (count > 1) count--; // Remove repeated first vertex.
        Array.Resize(ref hull, count);
        return hull;
    }

    private static double Cross(Point origin, Point a, Point b) =>
        (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);
}
