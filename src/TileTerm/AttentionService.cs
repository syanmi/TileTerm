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
/// Nothing happens while the tile's window is the active window: the user is looking at it. Otherwise:
/// <list type="bullet">
/// <item>the window's taskbar button flashes and gets a mark, until the window is activated
/// (<see cref="MainWindow.ShowAttention"/>);</item>
/// <item>a toast notification; clicking it brings the window back and focuses that tile.</item>
/// </list>
/// There are no settings for this: a user who does not want the toasts turns TileTerm off in Windows'
/// own notification settings, where it is listed (see <see cref="ToastNotifications"/>). The window is
/// deliberately not brought to the front on its own — Windows would not let it take the keyboard focus
/// anyway, and covering what the user is working on is worse than a flashing button.
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
        if (Window.GetWindow(pane) is not MainWindow window) return;
        // A minimized window can still be the "active" one (minimizing does not always hand activation to
        // another window), but nobody is looking at it then.
        if (window.IsActive && window.WindowState != WindowState.Minimized) return;

        var last = s_lastRaised.GetOrCreateValue(pane);
        var now = DateTime.UtcNow;
        if (now - last.Value < RepeatInterval) return;
        last.Value = now;

        window.ShowAttention();

        var title = string.IsNullOrWhiteSpace(attention.Title) ? pane.Profile.Name : attention.Title!;
        var body = !string.IsNullOrWhiteSpace(attention.Body) ? attention.Body!
            : attention.IsBell ? "ベル(通知)を受け取りました" : "通知を受け取りました";
        ToastNotifications.Show(title, body, () => window.Dispatcher.BeginInvoke(() => ShowPane(window, pane)));
    }

    /// <summary>What clicking the toast does: back to the window, with the tile that notified focused.</summary>
    private static void ShowPane(MainWindow window, TerminalPaneControl pane)
    {
        if (!window.IsLoaded || !window.IsVisible) return;   // closed since
        var hwnd = new WindowInteropHelper(window).Handle;
        // SW_RESTORE rather than WindowState = Normal: it returns a window that was maximized before
        // being minimized to maximized.
        if (hwnd != IntPtr.Zero && IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        window.Activate();
        if (!pane.IsShutDown) pane.FocusCanvas();
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
