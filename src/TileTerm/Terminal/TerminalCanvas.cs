using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using XTerm.Common;
using XTerm.Selection;
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

    /// <summary>Consolas for everything Latin; the rest of the list covers Japanese (which Consolas lacks),
    /// so wide characters get one consistent font instead of whatever WPF happens to fall back to.</summary>
    internal static readonly FontFamily Font = new("Consolas, BIZ UDGothic, Yu Gothic, Meiryo, MS Gothic");

    private static readonly Typeface RegularFace = new(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface BoldFace = new(Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    /// <summary>Background of selected cells (a dark blue that keeps every text color readable).</summary>
    private static readonly Color SelectionColor = Color.FromRgb(0x26, 0x4F, 0x78);

    private readonly Dictionary<Color, SolidColorBrush> _brushes = new();
    private readonly DispatcherTimer _autoScroll = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private (int Col, int Row) _anchor;
    private bool _pressed;        // the left button went down on the terminal and is still held
    private bool _dragStarted;    // ... and the mouse has since moved off the cell it went down on
    private Rect _lastCursorRect;
    private (int Max, int Value, int Rows) _lastView = (-1, -1, -1);

    private double _cellWidth = 8;
    private double _cellHeight = 16;
    private double _baseline = 12;
    private double _pixelsPerDip = 1.0;
    private bool _metricsReady;

    public XTerm.Terminal? Terminal { get; set; }

    /// <summary>Fired after a layout size change, with the new size expressed in terminal cells.</summary>
    public event Action<int, int>? SizeInCellsChanged;

    /// <summary>Fired after a render in which the cursor cell's position changed, with that
    /// cell's rectangle in this control's coordinates (so the hosting pane can keep its IME
    /// input box on it).</summary>
    public event Action<Rect>? CursorMoved;

    /// <summary>Fired after a render in which the scroll position or the amount of history changed:
    /// (lines of history above the screen, the top visible line, rows on screen). Drives the scroll bar.</summary>
    public event Action<int, int, int>? ViewChanged;

    public TerminalCanvas()
    {
        // Keyboard focus lives in the hosting pane's IME TextBox, not here.
        Focusable = false;
        FocusVisualStyle = null;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Cursor = Cursors.IBeam;
        _autoScroll.Tick += (_, _) => OnAutoScrollTick();
    }

    private void EnsureMetrics()
    {
        if (_metricsReady) return;

        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var probe = new FormattedText(
            "M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            RegularFace, FontSize, Brushes.White, _pixelsPerDip);

        // Whole pixels, so cell backgrounds and block characters of neighbouring cells meet exactly
        // instead of leaving hairline seams (a fractional cell width like 7.7px would).
        _cellWidth = Math.Max(1, Math.Round(probe.WidthIncludingTrailingWhitespace * _pixelsPerDip)) / _pixelsPerDip;
        _cellHeight = Math.Max(1, Math.Ceiling(probe.Height * _pixelsPerDip - 0.01)) / _pixelsPerDip;
        _baseline = probe.Baseline;
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

    /// <summary>The cell (0-based column and row, clamped to the screen) under a point of this control.</summary>
    public (int Col, int Row) CellAt(Point point)
    {
        var (cols, rows) = MeasureCells(new Size(ActualWidth, ActualHeight));
        return (Math.Clamp((int)(point.X / _cellWidth), 0, cols - 1), Math.Clamp((int)(point.Y / _cellHeight), 0, rows - 1));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        var (cols, rows) = MeasureCells(info.NewSize);
        SizeInCellsChanged?.Invoke(cols, rows);
    }

    private SolidColorBrush BrushFor(Color color)
    {
        if (_brushes.TryGetValue(color, out var brush)) return brush;
        if (_brushes.Count > 1024) _brushes.Clear();   // 24-bit color output can produce endless variety
        brush = new SolidColorBrush(color);
        brush.Freeze();
        _brushes[color] = brush;
        return brush;
    }

    protected override void OnRender(DrawingContext dc)
    {
        EnsureMetrics();

        double width = Math.Max(ActualWidth, 1);
        double height = Math.Max(ActualHeight, 1);
        dc.DrawRectangle(BrushFor(AnsiPalette.DefaultBackground), null, new Rect(0, 0, width, height));

        var terminal = Terminal;
        if (terminal is null)
        {
            RaiseViewChanged(0, 0, 0);
            return;
        }

        Rect cursorRect;
        bool atBottom, cursorVisible;
        int cursorCol, totalCols;
        (int Max, int Value, int Rows) view;

        // The session's reader thread writes into this buffer while we draw it; both sides take this lock.
        lock (terminal)
        {
            var buffer = terminal.Buffer;
            DrawCells(dc, terminal);

            cursorRect = new Rect(buffer.X * _cellWidth, buffer.Y * _cellHeight, _cellWidth, _cellHeight);
            cursorCol = buffer.X;
            totalCols = terminal.Cols;
            cursorVisible = terminal.CursorVisible;
            // Scrolled back into history, the cursor's row is not on screen.
            atBottom = buffer.IsAtBottom;
            view = (buffer.YBase, buffer.YDisp, terminal.Rows);
        }

        if (cursorRect != _lastCursorRect)
        {
            _lastCursorRect = cursorRect;
            CursorMoved?.Invoke(cursorRect);
        }

        // Text the IME is still composing (not yet sent to the program) is shown at the cursor, underlined.
        var composition = CompositionText;
        bool composing = atBottom && !string.IsNullOrEmpty(composition);
        if (composing)
            DrawComposition(dc, composition!, cursorCol, cursorRect.Y, totalCols);

        if (cursorVisible && atBottom && !composing)
        {
            var cursorBrush = BrushFor(TileScheme.TerminalCursor);
            dc.DrawRectangle(cursorBrush, null, terminal.Options.CursorStyle switch
            {
                CursorStyle.Underline => new Rect(cursorRect.X, cursorRect.Y + _cellHeight - 2, _cellWidth, 2),
                CursorStyle.Bar => new Rect(cursorRect.X, cursorRect.Y, 2, _cellHeight),
                _ => cursorRect,
            });
        }

        RaiseViewChanged(view.Max, view.Value, view.Rows);
    }

    private void DrawCells(DrawingContext dc, XTerm.Terminal terminal)
    {
        var buffer = terminal.Buffer;
        var selection = terminal.Selection.HasSelection ? terminal.Selection : null;

        for (int row = 0; row < terminal.Rows; row++)
        {
            int index = buffer.YDisp + row;
            if (index < 0 || index >= buffer.Lines.Length) continue;
            var line = buffer.Lines[index];
            if (line is null) continue;

            int cols = Math.Min(terminal.Cols, line.Length);
            for (int col = 0; col < cols; col++)
            {
                var cell = line[col];
                if (cell.Width == 0) continue; // second half of a wide (CJK) character

                var attrs = cell.Attributes;
                var fg = AnsiPalette.Resolve(attrs.GetFgColor(), attrs.GetFgColorMode(), AnsiPalette.DefaultForeground);
                var bg = AnsiPalette.Resolve(attrs.GetBgColor(), attrs.GetBgColorMode(), AnsiPalette.DefaultBackground);
                if (attrs.IsInverse())
                    (fg, bg) = (bg, fg);
                if (selection is not null && selection.IsCellSelected(col, row))
                    bg = SelectionColor;

                int span = Math.Max(cell.Width, 1);
                var cellRect = new Rect(col * _cellWidth, row * _cellHeight, span * _cellWidth, _cellHeight);

                if (bg != AnsiPalette.DefaultBackground)
                    dc.DrawRectangle(BrushFor(bg), null, cellRect);

                string? text = cell.Content;
                if (string.IsNullOrWhiteSpace(text)) continue;

                DrawGlyph(dc, text, cellRect, span, BrushFor(fg), attrs.IsBold(), attrs.IsUnderline());
            }
        }
    }

    private void DrawGlyph(DrawingContext dc, string text, Rect cellRect, int span, SolidColorBrush fgBrush, bool bold, bool underline)
    {
        if (CellGlyphs.TryDraw(dc, text, cellRect, fgBrush, _pixelsPerDip)) return;

        var formatted = new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            bold ? BoldFace : RegularFace, FontSize, fgBrush, _pixelsPerDip);

        if (underline)
            formatted.SetTextDecorations(TextDecorations.Underline);

        // A wide character's glyph is narrower than its two cells: center it, so a run of them
        // is evenly spaced. And line the baseline up with the Latin text's, whichever font
        // supplied the glyph.
        double dx = span > 1 ? Math.Max(0, (cellRect.Width - formatted.WidthIncludingTrailingWhitespace) / 2) : 0;
        dc.DrawText(formatted, new Point(cellRect.X + dx, cellRect.Y + _baseline - formatted.Baseline));
    }

    /// <summary>What the IME is composing right now — typed but not yet confirmed. Shown at the cursor,
    /// underlined, until the IME confirms it (then the program echoes it) or cancels it.</summary>
    public string? CompositionText
    {
        get => _compositionText;
        set
        {
            if (_compositionText == value) return;
            _compositionText = value;
            InvalidateVisual();
        }
    }

    private string? _compositionText;

    private void DrawComposition(DrawingContext dc, string text, int startCol, double y, int totalCols)
    {
        var fgBrush = BrushFor(AnsiPalette.DefaultForeground);
        var bgBrush = BrushFor(AnsiPalette.DefaultBackground);

        int col = startCol;
        for (int i = 0; i < text.Length;)
        {
            int length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            int span = IsWide(char.ConvertToUtf32(text, i)) ? 2 : 1;
            if (col + span > totalCols) break;

            var cellRect = new Rect(col * _cellWidth, y, span * _cellWidth, _cellHeight);
            dc.DrawRectangle(bgBrush, null, cellRect);
            DrawGlyph(dc, text.Substring(i, length), cellRect, span, fgBrush, bold: false, underline: false);

            col += span;
            i += length;
        }

        dc.DrawRectangle(fgBrush, null, new Rect(startCol * _cellWidth, y + _cellHeight - 1, (col - startCol) * _cellWidth, 1));
    }

    /// <summary>Whether a character takes two terminal cells (East Asian wide / fullwidth).</summary>
    private static bool IsWide(int codePoint) =>
        codePoint is (>= 0x1100 and <= 0x115F) or (>= 0x2E80 and <= 0x303E) or (>= 0x3041 and <= 0x33FF)
            or (>= 0x3400 and <= 0x4DBF) or (>= 0x4E00 and <= 0x9FFF) or (>= 0xA000 and <= 0xA4CF)
            or (>= 0xAC00 and <= 0xD7A3) or (>= 0xF900 and <= 0xFAFF) or (>= 0xFE30 and <= 0xFE6F)
            or (>= 0xFF00 and <= 0xFF60) or (>= 0xFFE0 and <= 0xFFE6) or (>= 0x20000 and <= 0x3FFFD);

    private void RaiseViewChanged(int max, int value, int rows)
    {
        if ((max, value, rows) == _lastView) return;
        _lastView = (max, value, rows);
        ViewChanged?.Invoke(max, value, rows);
    }

    /// <summary>True when the screen shows the live end of the output (not scrolled back into history).</summary>
    public bool IsAtBottom
    {
        get
        {
            var terminal = Terminal;
            if (terminal is null) return true;
            lock (terminal) return terminal.Buffer.IsAtBottom;
        }
    }

    /// <summary>Scrolls the view by whole lines: negative = towards older output, positive = towards the newest.</summary>
    public void ScrollLines(int lines) => Scroll(t => t.ScrollLines(lines), lines != 0);

    /// <summary>Puts <paramref name="line"/> (0 = oldest history line) at the top of the screen.</summary>
    public void ScrollToLine(int line) => Scroll(t => t.Buffer.ScrollToLine(line), true);

    public void ScrollToTop() => Scroll(t => t.ScrollToTop(), true);

    public void ScrollToBottom() => Scroll(t => t.ScrollToBottom(), true);

    private void Scroll(Action<XTerm.Terminal> action, bool needed)
    {
        var terminal = Terminal;
        if (terminal is null || !needed) return;
        lock (terminal) action(terminal);
        InvalidateVisual();
    }

    // ---- Selecting text with the mouse -------------------------------------------------------------
    // Drag to select, double-click for a word, triple-click for a line. The selection itself lives in
    // XTerm.NET's SelectionManager (anchored to buffer lines, so it survives scrolling); this only feeds
    // it mouse positions. Copying and pasting are done by the hosting pane.

    /// <summary>True while some text is selected.</summary>
    public bool HasSelection
    {
        get
        {
            var terminal = Terminal;
            if (terminal is null) return false;
            lock (terminal) return terminal.Selection.HasSelection;
        }
    }

    /// <summary>The selected text, lines joined with CRLF and trailing spaces dropped (the screen pads
    /// every line with blanks, which nobody wants pasted); empty when nothing is selected.</summary>
    public string GetSelectedText()
    {
        var terminal = Terminal;
        if (terminal is null) return "";

        string text;
        lock (terminal) text = terminal.Selection.HasSelection ? terminal.Selection.GetSelectionText() : "";
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return string.Join("\r\n", Array.ConvertAll(lines, l => l.TrimEnd()));
    }

    public void ClearSelection()
    {
        var terminal = Terminal;
        if (terminal is null) return;
        lock (terminal) terminal.Selection.ClearSelection();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var terminal = Terminal;
        if (terminal is null) return;

        var cell = CellAt(e.GetPosition(this));
        lock (terminal)
        {
            var selection = terminal.Selection;
            selection.ClearSelection();
            if (e.ClickCount >= 2)
            {
                selection.StartSelection(cell.Col, cell.Row, e.ClickCount == 2 ? SelectionMode.Word : SelectionMode.Line);
                selection.EndSelection();
            }
        }

        _anchor = cell;
        _dragStarted = false;
        _pressed = e.ClickCount == 1;
        if (_pressed) CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_pressed) return;

        var point = e.GetPosition(this);
        ExtendSelection(CellAt(point));
        _autoScroll.IsEnabled = point.Y < 0 || point.Y > ActualHeight;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => FinishSelecting();

    protected override void OnLostMouseCapture(MouseEventArgs e) => FinishSelecting();

    private void ExtendSelection((int Col, int Row) cell)
    {
        var terminal = Terminal;
        if (terminal is null) return;

        lock (terminal)
        {
            if (!_dragStarted)
            {
                if (cell == _anchor) return;   // a plain click selects nothing
                terminal.Selection.StartSelection(_anchor.Col, _anchor.Row, SelectionMode.Normal);
                _dragStarted = true;
            }
            terminal.Selection.UpdateSelection(cell.Col, cell.Row);
        }
        InvalidateVisual();
    }

    /// <summary>While the mouse is held above or below the terminal, keep scrolling that way and extending
    /// the selection, so a selection can span more than one screen.</summary>
    private void OnAutoScrollTick()
    {
        if (!_pressed) return;

        var point = Mouse.GetPosition(this);
        if (point.Y < 0) ScrollLines(-1);
        else if (point.Y > ActualHeight) ScrollLines(1);
        ExtendSelection(CellAt(point));
    }

    private void FinishSelecting()
    {
        if (!_pressed) return;
        _pressed = false;
        _autoScroll.Stop();

        var terminal = Terminal;
        if (terminal is not null && _dragStarted)
            lock (terminal) terminal.Selection.EndSelection();
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
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
