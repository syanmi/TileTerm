using System;
using System.Windows;

namespace TileTerm;

/// <summary>
/// Owns "which theme is active": loads the saved choice at startup, switches it at runtime
/// (re-coloring the shared brushes in <see cref="Theme"/>, which every window/control follows),
/// persists it, and tells interested parties via <see cref="Changed"/> — used by things that
/// don't get their colors from those brushes, like the OS-drawn title bars and the terminal canvases.
/// </summary>
internal static class ThemeManager
{
    public static ThemePalette Palette { get; private set; } = ThemePalette.Dark;
    public static ThemeKind Current => Palette.Mode;

    /// <summary>Raised after the palette has been swapped and the shared brushes re-colored.</summary>
    public static event Action? Changed;

    /// <summary>Must run before the first window is created (see <c>App.OnStartup</c>): registers
    /// the brushes as application resources — the XAML uses them through <c>DynamicResource</c> —
    /// and applies the saved theme.</summary>
    public static void Initialize(Application app)
    {
        Theme.RegisterResources(app.Resources);
        Palette = ThemePalette.For(AppSettings.Current.Theme);
        Theme.ApplyPalette(Palette);
    }

    public static void Set(ThemeKind mode)
    {
        if (mode == Current) return;

        Palette = ThemePalette.For(mode);
        Theme.ApplyPalette(Palette);

        AppSettings.Current.Theme = mode;
        AppSettings.Current.Save();

        Changed?.Invoke();
    }
}
