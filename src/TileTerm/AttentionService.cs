using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TileTerm.Terminal;

namespace TileTerm;

/// <summary>
/// Decides what happens when a program in a tile asks for attention — rings the bell (BEL) or sends a
/// desktop notification (OSC 9, OSC 777), typically a command-line tool such as Claude Code saying it
/// has finished or is waiting for input.
///
/// Nothing happens while the tile's window is the active window: the user is looking at it. Otherwise,
/// depending on the settings (<see cref="AppSettings"/>, "通知" page):
/// <list type="bullet">
/// <item>a mark on the window's taskbar button, until the window is activated (<see cref="MainWindow.ShowAttentionBadge"/>);</item>
/// <item>a toast notification; clicking it brings the window back and focuses that tile;</item>
/// <item>bringing the window to the front, restoring it if minimized. Windows does not let a program
/// that is in the background take the keyboard focus from the one the user is typing in, so the window
/// comes up on top but the focus stays where it was (the taskbar button flashes instead).</item>
/// </list>
/// </summary>
internal static class AttentionService
{
    /// <summary>Repeats from the same tile within this interval are dropped — a program that rings the
    /// bell several times in a row (or a held-down key at an empty prompt) should not stack up toasts.</summary>
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(3);

    private static readonly ConditionalWeakTable<TerminalPaneControl, StrongBox<DateTime>> s_lastRaised = new();

    /// <summary>Call on the UI thread.</summary>
    public static void Raise(TerminalPaneControl pane, TerminalAttention attention)
    {
        var settings = AppSettings.Current;
        if (attention.IsBell && !settings.NotifyOnBell) return;
        if (Window.GetWindow(pane) is not MainWindow window) return;
        // A minimized window can still be the "active" one (minimizing does not always hand activation to
        // another window), but nobody is looking at it then.
        if (window.IsActive && window.WindowState != WindowState.Minimized) return;

        var last = s_lastRaised.GetOrCreateValue(pane);
        var now = DateTime.UtcNow;
        if (now - last.Value < RepeatInterval) return;
        last.Value = now;

        if (settings.NotifyTaskbarBadge)
            window.ShowAttentionBadge();

        if (settings.NotifyToast)
        {
            var title = string.IsNullOrWhiteSpace(attention.Title) ? pane.Profile.Name : attention.Title!;
            var body = !string.IsNullOrWhiteSpace(attention.Body) ? attention.Body!
                : attention.IsBell ? "ベル(通知)を受け取りました" : "通知を受け取りました";
            ToastNotifications.Show(title, body, () => window.Dispatcher.BeginInvoke(() => ShowPane(window, pane)));
        }

        if (settings.NotifyBringToFront)
            BringToFront(window);
    }

    /// <summary>Restores <paramref name="window"/> if it is minimized and raises it above other windows,
    /// trying to activate it too (which Windows allows only in some situations, see the class summary).</summary>
    private static void BringToFront(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // SW_RESTORE rather than WindowState = Normal: it returns a window that was maximized before
        // being minimized to maximized.
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

        // Raising through topmost and back puts it above the other windows even when activation is refused.
        window.Topmost = true;
        window.Topmost = false;
        window.Activate();
    }

    /// <summary>What clicking the toast does: back to the window, with the tile that notified focused.</summary>
    private static void ShowPane(MainWindow window, TerminalPaneControl pane)
    {
        if (!window.IsLoaded || !window.IsVisible) return;   // closed since
        BringToFront(window);
        if (!pane.IsShutDown) pane.FocusCanvas();
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
