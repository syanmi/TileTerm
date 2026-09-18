using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TileTerm;

/// <summary>
/// Small vector icons for toolbar/title-bar buttons, drawn as plain shapes
/// instead of relying on an icon font (safer: no font-availability risk,
/// crisp at any size/DPI). One shared brush color; callers just drop the
/// returned element in as a Button's Content.
/// </summary>
internal static class Icons
{
    // The shared theme foreground brush itself (not a copy): its color changes with the theme,
    // so every icon drawn with it follows a light/dark switch without being rebuilt.
    private static readonly Brush Ink = Theme.Fg;

    private static readonly Lazy<ImageSource> AppIconSource = new(() =>
    {
        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri("pack://application:,,,/Assets/TileTerm-titlebar.png", UriKind.Absolute);
        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    });

    /// <summary>The app's own icon (the same mark as the exe/taskbar icon), for the title bar.
    /// A separate small PNG rather than the .ico so the mark is tuned for this size: an ICO
    /// shown through an Image picks one frame and scales it, which blurs at 18px.</summary>
    public static UIElement App(double size = 18)
    {
        var image = new Image { Source = AppIconSource.Value, Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    public static UIElement SplitRight() => BuildSplit(vertical: true);
    public static UIElement SplitDown() => BuildSplit(vertical: false);

    private static UIElement BuildSplit(bool vertical)
    {
        var canvas = new Canvas { Width = 16, Height = 16 };

        canvas.Children.Add(new Rectangle
        {
            Width = 14, Height = 14, Stroke = Ink, StrokeThickness = 1.3, RadiusX = 1, RadiusY = 1,
        }.At(1, 1));

        if (vertical)
        {
            canvas.Children.Add(new Line { X1 = 8, Y1 = 1, X2 = 8, Y2 = 15, Stroke = Ink, StrokeThickness = 1.3 });
            canvas.Children.Add(new Rectangle
            {
                Width = 5.7, Height = 12.4, Fill = Ink, Opacity = 0.5,
            }.At(8.3, 1.3));
        }
        else
        {
            canvas.Children.Add(new Line { X1 = 1, Y1 = 8, X2 = 15, Y2 = 8, Stroke = Ink, StrokeThickness = 1.3 });
            canvas.Children.Add(new Rectangle
            {
                Width = 12.4, Height = 5.7, Fill = Ink, Opacity = 0.5,
            }.At(1.3, 8.3));
        }

        return canvas;
    }

    public static UIElement Plus(double size = 14, double thickness = 1.4)
    {
        var canvas = new Canvas { Width = size, Height = size };
        double m = size * 0.12;
        canvas.Children.Add(Cap(new Line { X1 = size / 2, Y1 = m, X2 = size / 2, Y2 = size - m, Stroke = Ink, StrokeThickness = thickness }));
        canvas.Children.Add(Cap(new Line { X1 = m, Y1 = size / 2, X2 = size - m, Y2 = size / 2, Stroke = Ink, StrokeThickness = thickness }));
        return canvas;
    }

    /// <summary>Outline folder (file-browse buttons) — a tab plus a body, stroked only.</summary>
    public static UIElement Folder(double size = 14)
    {
        var w = size;
        var h = size * 0.78;
        var geometry = Geometry.Parse(FormattableString.Invariant(
            $"M 0.5,{h * 0.2:0.##} L 0.5,{h - 0.5:0.##} L {w - 0.5:0.##},{h - 0.5:0.##} L {w - 0.5:0.##},{h * 0.3:0.##} L {w * 0.45:0.##},{h * 0.3:0.##} L {w * 0.35:0.##},0.5 L 0.5,0.5 Z"));
        return new Path
        {
            Data = geometry, Stroke = Ink, StrokeThickness = 1.1, StrokeLineJoin = PenLineJoin.Round,
            Width = w, Height = h, Fill = Brushes.Transparent,
        };
    }

    public static UIElement Minus(double size = 14, double thickness = 1.4)
    {
        var canvas = new Canvas { Width = size, Height = size };
        double m = size * 0.12;
        canvas.Children.Add(Cap(new Line { X1 = m, Y1 = size / 2, X2 = size - m, Y2 = size / 2, Stroke = Ink, StrokeThickness = thickness }));
        return canvas;
    }

    /// <summary>The × glyph. <paramref name="ink"/> overrides the theme foreground — tiles keep
    /// their dark look in every theme, so their buttons need light strokes even in the light theme.</summary>
    public static UIElement Close(double size = 16, double thickness = 1.4, Brush? ink = null)
    {
        var stroke = ink ?? Ink;
        var canvas = new Canvas { Width = size, Height = size };
        double m = size * 0.2;
        canvas.Children.Add(Cap(new Line { X1 = m, Y1 = m, X2 = size - m, Y2 = size - m, Stroke = stroke, StrokeThickness = thickness }));
        canvas.Children.Add(Cap(new Line { X1 = size - m, Y1 = m, X2 = m, Y2 = size - m, Stroke = stroke, StrokeThickness = thickness }));
        return canvas;
    }

    /// <summary>Restart/refresh glyph. Uses the standard "↻" character rather than a
    /// hand-drawn arc — a circular-arrow path is fiddly to get right without a live
    /// design tool, and this glyph is a plain, well-supported Unicode symbol (not a
    /// private-use icon-font codepoint), so it doesn't carry the font-availability
    /// risk that ruled out icon fonts elsewhere in this project.</summary>
    public static UIElement Refresh(double fontSize = 13, Brush? ink = null) => new TextBlock
    {
        Text = "↻", Foreground = ink ?? Ink, FontSize = fontSize,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
    };

    public static UIElement Minimize(double width = 10, double thickness = 1.4) =>
        Cap(new Line { X1 = 0, Y1 = 0, X2 = width, Y2 = 0, Stroke = Ink, StrokeThickness = thickness });

    public static UIElement Maximize(double size = 10, double thickness = 1.2) =>
        new Rectangle { Width = size, Height = size, Stroke = Ink, StrokeThickness = thickness };

    public static UIElement Restore(double size = 10, double thickness = 1.1)
    {
        var canvas = new Canvas { Width = size + 3, Height = size + 3 };
        canvas.Children.Add(new Rectangle { Width = size, Height = size, Stroke = Ink, StrokeThickness = thickness }.At(3, 0));
        canvas.Children.Add(new Rectangle
        {
            Width = size, Height = size, Stroke = Ink, StrokeThickness = thickness, Fill = Theme.BgTitleBar,
        }.At(0, 3));
        return canvas;
    }

    /// <summary>Gear/cog icon for the settings button. Built from a computed tooth
    /// polygon plus a punched-out center hole, rather than a hand-tuned path string —
    /// simple trigonometry is easier to get right on the first try than eyeballing
    /// coordinates blind.</summary>
    public static UIElement Settings(double size = 15)
    {
        double cx = size / 2.0, cy = size / 2.0;
        double outerR = size * 0.46;
        double innerR = size * 0.32;
        double holeR = size * 0.17;
        const int teeth = 8;
        double toothHalfAngle = (360.0 / teeth) * 0.22;

        var points = new PointCollection();
        for (int i = 0; i < teeth; i++)
        {
            double a0 = 360.0 / teeth * i;
            points.Add(Polar(cx, cy, innerR, a0 - toothHalfAngle));
            points.Add(Polar(cx, cy, outerR, a0 - toothHalfAngle));
            points.Add(Polar(cx, cy, outerR, a0 + toothHalfAngle));
            points.Add(Polar(cx, cy, innerR, a0 + toothHalfAngle));
        }

        var figure = new PathFigure { StartPoint = points[0], IsClosed = true };
        figure.Segments.Add(new PolyLineSegment(points.Skip(1).ToList(), isStroked: true));
        var gear = new PathGeometry();
        gear.Figures.Add(figure);

        var hole = new EllipseGeometry(new Point(cx, cy), holeR, holeR);
        var combined = new CombinedGeometry(GeometryCombineMode.Exclude, gear, hole);

        return new Path { Data = combined, Fill = Ink, Width = size, Height = size, Stretch = Stretch.Uniform };
    }

    private static Point Polar(double cx, double cy, double r, double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180.0;
        return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }

    private static T At<T>(this T shape, double left, double top) where T : UIElement
    {
        Canvas.SetLeft(shape, left);
        Canvas.SetTop(shape, top);
        return shape;
    }

    private static Line Cap(Line line)
    {
        line.StrokeStartLineCap = PenLineCap.Round;
        line.StrokeEndLineCap = PenLineCap.Round;
        return line;
    }
}
