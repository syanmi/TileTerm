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
    private static readonly Brush Ink = Brushes.Gainsboro;

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
                Width = 5.7, Height = 12.4, Fill = new SolidColorBrush(Color.FromArgb(130, 200, 200, 200)),
            }.At(8.3, 1.3));
        }
        else
        {
            canvas.Children.Add(new Line { X1 = 1, Y1 = 8, X2 = 15, Y2 = 8, Stroke = Ink, StrokeThickness = 1.3 });
            canvas.Children.Add(new Rectangle
            {
                Width = 12.4, Height = 5.7, Fill = new SolidColorBrush(Color.FromArgb(130, 200, 200, 200)),
            }.At(1.3, 8.3));
        }

        return canvas;
    }

    public static UIElement Close(double size = 16, double thickness = 1.4)
    {
        var canvas = new Canvas { Width = size, Height = size };
        double m = size * 0.2;
        canvas.Children.Add(Cap(new Line { X1 = m, Y1 = m, X2 = size - m, Y2 = size - m, Stroke = Ink, StrokeThickness = thickness }));
        canvas.Children.Add(Cap(new Line { X1 = size - m, Y1 = m, X2 = m, Y2 = size - m, Stroke = Ink, StrokeThickness = thickness }));
        return canvas;
    }

    /// <summary>Restart/refresh glyph. Uses the standard "↻" character rather than a
    /// hand-drawn arc — a circular-arrow path is fiddly to get right without a live
    /// design tool, and this glyph is a plain, well-supported Unicode symbol (not a
    /// private-use icon-font codepoint), so it doesn't carry the font-availability
    /// risk that ruled out icon fonts elsewhere in this project.</summary>
    public static UIElement Refresh(double fontSize = 13) => new TextBlock
    {
        Text = "↻", Foreground = Ink, FontSize = fontSize,
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
            Width = size, Height = size, Stroke = Ink, StrokeThickness = thickness, Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
        }.At(0, 3));
        return canvas;
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
