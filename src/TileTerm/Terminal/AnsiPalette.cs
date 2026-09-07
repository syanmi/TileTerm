using System.Windows.Media;

namespace TileTerm.Terminal;

/// <summary>
/// Converts the color values XTerm.NET reports per cell
/// (<c>AttributeData.GetFgColor()</c> / <c>GetBgColor()</c>, together with
/// their "mode": 0 = terminal default, 1 = 256-color palette index,
/// 2 = 24-bit RGB) into WPF <see cref="Color"/> values.
/// </summary>
internal static class AnsiPalette
{
    /// <summary>The 16 standard ANSI colors (index 0-15), xterm's default palette.</summary>
    private static readonly Color[] Standard16 =
    {
        Color.FromRgb(0x00, 0x00, 0x00), // 0 black
        Color.FromRgb(0xCD, 0x00, 0x00), // 1 red
        Color.FromRgb(0x00, 0xCD, 0x00), // 2 green
        Color.FromRgb(0xCD, 0xCD, 0x00), // 3 yellow
        Color.FromRgb(0x00, 0x00, 0xEE), // 4 blue
        Color.FromRgb(0xCD, 0x00, 0xCD), // 5 magenta
        Color.FromRgb(0x00, 0xCD, 0xCD), // 6 cyan
        Color.FromRgb(0xE5, 0xE5, 0xE5), // 7 white
        Color.FromRgb(0x7F, 0x7F, 0x7F), // 8 bright black
        Color.FromRgb(0xFF, 0x00, 0x00), // 9 bright red
        Color.FromRgb(0x00, 0xFF, 0x00), // 10 bright green
        Color.FromRgb(0xFF, 0xFF, 0x00), // 11 bright yellow
        Color.FromRgb(0x5C, 0x5C, 0xFF), // 12 bright blue
        Color.FromRgb(0xFF, 0x00, 0xFF), // 13 bright magenta
        Color.FromRgb(0x00, 0xFF, 0xFF), // 14 bright cyan
        Color.FromRgb(0xFF, 0xFF, 0xFF), // 15 bright white
    };

    // The 6-level intensity ramp used by the 6x6x6 color cube (indices 16-231).
    private static readonly byte[] CubeLevel = { 0, 95, 135, 175, 215, 255 };

    public static Color DefaultForeground { get; } = Color.FromRgb(0xE5, 0xE5, 0xE5);
    public static Color DefaultBackground { get; } = Colors.Black;

    public static Color Resolve(int color, int mode, Color fallback) => mode switch
    {
        1 => FromIndex(color),
        2 => Color.FromRgb((byte)((color >> 16) & 0xFF), (byte)((color >> 8) & 0xFF), (byte)(color & 0xFF)),
        _ => fallback,
    };

    private static Color FromIndex(int index)
    {
        if (index is >= 0 and <= 15)
            return Standard16[index];

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
