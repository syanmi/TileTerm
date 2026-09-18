using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TileTerm;

/// <summary>
/// Makes a window's OS-drawn title bar (icon/text/min/max/close strip) follow the active
/// theme, via the same <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> attribute Windows Terminal/VS Code
/// use for their native dialogs — set to dark or light — plus, on Windows 11, the exact caption
/// and text colors. Re-applied automatically when the theme changes while the window is open.
/// Only relevant for windows that still use the normal Windows chrome — <see cref="MainWindow"/>
/// draws its own title bar (<c>WindowStyle="None"</c>) so it doesn't need this.
/// </summary>
internal static class TitleBarTheme
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

            var palette = ThemeManager.Palette;
            int useDark = palette.IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));

            // The immersive-dark-mode flag alone leaves Windows' own generic caption color, a
            // visibly different shade from MainWindow's custom-drawn title bar strip — the two
            // windows would read as two different themes side by side. Windows 11 (build
            // 22000+) lets a window request an exact caption/text color instead; older Windows
            // ignores these two calls (the failure HRESULT isn't checked, as this is a
            // fire-and-forget helper) and keeps the generic look from the call above.
            int captionColor = ToColorRef(palette.BgTitleBar);
            DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            int textColor = ToColorRef(palette.Fg);
            DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            TrySet();
        else
            window.SourceInitialized += (_, _) => TrySet();

        Action onThemeChanged = TrySet;
        ThemeManager.Changed += onThemeChanged;
        window.Closed += (_, _) => ThemeManager.Changed -= onThemeChanged;
    }

    /// <summary>Packs a color into a Win32 COLORREF (0x00BBGGRR), which is what
    /// DWMWA_CAPTION_COLOR/DWMWA_TEXT_COLOR expect — byte order reversed from a typical RGB int.</summary>
    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
