using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace TileTerm.Terminal;

/// <summary>
/// Draws the block-element (U+2580-259F) and straight box-drawing characters as plain rectangles
/// instead of font glyphs. A font's glyphs for these stop short of the cell edges (fonts add line
/// spacing the cell also has), which leaves visible seams between rows — apps like Claude Code draw
/// their logo and frames with them. Rectangles snapped to whole pixels always meet the neighbours.
/// </summary>
internal static class CellGlyphs
{
    // Arm weights of a box-drawing character: 0 none, 1 light, 2 heavy. Order: left, right, up, down.
    // The rounded corners (╭╮╯╰) are drawn square, which still joins the lines next to them.
    private static readonly Dictionary<char, (byte L, byte R, byte U, byte D)> BoxArms = new()
    {
        ['─'] = (1, 1, 0, 0), ['━'] = (2, 2, 0, 0), ['│'] = (0, 0, 1, 1), ['┃'] = (0, 0, 2, 2),
        ['┌'] = (0, 1, 0, 1), ['┏'] = (0, 2, 0, 2), ['┐'] = (1, 0, 0, 1), ['┓'] = (2, 0, 0, 2),
        ['└'] = (0, 1, 1, 0), ['┗'] = (0, 2, 2, 0), ['┘'] = (1, 0, 1, 0), ['┛'] = (2, 0, 2, 0),
        ['├'] = (0, 1, 1, 1), ['┣'] = (0, 2, 2, 2), ['┤'] = (1, 0, 1, 1), ['┫'] = (2, 0, 2, 2),
        ['┬'] = (1, 1, 0, 1), ['┳'] = (2, 2, 0, 2), ['┴'] = (1, 1, 1, 0), ['┻'] = (2, 2, 2, 0),
        ['┼'] = (1, 1, 1, 1), ['╋'] = (2, 2, 2, 2),
        ['╭'] = (0, 1, 0, 1), ['╮'] = (1, 0, 0, 1), ['╯'] = (1, 0, 1, 0), ['╰'] = (0, 1, 1, 0),
        ['╴'] = (1, 0, 0, 0), ['╵'] = (0, 0, 1, 0), ['╶'] = (0, 1, 0, 0), ['╷'] = (0, 0, 0, 1),
    };

    // Quadrant bits: 1 upper-left, 2 upper-right, 4 lower-left, 8 lower-right (U+2596-259F).
    private static readonly int[] Quadrants = { 4, 8, 1, 1 | 4 | 8, 1 | 8, 1 | 2 | 4, 1 | 2 | 8, 2, 2 | 4, 2 | 4 | 8 };

    /// <summary>Draws <paramref name="text"/> into <paramref name="cell"/> if it is one of the characters
    /// this class handles; returns false (drawing nothing) otherwise.</summary>
    public static bool TryDraw(DrawingContext dc, string text, Rect cell, Brush fg, double dpi)
    {
        if (text.Length != 1) return false;
        char c = text[0];

        if (c is >= '▀' and <= '▟') return DrawBlock(dc, c, cell, fg, dpi);
        if (BoxArms.TryGetValue(c, out var arms)) return DrawBox(dc, arms, cell, fg, dpi);
        return false;
    }

    private static bool DrawBlock(DrawingContext dc, char c, Rect cell, Brush fg, double dpi)
    {
        int x0 = Px(cell.Left, dpi), x1 = Px(cell.Right, dpi), y0 = Px(cell.Top, dpi), y1 = Px(cell.Bottom, dpi);
        int w = x1 - x0, h = y1 - y0;
        int xm = x0 + (w + 1) / 2, ym = y0 + (h + 1) / 2;   // the halves meet on a whole pixel

        void Fill(int l, int t, int r, int b, Brush? brush = null)
        {
            if (r > l && b > t) dc.DrawRectangle(brush ?? fg, null, new Rect(l / dpi, t / dpi, (r - l) / dpi, (b - t) / dpi));
        }

        switch (c)
        {
            case '▀': Fill(x0, y0, x1, ym); break;                                         // ▀
            case >= '▁' and <= '▇': Fill(x0, y1 - Eighths(h, c - '▀'), x1, y1); break;   // ▁..▇
            case '█': Fill(x0, y0, x1, y1); break;                                         // █
            case >= '▉' and <= '▏': Fill(x0, y0, x0 + Eighths(w, '▐' - c), y1); break;   // ▉..▏
            case '▐': Fill(xm, y0, x1, y1); break;                                         // ▐
            case '░': Fill(x0, y0, x1, y1, Shade(fg, 0.25)); break;                        // ░
            case '▒': Fill(x0, y0, x1, y1, Shade(fg, 0.5)); break;                         // ▒
            case '▓': Fill(x0, y0, x1, y1, Shade(fg, 0.75)); break;                        // ▓
            case '▔': Fill(x0, y0, x1, y0 + Eighths(h, 1)); break;                         // ▔
            case '▕': Fill(x1 - Eighths(w, 1), y0, x1, y1); break;                         // ▕
            default:                                                                            // ▖..▟
                int q = Quadrants[c - '▖'];
                if ((q & 1) != 0) Fill(x0, y0, xm, ym);
                if ((q & 2) != 0) Fill(xm, y0, x1, ym);
                if ((q & 4) != 0) Fill(x0, ym, xm, y1);
                if ((q & 8) != 0) Fill(xm, ym, x1, y1);
                break;
        }
        return true;
    }

    private static bool DrawBox(DrawingContext dc, (byte L, byte R, byte U, byte D) arms, Rect cell, Brush fg, double dpi)
    {
        int x0 = Px(cell.Left, dpi), x1 = Px(cell.Right, dpi), y0 = Px(cell.Top, dpi), y1 = Px(cell.Bottom, dpi);
        int cx = x0 + (x1 - x0) / 2, cy = y0 + (y1 - y0) / 2;

        int Thick(byte weight) => weight == 2 ? 2 : 1;
        void Fill(int l, int t, int r, int b) =>
            dc.DrawRectangle(fg, null, new Rect(l / dpi, t / dpi, (r - l) / dpi, (b - t) / dpi));

        // Each arm runs from the cell edge to just past the center, so arms of different weight still join.
        int reachX = Thick((byte)Math.Max(Math.Max(arms.U, arms.D), (byte)1)), reachY = Thick((byte)Math.Max(Math.Max(arms.L, arms.R), (byte)1));
        if (arms.L > 0) { int t = Thick(arms.L); Fill(x0, cy - t / 2, cx - reachX / 2 + reachX, cy - t / 2 + t); }
        if (arms.R > 0) { int t = Thick(arms.R); Fill(cx - reachX / 2, cy - t / 2, x1, cy - t / 2 + t); }
        if (arms.U > 0) { int t = Thick(arms.U); Fill(cx - t / 2, y0, cx - t / 2 + t, cy - reachY / 2 + reachY); }
        if (arms.D > 0) { int t = Thick(arms.D); Fill(cx - t / 2, cy - reachY / 2, cx - t / 2 + t, y1); }
        return true;
    }

    private static int Px(double dips, double dpi) => (int)Math.Round(dips * dpi);

    private static int Eighths(int size, int n) => Math.Max(1, (int)Math.Round(size * n / 8.0));

    private static Brush Shade(Brush fg, double alpha)
    {
        if (fg is not SolidColorBrush solid) return fg;
        var c = solid.Color;
        var brush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }
}
