using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BonbonPet.Services;
using BonbonPet.Core;

namespace BonbonPet;

/// <summary>A small vector thought bubble styled after the supplied reference.</summary>
public sealed class BubbleWindow : Window
{
    private readonly TextBlock _text;
    private readonly ThoughtBubbleSurface _surface;
    private readonly Border _host;
    private readonly ScaleTransform _visualTransform = new(1, 1);
    private bool _prepared;
    private string? _configuredFont;
    private double _requestedVisualScale = 1;
    private double _maxWidth = double.PositiveInfinity, _maxHeight = double.PositiveInfinity;
    private bool _nativeSizeLimited;
    private (double Left, double Top, double Right, double Bottom) _nativeLimitMonitor;
    internal string CurrentText => _text.Text;
    internal double CurrentFontSize => _text.FontSize * CurrentVisualScale;
    internal int CurrentFontWeight => _text.FontWeight.ToOpenTypeWeight();
    internal double CurrentVisualScale => _visualTransform.ScaleX;
    internal double RequestedVisualScale => _requestedVisualScale;

    public BubbleWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.Manual;
        IsHitTestVisible = false;
        Width = 240;
        Height = 158;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        _text = new TextBlock
        {
            Text = "欧耶耶~",
            FontSize = 13.5,
            Foreground = ThoughtBubbleSurface.Ink,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            LineHeight = 21,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };
        _surface = new ThoughtBubbleSurface(_text);
        _surface.LayoutTransform = _visualTransform;
        // This final inset is in window DIPs, so even a very small bubble retains
        // room for raster antialiasing around the scaled vector's measured ink.
        _host = new Border { Child = _surface, Padding = new Thickness(2) };
        Content = _host;
        SourceInitialized += (_, _) => DesktopService.MakeToolWindow(new WindowInteropHelper(this).Handle, clickThrough: true);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) LayoutText(); }));
    }

    /// <summary>Warm the HWND, text layout and drawing while the bubble stays hidden.</summary>
    public void Prepare()
    {
        if (_prepared) return;
        new WindowInteropHelper(this).EnsureHandle();
        // Warm a detached visual. Manually arranging the live content while its
        // HWND is hidden leaves WPF with the warmup window's stale layout clip.
        var warmText = new TextBlock
        {
            Text = _text.Text, FontFamily = FontFamily, FontSize = _text.FontSize,
            FontWeight = _text.FontWeight,
            Foreground = ThoughtBubbleSurface.Ink, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, LineHeight = _text.LineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };
        var warmSurface = new ThoughtBubbleSurface(warmText);
        warmSurface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        warmSurface.Arrange(new Rect(warmSurface.DesiredSize));
        var warmup = new RenderTargetBitmap((int)Math.Ceiling(warmSurface.ActualWidth), (int)Math.Ceiling(warmSurface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        warmup.Render(warmSurface);
        _prepared = true;
    }

    public void Say(string text)
    {
        Prepare();
        _text.Text = text ?? "";
        // Set the requested native size before first show, then let the visible
        // window perform its own final arrange. Hidden live visuals are never
        // manually arranged into a temporary warmup rectangle.
        LayoutText();
        if (!IsVisible) Show();
        LayoutText();
    }

    public void SetAppearance(string family, double baseFontSize, double visualScale = 1, int fontWeight = 400)
    {
        double size = double.IsFinite(baseFontSize) ? Math.Clamp(baseFontSize, 10, 32) : PetSettings.DefaultBubbleFontSize;
        visualScale = double.IsFinite(visualScale) ? Math.Clamp(visualScale, .1, 10) : 1;
        fontWeight = Math.Clamp(fontWeight, 100, 900);
        bool changed = false;
        if (!string.Equals(_configuredFont, family, StringComparison.Ordinal))
        {
            FontFamily = Fonts.SystemFontFamilies.FirstOrDefault(font => string.Equals(font.Source, family, StringComparison.OrdinalIgnoreCase))
                ?? new FontFamily(PetSettings.DefaultBubbleFontFamily);
            _configuredFont = family; changed = true;
        }
        if (_text.FontSize != size)
        {
            _text.FontSize = size; _text.LineHeight = size * 1.55; changed = true;
        }
        if (_text.FontWeight.ToOpenTypeWeight() != fontWeight)
        {
            _text.FontWeight = FontWeight.FromOpenTypeWeight(fontWeight); changed = true;
        }
        if (_requestedVisualScale != visualScale)
        {
            _requestedVisualScale = visualScale; changed = true;
        }
        if (changed) LayoutText();
    }

    /// <summary>Fit the complete vector and text uniformly within a screen budget.</summary>
    public void FitToBounds(double widthDip, double heightDip)
    {
        widthDip = double.IsFinite(widthDip) ? Math.Max(8, widthDip) : double.PositiveInfinity;
        heightDip = double.IsFinite(heightDip) ? Math.Max(8, heightDip) : double.PositiveInfinity;
        if (_maxWidth == widthDip && _maxHeight == heightDip) return;
        _maxWidth = widthDip; _maxHeight = heightDip;
        LayoutText();
    }

    private void LayoutText()
    {
        var unlimited = new Size(double.PositiveInfinity, double.PositiveInfinity);
        double availableWidth = _maxWidth, availableHeight = _maxHeight;
        bool nativeLimited = false;
        // First attachment to a native DPI source can change text metrics. Size
        // from the surface's ink bounds and converge after the window's arrange,
        // rather than retaining a LayoutTransform's earlier constrained desire.
        for (int pass = 0; pass < 6; pass++)
        {
            _surface.InvalidateMeasure(); _surface.Measure(unlimited);
            var natural = _surface.NaturalSize;
            double fittedScale = Math.Min(_requestedVisualScale,
                Math.Min((availableWidth - 4) / natural.Width, (availableHeight - 4) / natural.Height));
            if (_visualTransform.ScaleX != fittedScale)
                _visualTransform.ScaleX = _visualTransform.ScaleY = fittedScale;
            double requestedWidth = Math.Ceiling(natural.Width * fittedScale + 4 - .0000001);
            double requestedHeight = Math.Ceiling(natural.Height * fittedScale + 4 - .0000001);
            Width = requestedWidth;
            Height = requestedHeight;
            // Let the visible window own the final arrange and clipping rectangle.
            _host.InvalidateMeasure(); _host.InvalidateArrange(); _surface.InvalidateArrange();
            InvalidateMeasure();
            if (!IsVisible) break;
            UpdateLayout();
            // Windows can cap a layered HWND below its requested WPF size on
            // a small monitor or desktop session. Its actual client then owns
            // a smaller layout clip. Fit every element into that real budget.
            // Keep this limit local: a later layout on a larger monitor retries
            // the requested visual scale instead of retaining an old ceiling.
            bool clientLimited = false;
            if (_host.ActualWidth > 0 && _host.ActualWidth + .75 < requestedWidth)
            {
                availableWidth = Math.Min(availableWidth, Math.Max(8, _host.ActualWidth));
                clientLimited = true;
            }
            if (_host.ActualHeight > 0 && _host.ActualHeight + .75 < requestedHeight)
            {
                availableHeight = Math.Min(availableHeight, Math.Max(8, _host.ActualHeight));
                clientLimited = true;
            }
            if (clientLimited) { nativeLimited = true; continue; }
            if (_surface.NaturalSize == natural) break;
        }
        if (IsVisible)
        {
            _nativeSizeLimited = nativeLimited;
            if (nativeLimited) _nativeLimitMonitor = DesktopService.GetMonitorBounds(new WindowInteropHelper(this).Handle);
        }
    }

    public void Place(double xPixels, double yPixels, bool topmost)
    {
        var handle = new WindowInteropHelper(this).Handle;
        DesktopService.PositionWindow(handle, xPixels, yPixels, topmost);
        if (_nativeSizeLimited && DesktopService.GetMonitorBounds(handle) != _nativeLimitMonitor) LayoutText();
    }
}

internal sealed class ThoughtBubbleSurface : Decorator
{
    public static readonly Brush Ink = FreezeBrush(Color.FromRgb(43, 57, 103));
    private static readonly Brush Paper = FreezeBrush(Color.FromRgb(255, 254, 255));
    private static readonly Pen Outline = FreezePen(Ink, 5.5);
    private static readonly Pen InnerEdge = FreezePen(FreezeBrush(Color.FromRgb(229, 249, 249)), 2.3);
    private static readonly Pen LargeDotOutline = FreezePen(Ink, 4.8);
    private static readonly Pen SmallDotOutline = FreezePen(Ink, 3.9);
    private Geometry? _body;
    private Geometry? _inner;
    private TranslateTransform? _inkTranslation;
    private double _bodyWidth, _bodyHeight;
    private Point _firstDot, _secondDot;
    internal Size NaturalSize { get; private set; }
    internal Rect TextLayoutBounds { get; private set; }

    public ThoughtBubbleSurface(TextBlock text) => Child = text;

    protected override Size MeasureOverride(Size constraint)
    {
        Child.Measure(new Size(220, double.PositiveInfinity));
        double width = Math.Clamp(Child.DesiredSize.Width + 76, 210, 300);
        Child.Measure(new Size(width - 76, double.PositiveInfinity));
        double textHeight = Child.DesiredSize.Height;
        double height = Math.Max(110, Math.Max(width * .57, textHeight + 58));
        Geometry candidate = Body(width - 7, height - 7, 3.5, 3.5);
        // A tall ellipse narrows around the first and last lines, even when its
        // bounding box is taller than the text. Keep space for the full text
        // rectangle and glyph overhangs inside the actual paper fill.
        for (int pass = 0; textHeight > 0 && pass < 32; pass++)
        {
            var textArea = new Rect(38, TextTop(height, textHeight), width - 76, textHeight);
            textArea.Inflate(8, 8);
            if (candidate.FillContainsWithDetail(new RectangleGeometry(textArea)) == IntersectionDetail.FullyContains) break;
            height += 12;
            candidate = Body(width - 7, height - 7, 3.5, 3.5);
        }
        if (width != _bodyWidth || height != _bodyHeight || _body == null)
        {
            _bodyWidth = width;
            _bodyHeight = height;
            _body = candidate;
            _inner = Body(width - 17, height - 17, 8.5, 8.5);
            _firstDot = new Point(width * .383, height + 28);
            _secondDot = new Point(width * .49, height + 52);
            // Bézier control points can extend beyond nominal body dimensions.
            // Measure the actual stroked geometry, then union the detached dots.
            var inkBounds = _body.GetRenderBounds(Outline);
            inkBounds.Union(_inner.GetRenderBounds(InnerEdge));
            inkBounds.Union(new EllipseGeometry(_firstDot, 12.5, 9.5).GetRenderBounds(LargeDotOutline));
            inkBounds.Union(new EllipseGeometry(_secondDot, 8.4, 6.3).GetRenderBounds(SmallDotOutline));
            const double inset = 3;
            _inkTranslation = new TranslateTransform(inset - inkBounds.Left, inset - inkBounds.Top);
            _inkTranslation.Freeze();
            NaturalSize = new Size(inkBounds.Width + inset * 2, inkBounds.Height + inset * 2);
            InvalidateVisual();
        }
        return NaturalSize;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        double top = TextTop(_bodyHeight, Child.DesiredSize.Height);
        TextLayoutBounds = new Rect(38 + _inkTranslation!.X, top + _inkTranslation.Y,
            _bodyWidth - 76, Child.DesiredSize.Height);
        Child.Arrange(TextLayoutBounds);
        return arrangeSize;
    }

    internal bool ContainsTextBounds(Rect bounds)
    {
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return true;
        if (_body == null || _inkTranslation == null) return false;
        bounds.Offset(-_inkTranslation.X, -_inkTranslation.Y);
        return _body.FillContainsWithDetail(new RectangleGeometry(bounds)) == IntersectionDetail.FullyContains;
    }

    private static double TextTop(double bodyHeight, double textHeight) =>
        Math.Max(20, (bodyHeight - textHeight) / 2 - 3);

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (_body == null || _inner == null || _inkTranslation == null) return;
        drawing.PushTransform(_inkTranslation);
        drawing.DrawGeometry(Paper, Outline, _body);
        drawing.DrawGeometry(null, InnerEdge, _inner);
        // Two detached ovals point down towards the character, as in the reference.
        drawing.DrawEllipse(Paper, LargeDotOutline, _firstDot, 12.5, 9.5);
        drawing.DrawEllipse(null, InnerEdge, _firstDot, 8.6, 5.6);
        drawing.DrawEllipse(Paper, SmallDotOutline, _secondDot, 8.4, 6.3);
        drawing.DrawEllipse(null, InnerEdge, _secondDot, 5.2, 3.1);
        drawing.Pop();
    }

    private static Geometry Body(double width, double height, double left, double top)
    {
        var geometry = new StreamGeometry();
        // Short bubbles retain the supplied ellipse. Longer messages extend
        // its middle while the rounded ends retain their natural proportions,
        // keeping the text column inside the paper without gigantic endcaps.
        double roundedHeight = Math.Min(height, width * .57);
        double extension = height - roundedHeight;
        Point P(double x, double y, bool lower = false) => new(left + x * width,
            top + y * roundedHeight + (lower ? extension : 0));
        using (var context = geometry.Open())
        {
            context.BeginFigure(P(.49, .014), true, true);
            context.BezierTo(P(.77, -.018), P(.971, .18), P(.984, .46), true, false);
            if (extension > 0) context.LineTo(P(.984, .46, true), true, false);
            context.BezierTo(P(1.015, .733, true), P(.835, .965, true), P(.575, .975, true), true, false);
            context.BezierTo(P(.527, .98, true), P(.479, .977, true), P(.432, .974, true), true, false);
            context.BezierTo(P(.418, 1.075, true), P(.322, 1.105, true), P(.289, .963, true), true, false);
            context.BezierTo(P(.135, .93, true), P(.014, .752, true), P(.012, .535, true), true, false);
            if (extension > 0) context.LineTo(P(.012, .535), true, false);
            context.BezierTo(P(-.008, .28), P(.205, -.003), P(.49, .014), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Brush FreezeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
    private static Pen FreezePen(Brush brush, double width)
    {
        var pen = new Pen(brush, width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}
