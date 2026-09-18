using System.Windows.Media;

namespace TileTerm;

public enum ThemeKind
{
    Dark,
    Light,
}

/// <summary>
/// The colors of the app's own chrome (window, title bar, dialogs, lists, fields...) for one
/// theme. <see cref="Theme"/> owns one shared brush per field and re-colors it when the active
/// palette changes, so anything using those brushes (or a <c>DynamicResource</c> pointing at
/// them) follows the switch without being rebuilt. Tiles are deliberately not part of this —
/// see <see cref="TileScheme"/>.
/// </summary>
internal sealed record ThemePalette(
    ThemeKind Mode,
    Color BgWindow, Color BgField, Color BgList, Color BgSidebar, Color BgHover, Color BgPressed,
    Color BgTitleBar, Color BgTitleHover,
    Color Border, Color Fg, Color FgMuted, Color FgDisabled,
    Color Accent, Color AccentHover, Color PaneActiveBorder,
    Color GoldStar, Color Favorite)
{
    public bool IsDark => Mode == ThemeKind.Dark;

    internal static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>The original TileTerm look (VSCode-like dark), unchanged from before themes existed.</summary>
    public static ThemePalette Dark { get; } = new(
        ThemeKind.Dark,
        BgWindow: C(0x1E1E1E), BgField: C(0x2D2D2D), BgList: C(0x212121), BgSidebar: C(0x212121),
        BgHover: C(0x3A3A3A), BgPressed: C(0x484848),
        BgTitleBar: C(0x1A1A1A), BgTitleHover: C(0x3F3F3F),
        Border: C(0x3A3A3A), Fg: C(0xDCDCDC), FgMuted: C(0x9A9A9A), FgDisabled: C(0x777777),
        Accent: C(0x3A9BF5), AccentHover: C(0x5BACF7), PaneActiveBorder: C(0x1E90FF),
        GoldStar: C(0xE0B04A), Favorite: C(0xE0607D));

    /// <summary>Light counterpart, sampled from the JetBrains (CLion) light settings dialog that this
    /// UI is modeled on (reference/ref_CLion-settings.png): #F2F2F2 window/title/bottom-bar gray, a
    /// blue-gray (#E6EBF0) category sidebar, white lists and fields, #C9C9C9-ish hairlines, near-black
    /// text and the #2675BF selection blue. Kept that muted on purpose — a brighter white/blue reads
    /// as glaring next to the dark tiles.</summary>
    public static ThemePalette Light { get; } = new(
        ThemeKind.Light,
        BgWindow: C(0xF2F2F2), BgField: C(0xFFFFFF), BgList: C(0xFFFFFF), BgSidebar: C(0xE6EBF0),
        BgHover: C(0xE3E3E3), BgPressed: C(0xD5D5D5),
        BgTitleBar: C(0xE6EBF0), BgTitleHover: C(0xD3DAE2),
        Border: C(0xC9C9C9), Fg: C(0x1A1A1A), FgMuted: C(0x6B6B6B), FgDisabled: C(0x8C8C8C),
        Accent: C(0x2675BF), AccentHover: C(0x3A88D2), PaneActiveBorder: C(0x2675BF),
        GoldStar: C(0xC98A00), Favorite: C(0xD6335B));

    public static ThemePalette For(ThemeKind mode) => mode == ThemeKind.Light ? Light : Dark;
}

/// <summary>
/// Colors of a tile (its title strip and the terminal area under it). Fixed, not per theme: a
/// terminal is conventionally dark, and TUI programs (Claude Code, vim, ...) are written for a dark
/// background — on a light one their fixed bright colors turn unreadable — so tiles look the same in
/// both themes and only the chrome around them changes.
/// </summary>
internal static class TileScheme
{
    private static Color C(uint rgb) => ThemePalette.C(rgb);

    public static readonly Color HeaderBg = C(0x252525);
    public static readonly Color HeaderFg = C(0xDCDCDC);

    public static readonly Color TerminalBg = C(0x000000);
    public static readonly Color TerminalFg = C(0xE5E5E5);
    public static readonly Color TerminalCursor = Color.FromArgb(160, 220, 220, 220);

    /// <summary>The 16 standard ANSI colors (index 0-15), xterm's default palette.</summary>
    public static readonly Color[] Ansi16 =
    {
        C(0x000000), C(0xCD0000), C(0x00CD00), C(0xCDCD00), C(0x0000EE), C(0xCD00CD), C(0x00CDCD), C(0xE5E5E5),
        C(0x7F7F7F), C(0xFF0000), C(0x00FF00), C(0xFFFF00), C(0x5C5CFF), C(0xFF00FF), C(0x00FFFF), C(0xFFFFFF),
    };
}
