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

    public static void Apply(Window window)
    {
        void TrySet()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int useDark = 1;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            TrySet();
        else
            window.SourceInitialized += (_, _) => TrySet();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
