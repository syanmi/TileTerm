using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using XTerm.Common;
using XTermKey = XTerm.Input.Key;

namespace TileTerm.Terminal;

/// <summary>
/// Renders one <see cref="XTerm.Terminal"/> screen buffer as monospaced cells
/// and turns WPF keyboard input into the escape sequences the hosted console
/// process expects. XTerm.NET itself is headless (it has no notion of pixels
/// or WPF); this control is the "bring your own renderer" half of it.
/// </summary>
public sealed class TerminalCanvas : FrameworkElement
{
    internal const double FontSize = 14.0;
    internal static readonly FontFamily Font = new("Consolas");

    private Rect _lastCursorRect;

    private double _cellWidth = 8;
    private double _cellHeight = 16;
    private double _pixelsPerDip = 1.0;
    private bool _metricsReady;

    public XTerm.Terminal? Terminal { get; set; }

    /// <summary>Fired after a layout size change, with the new size expressed in terminal cells.</summary>
    public event Action<int, int>? SizeInCellsChanged;

    /// <summary>Fired after a render in which the cursor cell's position changed, with that
    /// cell's rectangle in this control's coordinates (so the hosting pane can keep its IME
    /// input box on it).</summary>
    public event Action<Rect>? CursorMoved;

    public TerminalCanvas()
    {
        // Keyboard focus lives in the hosting pane's IME TextBox, not here.
        Focusable = false;
        FocusVisualStyle = null;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    private void EnsureMetrics()
    {
        if (_metricsReady) return;

        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var probe = new FormattedText(
            "M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            FontSize, Brushes.White, _pixelsPerDip);

        _cellWidth = probe.WidthIncludingTrailingWhitespace;
        _cellHeight = probe.Height;
        _metricsReady = true;
    }

    /// <summary>Converts a pixel size into a terminal grid size (columns/rows), rounding down.</summary>
    public (int Cols, int Rows) MeasureCells(Size size)
    {
        EnsureMetrics();
        int cols = Math.Max(1, (int)(size.Width / _cellWidth));
        int rows = Math.Max(1, (int)(size.Height / _cellHeight));
        return (cols, rows);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        var (cols, rows) = MeasureCells(info.NewSize);
        SizeInCellsChanged?.Invoke(cols, rows);
    }

    protected override void OnRender(DrawingContext dc)
    {
        EnsureMetrics();

        double width = Math.Max(ActualWidth, 1);
        double height = Math.Max(ActualHeight, 1);
        dc.DrawRectangle(new SolidColorBrush(AnsiPalette.DefaultBackground), null, new Rect(0, 0, width, height));

        var terminal = Terminal;
        if (terminal is null) return;

        var buffer = terminal.Buffer;

        for (int row = 0; row < terminal.Rows; row++)
        {
            var line = buffer.Lines[buffer.YDisp + row];
            if (line is null) continue;

            for (int col = 0; col < terminal.Cols; col++)
            {
                var cell = line[col];
                if (cell.Width == 0) continue; // second half of a wide (CJK) character

                var attrs = cell.Attributes;
                var fg = AnsiPalette.Resolve(attrs.GetFgColor(), attrs.GetFgColorMode(), AnsiPalette.DefaultForeground);
                var bg = AnsiPalette.Resolve(attrs.GetBgColor(), attrs.GetBgColorMode(), AnsiPalette.DefaultBackground);
                if (attrs.IsInverse())
                    (fg, bg) = (bg, fg);

                double x = col * _cellWidth;
                double y = row * _cellHeight;

                if (bg != AnsiPalette.DefaultBackground)
                    dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(x, y, _cellWidth, _cellHeight));

                string? text = cell.Content;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var typeface = new Typeface(Font, FontStyles.Normal,
                        attrs.IsBold() ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                    var formatted = new FormattedText(
                        text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        typeface, FontSize, new SolidColorBrush(fg), _pixelsPerDip);

                    if (attrs.IsUnderline())
                        formatted.SetTextDecorations(TextDecorations.Underline);

                    dc.DrawText(formatted, new Point(x, y));
                }
            }
        }

        var cursorRect = new Rect(buffer.X * _cellWidth, buffer.Y * _cellHeight, _cellWidth, _cellHeight);
        if (cursorRect != _lastCursorRect)
        {
            _lastCursorRect = cursorRect;
            CursorMoved?.Invoke(cursorRect);
        }

        if (terminal.CursorVisible)
        {
            double cx = buffer.X * _cellWidth;
            double cy = buffer.Y * _cellHeight;
            var cursorBrush = new SolidColorBrush(TileScheme.TerminalCursor);
            dc.DrawRectangle(cursorBrush, null, terminal.Options.CursorStyle switch
            {
                CursorStyle.Underline => new Rect(cx, cy + _cellHeight - 2, _cellWidth, 2),
                CursorStyle.Bar => new Rect(cx, cy, 2, _cellHeight),
                _ => new Rect(cx, cy, _cellWidth, _cellHeight),
            });
        }
    }

    /// <summary>Maps a WPF named key (Enter, arrows, function keys, ...) to XTerm.NET's key enum.</summary>
    public static XTermKey? MapKey(Key key) => key switch
    {
        Key.Enter => XTermKey.Enter,
        Key.Tab => XTermKey.Tab,
        Key.Back => XTermKey.Backspace,
        Key.Escape => XTermKey.Escape,
        Key.Up => XTermKey.UpArrow,
        Key.Down => XTermKey.DownArrow,
        Key.Left => XTermKey.LeftArrow,
        Key.Right => XTermKey.RightArrow,
        Key.Home => XTermKey.Home,
        Key.End => XTermKey.End,
        Key.PageUp => XTermKey.PageUp,
        Key.PageDown => XTermKey.PageDown,
        Key.Insert => XTermKey.Insert,
        Key.Delete => XTermKey.Delete,
        Key.F1 => XTermKey.F1,
        Key.F2 => XTermKey.F2,
        Key.F3 => XTermKey.F3,
        Key.F4 => XTermKey.F4,
        Key.F5 => XTermKey.F5,
        Key.F6 => XTermKey.F6,
        Key.F7 => XTermKey.F7,
        Key.F8 => XTermKey.F8,
        Key.F9 => XTermKey.F9,
        Key.F10 => XTermKey.F10,
        Key.F11 => XTermKey.F11,
        Key.F12 => XTermKey.F12,
        _ => null,
    };

    public static XTerm.Input.KeyModifiers MapModifiers(ModifierKeys mods)
    {
        var result = XTerm.Input.KeyModifiers.None;
        if (mods.HasFlag(ModifierKeys.Shift)) result |= XTerm.Input.KeyModifiers.Shift;
        if (mods.HasFlag(ModifierKeys.Alt)) result |= XTerm.Input.KeyModifiers.Alt;
        if (mods.HasFlag(ModifierKeys.Control)) result |= XTerm.Input.KeyModifiers.Control;
        return result;
    }
}
