using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TileTerm;

/// <summary>
/// App-wide preferences that aren't tied to a profile, kept in <c>settings.json</c> next to
/// <c>profiles.json</c> (see <see cref="AppPaths"/>). A missing or unreadable file simply means
/// "defaults": the Dark theme, and update checks on.
/// </summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The one loaded copy, shared by everything that reads or changes a setting.</summary>
    public static AppSettings Current { get; } = Load();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemeKind Theme { get; set; } = ThemeKind.Dark;

    /// <summary>Look for a new version on GitHub when the app starts (see <see cref="UpdateService"/>).
    /// On by default; the user can turn it off in the settings.</summary>
    public bool CheckForUpdates { get; set; } = true;

    // What happens when a program in a tile asks for attention (BEL, or a notification escape sequence
    // such as OSC 9) while its window is not the active one — see AttentionService.

    /// <summary>Put a mark on the window's taskbar button until the window is activated.</summary>
    public bool NotifyTaskbarBadge { get; set; } = true;

    /// <summary>Show a Windows toast notification.</summary>
    public bool NotifyToast { get; set; } = true;

    /// <summary>Restore the window (even from minimized) and bring it in front of other windows.</summary>
    public bool NotifyBringToFront { get; set; } = false;

    /// <summary>Count a plain BEL as a notification. Programs also ring it for small things (a failed tab
    /// completion), so it can be turned off, leaving only the explicit notification sequences.</summary>
    public bool NotifyOnBell { get; set; } = true;

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    private static AppSettings Load()
    {
        try
        {
            var path = FilePath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable: run with defaults rather than failing to start.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The setting still applies for this session; it just won't be remembered.
        }
    }
}
