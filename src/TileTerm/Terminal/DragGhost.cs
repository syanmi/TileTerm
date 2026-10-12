using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TileTerm.Terminal;

/// <summary>
/// What follows the cursor while a tile is dragged by its title bar: a small, see-through picture of the
/// tile with its name, like a browser tab being dragged. Without it nothing on screen changed once the
/// cursor left the window, so there was no telling whether a drag was still going (or had been cancelled
/// with Esc) and what letting go would do. Outside every TileTerm window its caption says what releasing
/// does there (open the tile in a new window, or nothing for a window's only tile).
///
/// A borderless, click-through, never-activated top-most window, so it neither steals the drop nor the focus.
/// </summary>
internal sealed class DragGhost
{
    private const double MaxImageWidth = 260;
    private static readonly Point CursorOffset = new(14, 18);

    private readonly Window _window;
    private readonly TextBlock _hint;
    private readonly bool _canTearOff;
    private readonly DpiScale _dpi;

    public DragGhost(TerminalPaneControl pane, bool canTearOff)
    {
        _canTearOff = canTearOff;
        _dpi = VisualTreeHelper.GetDpi(pane);

        var image = new Image { Source = Snapshot(pane), Stretch = Stretch.Uniform, Width = Math.Min(MaxImageWidth, Math.Max(pane.ActualWidth * 0.4, 120)) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var title = new TextBlock
        {
            Text = pane.Profile.Name, Foreground = Theme.TileFg, FontSize = 12, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _hint = new TextBlock { Foreground = Theme.TileFg, FontSize = 11, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

        var caption = new StackPanel { Margin = new Thickness(8, 5, 8, 6) };
        caption.Children.Add(title);
        caption.Children.Add(_hint);

        var stack = new StackPanel();
        stack.Children.Add(image);
        stack.Children.Add(caption);

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            IsHitTestVisible = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Opacity = 0.88,
            Content = new Border
            {
                Child = stack,
                Width = image.Width + 2,
                Background = Theme.TileHeaderBg,
                BorderBrush = Theme.PaneActiveBorder,
                BorderThickness = new Thickness(1),
            },
        };
        _window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE)
                | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    /// <summary>Moves the ghost to the cursor and updates its caption. Call while the drag goes on.</summary>
    public void Update(Point cursor, bool overTileTerm)
    {
        _window.Left = cursor.X / _dpi.DpiScaleX + CursorOffset.X;
        _window.Top = cursor.Y / _dpi.DpiScaleY + CursorOffset.Y;
        if (overTileTerm)
        {
            _hint.Visibility = Visibility.Collapsed;
        }
        else
        {
            _hint.Text = _canTearOff ? "ここで離すと新しいウィンドウで開きます" : "タイルが1枚だけのウィンドウからは切り離せません";
            _hint.Visibility = Visibility.Visible;
        }
        if (!_window.IsVisible) _window.Show();
    }

    public void Close() => _window.Close();

    /// <summary>The tile as it looks now, at its own resolution.</summary>
    private static BitmapSource Snapshot(FrameworkElement element)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        int w = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
        int h = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_NOACTIVATE = 0x8000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
