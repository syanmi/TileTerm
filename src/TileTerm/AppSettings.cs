using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TileTerm;

/// <summary>
/// App-wide preferences that aren't tied to a profile (today: just the theme), kept in
/// <c>settings.json</c> next to <c>profiles.json</c> (see <see cref="AppPaths"/>). A missing or
/// unreadable file simply means "defaults" — the theme falls back to Dark.
/// </summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemeKind Theme { get; set; } = ThemeKind.Dark;

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    public static AppSettings Load()
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
            // The theme still applies for this session; it just won't be remembered.
        }
    }
}
