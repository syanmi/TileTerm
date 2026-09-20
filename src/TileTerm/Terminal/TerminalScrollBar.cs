using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TileTerm.Terminal;

/// <summary>
/// The thin scroll bar at a tile's right edge: a track and a draggable thumb for the terminal's
/// scrollback. Its width is always reserved (the thumb just disappears when there is nothing to scroll),
/// so the terminal's column count does not jump when the first line scrolls off the screen.
///
/// It only reports what the user asks for (<see cref="Scrolled"/>, <see cref="PageRequested"/>); the
/// hosting pane moves the terminal and then calls <see cref="Update"/> with the result.
/// </summary>
internal sealed class TerminalScrollBar : FrameworkElement
{
    public const double BarWidth = 12;
    private const double MinThumbHeight = 24;

    private static readonly Brush TrackBrush = Frozen(Color.FromRgb(0x14, 0x14, 0x14));
    private static readonly Brush ThumbBrush = Frozen(Color.FromArgb(0x70, 0xA0, 0xA0, 0xA0));
    private static readonly Brush ThumbActiveBrush = Frozen(Color.FromArgb(0xB0, 0xC0, 0xC0, 0xC0));

    private int _max;      // largest value: how many lines of history there are above the screen
    private int _value;    // the top visible line, 0 (oldest) .. _max (the live screen)
    private int _rows;     // how many lines the screen shows
    private bool _hover;
    private bool _dragging;
    private double _dragOffset;

    /// <summary>The user dragged the thumb: the line that should now be at the top.</summary>
    public event Action<int>? Scrolled;

    /// <summary>The user clicked the track above (-1) or below (+1) the thumb.</summary>
    public event Action<int>? PageRequested;

    public TerminalScrollBar()
    {
        Width = BarWidth;
        Focusable = false;
    }

    public void Update(int max, int value, int rows)
    {
        if (max == _max && value == _value && rows == _rows) return;
        _max = max;
        _value = value;
        _rows = rows;
        InvalidateVisual();
    }

    private Rect ThumbRect()
    {
        double track = ActualHeight;
        double total = _max + Math.Max(_rows, 1);
        double height = Math.Min(track, Math.Max(MinThumbHeight, track * _rows / total));
        double top = _max <= 0 ? 0 : (track - height) * Math.Clamp(_value, 0, _max) / _max;
        return new Rect(3, top, BarWidth - 6, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(TrackBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_max <= 0) return;
        dc.DrawRoundedRectangle(_hover || _dragging ? ThumbActiveBrush : ThumbBrush, null, ThumbRect(), 3, 3);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        _hover = true;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_max <= 0) return;

        double y = e.GetPosition(this).Y;
        var thumb = ThumbRect();
        if (y >= thumb.Top && y <= thumb.Bottom)
        {
            _dragging = true;
            _dragOffset = y - thumb.Top;
            CaptureMouse();
            InvalidateVisual();
        }
        else
        {
            PageRequested?.Invoke(y < thumb.Top ? -1 : 1);
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;

        double room = ActualHeight - ThumbRect().Height;
        if (room <= 0) return;
        double top = e.GetPosition(this).Y - _dragOffset;
        Scrolled?.Invoke((int)Math.Round(Math.Clamp(top / room, 0, 1) * _max));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
