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
    private bool _prepared;
    private string? _configuredFont;
    internal string CurrentText => _text.Text;
    internal double CurrentFontSize => _text.FontSize;

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
        Content = _surface;
        SourceInitialized += (_, _) => DesktopService.MakeToolWindow(new WindowInteropHelper(this).Handle, clickThrough: true);
    }

    /// <summary>Warm the HWND, text layout and drawing while the bubble stays hidden.</summary>
    public void Prepare()
    {
        if (_prepared) return;
        new WindowInteropHelper(this).EnsureHandle();
        LayoutText();
        // Pay the first glyph/vector setup cost during startup, not on a click.
        var warmup = new RenderTargetBitmap((int)Math.Ceiling(Width), (int)Math.Ceiling(Height), 96, 96, PixelFormats.Pbgra32);
        warmup.Render(_surface);
        _prepared = true;
    }

    public void Say(string text)
    {
        Prepare();
        _text.Text = text ?? "";
        LayoutText();
        if (!IsVisible) Show();
        UpdateLayout();
    }

    public void SetAppearance(string family, double size)
    {
        size = double.IsFinite(size) ? Math.Clamp(size, 10, 32) : PetSettings.DefaultBubbleFontSize;
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
        if (changed && _prepared) LayoutText();
    }

    private void LayoutText()
    {
        _surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = _surface.DesiredSize;
        Width = Math.Ceiling(desired.Width);
        Height = Math.Ceiling(desired.Height);
        _surface.Arrange(new Rect(0, 0, Width, Height));
        _surface.UpdateLayout();
    }

    public void Place(double xPixels, double yPixels, bool topmost) =>
        DesktopService.PositionWindow(new WindowInteropHelper(this).Handle, xPixels, yPixels, topmost);
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
    private double _bodyWidth, _bodyHeight;

    public ThoughtBubbleSurface(TextBlock text) => Child = text;

    protected override Size MeasureOverride(Size constraint)
    {
        Child.Measure(new Size(220, double.PositiveInfinity));
        double width = Math.Clamp(Child.DesiredSize.Width + 76, 210, 300);
        Child.Measure(new Size(width - 76, double.PositiveInfinity));
        double height = Math.Max(110, Math.Max(width * .57, Child.DesiredSize.Height + 58));
        if (width != _bodyWidth || height != _bodyHeight || _body == null)
        {
            _bodyWidth = width;
            _bodyHeight = height;
            _body = Body(width - 7, height - 7, 3.5, 3.5);
            _inner = Body(width - 17, height - 17, 8.5, 8.5);
            InvalidateVisual();
        }
        return new Size(width, height + 64);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        double top = Math.Max(20, (_bodyHeight - Child.DesiredSize.Height) / 2 - 3);
        Child.Arrange(new Rect(38, top, _bodyWidth - 76, Child.DesiredSize.Height));
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (_body == null || _inner == null) return;
        drawing.DrawGeometry(Paper, Outline, _body);
        drawing.DrawGeometry(null, InnerEdge, _inner);
        // Two detached ovals point down towards the character, as in the reference.
        var first = new Point(_bodyWidth * .383, _bodyHeight + 28);
        var second = new Point(_bodyWidth * .49, _bodyHeight + 52);
        drawing.DrawEllipse(Paper, LargeDotOutline, first, 12.5, 9.5);
        drawing.DrawEllipse(null, InnerEdge, first, 8.6, 5.6);
        drawing.DrawEllipse(Paper, SmallDotOutline, second, 8.4, 6.3);
        drawing.DrawEllipse(null, InnerEdge, second, 5.2, 3.1);
    }

    private static Geometry Body(double width, double height, double left, double top)
    {
        var geometry = new StreamGeometry();
        Point P(double x, double y) => new(left + x * width, top + y * height);
        using (var context = geometry.Open())
        {
            context.BeginFigure(P(.49, .014), true, true);
            context.BezierTo(P(.77, -.018), P(.971, .18), P(.984, .46), true, false);
            context.BezierTo(P(1.015, .733), P(.835, .965), P(.575, .975), true, false);
            context.BezierTo(P(.527, .98), P(.479, .977), P(.432, .974), true, false);
            context.BezierTo(P(.418, 1.075), P(.322, 1.105), P(.289, .963), true, false);
            context.BezierTo(P(.135, .93), P(.014, .752), P(.012, .535), true, false);
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
