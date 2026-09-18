using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TileTerm;

/// <summary>
/// Makes a window's OS-drawn title bar (icon/text/min/max/close strip) render
/// in dark mode, via the same <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> attribute
/// Windows Terminal/VS Code use for their native dialogs. Only relevant for
/// windows that still use the normal Windows chrome — <see cref="MainWindow"/>
/// draws its own title bar (<c>WindowStyle="None"</c>) so it doesn't need this.
/// </summary>
internal static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    public static void Apply(Window window)
    {
        void TrySet()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int useDark = 1;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));

            // DWMWA_USE_IMMERSIVE_DARK_MODE alone leaves Windows' own generic dark-mode
            // caption color, which is a visibly different shade from MainWindow's own
            // custom-drawn title bar strip (#1A1A1A) — the two windows ended up reading as
            // two different "dark themes" side by side. Windows 11 (build 22000+) lets a
            // window request an exact caption/text color instead; older Windows just
            // ignores these two calls (DwmSetWindowAttribute returns a failure HRESULT,
            // which this fire-and-forget helper doesn't check, same as before) and keeps
            // the generic dark mode from the call above.
            int captionColor = ToColorRef(0x1A, 0x1A, 0x1A);
            DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            int textColor = ToColorRef(0xDC, 0xDC, 0xDC); // Gainsboro, matching Theme.Fg
            DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            TrySet();
        else
            window.SourceInitialized += (_, _) => TrySet();
    }

    /// <summary>Packs RGB into a Win32 COLORREF (0x00BBGGRR), which is what
    /// DWMWA_CAPTION_COLOR/DWMWA_TEXT_COLOR expect — byte order reversed from a typical RGB int.</summary>
    private static int ToColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
