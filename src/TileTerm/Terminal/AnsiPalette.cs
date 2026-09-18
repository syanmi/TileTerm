using System.Windows.Media;
using XTerm.Common;

namespace TileTerm.Terminal;

/// <summary>
/// Converts the color values XTerm.NET reports per cell
/// (<c>AttributeData.GetFgColor()</c> / <c>GetBgColor()</c>, together with
/// their <see cref="ColorMode"/>) into WPF <see cref="Color"/> values.
///
/// <see cref="ColorMode"/> only has two values, <c>Palette256 = 0</c> and
/// <c>RGB = 1</c> — there is no separate "default color" mode, despite what
/// XTerm.NET's own README usage sample implies. An unset foreground/background
/// is instead reported as a <see cref="ColorMode.Palette256"/> color one past
/// the valid 0-255 index range (256 for foreground, 257 for background;
/// verified via <c>AttributeData.Default</c>), so <see cref="Resolve"/> treats
/// any out-of-range palette index as "use the fallback" rather than hardcoding
/// those two exact sentinel values.
/// </summary>
internal static class AnsiPalette
{
    // The 6-level intensity ramp used by the 6x6x6 color cube (indices 16-231).
    private static readonly byte[] CubeLevel = { 0, 95, 135, 175, 215, 255 };

    /// <summary>Default foreground/background and the 16 ANSI colors come from <see cref="TileScheme"/>
    /// (the same in every theme — terminals stay dark).</summary>
    public static Color DefaultForeground => TileScheme.TerminalFg;
    public static Color DefaultBackground => TileScheme.TerminalBg;

    public static Color Resolve(int color, int mode, Color fallback) => mode == (int)ColorMode.RGB
        ? Color.FromRgb((byte)((color >> 16) & 0xFF), (byte)((color >> 8) & 0xFF), (byte)(color & 0xFF))
        : color is >= 0 and <= 255 ? FromIndex(color) : fallback;

    private static Color FromIndex(int index)
    {
        if (index is >= 0 and <= 15)
            return TileScheme.Ansi16[index];

        if (index is >= 16 and <= 231)
        {
            int i = index - 16;
            int r = i / 36;
            int g = (i / 6) % 6;
            int b = i % 6;
            return Color.FromRgb(CubeLevel[r], CubeLevel[g], CubeLevel[b]);
        }

        if (index is >= 232 and <= 255)
        {
            byte gray = (byte)(8 + 10 * (index - 232));
            return Color.FromRgb(gray, gray, gray);
        }

        return DefaultForeground;
    }
}
