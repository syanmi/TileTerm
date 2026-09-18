using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TileTerm.Terminal;

/// <summary>
/// Loads/saves the user's console-app profiles as JSON under
/// <c>%AppData%\TileTerm\profiles.json</c>, and seeds sensible defaults the
/// first time the app runs (only for apps actually found on this machine).
/// Also tracks which single profile is the "既定" (default — used when the
/// title bar's plain split buttons are clicked) and which profiles are
/// "お気に入り" (favorites — shown as their own quick-launch buttons).
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public List<ProfileDefinition> Profiles { get; private set; } = new();
    public string? DefaultProfileId { get; private set; }
    public List<string> FavoriteProfileIds { get; private set; } = new();

    public ProfileStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TileTerm");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "profiles.json");
    }

    public void Load()
    {
        if (File.Exists(_filePath))
        {
            var json = File.ReadAllText(_filePath);

            // Current format: a {Profiles, DefaultProfileId, FavoriteProfileIds} object.
            try
            {
                var data = JsonSerializer.Deserialize<StoreFile>(json, JsonOptions);
                if (data is { Profiles.Count: > 0 })
                {
                    Profiles = data.Profiles;
                    DefaultProfileId = data.DefaultProfileId;
                    FavoriteProfileIds = data.FavoriteProfileIds ?? new List<string>();
                    EnsureDefaultIsValid();
                    return;
                }
            }
            catch (JsonException)
            {
                // Not this shape — fall through and try the older one below.
            }

            // Older format from before defaults/favorites existed: a bare profile array.
            // Migrate it forward instead of discarding the user's existing profiles.
            try
            {
                var legacy = JsonSerializer.Deserialize<List<ProfileDefinition>>(json, JsonOptions);
                if (legacy is { Count: > 0 })
                {
                    Profiles = legacy;
                    DefaultProfileId = legacy[0].Id;
                    FavoriteProfileIds = new List<string>();
                    Save(); // upgrade the file on disk to the current format
                    return;
                }
            }
            catch (JsonException)
            {
                // Corrupt/unreadable file: fall through and reseed defaults below.
            }
        }

        Profiles = CreateDefaults();
        DefaultProfileId = Profiles.Count > 0 ? Profiles[0].Id : null;
        FavoriteProfileIds = new List<string>();
        Save();
    }

    public void Save()
    {
        var data = new StoreFile
        {
            Profiles = Profiles,
            DefaultProfileId = DefaultProfileId,
            FavoriteProfileIds = FavoriteProfileIds,
        };
        File.WriteAllText(_filePath, JsonSerializer.Serialize(data, JsonOptions));
    }

    public void AddProfile(ProfileDefinition profile)
    {
        Profiles.Add(profile);
        Save();
    }

    public void RemoveProfile(ProfileDefinition profile)
    {
        Profiles.Remove(profile);
        FavoriteProfileIds.Remove(profile.Id);
        if (DefaultProfileId == profile.Id)
            DefaultProfileId = Profiles.FirstOrDefault()?.Id;
        Save();
    }

    public ProfileDefinition? GetDefaultProfile() =>
        Profiles.FirstOrDefault(p => p.Id == DefaultProfileId) ?? Profiles.FirstOrDefault();

    public void SetDefault(ProfileDefinition profile)
    {
        if (!Profiles.Contains(profile)) return;
        DefaultProfileId = profile.Id;
        Save();
    }

    public bool IsFavorite(ProfileDefinition profile) => FavoriteProfileIds.Contains(profile.Id);

    public void SetFavorite(ProfileDefinition profile, bool isFavorite)
    {
        if (isFavorite)
        {
            if (!FavoriteProfileIds.Contains(profile.Id))
                FavoriteProfileIds.Add(profile.Id);
        }
        else
        {
            FavoriteProfileIds.Remove(profile.Id);
        }
        Save();
    }

    /// <summary>Favorite profiles in display order, skipping any id whose profile was deleted.</summary>
    public List<ProfileDefinition> GetFavoritesInOrder() =>
        FavoriteProfileIds
            .Select(id => Profiles.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

    public void MoveFavorite(ProfileDefinition profile, int delta)
    {
        int index = FavoriteProfileIds.IndexOf(profile.Id);
        if (index < 0) return;
        int newIndex = index + delta;
        if (newIndex < 0 || newIndex >= FavoriteProfileIds.Count) return;
        (FavoriteProfileIds[index], FavoriteProfileIds[newIndex]) = (FavoriteProfileIds[newIndex], FavoriteProfileIds[index]);
        Save();
    }

    private void EnsureDefaultIsValid()
    {
        if (Profiles.Count == 0) { DefaultProfileId = null; return; }
        if (DefaultProfileId is null || Profiles.All(p => p.Id != DefaultProfileId))
            DefaultProfileId = Profiles[0].Id;
    }

    private static string HomeDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Only ever proposes profiles for apps this machine actually has.</summary>
    private static List<ProfileDefinition> CreateDefaults()
    {
        var list = new List<ProfileDefinition>
        {
            new("Command Prompt", Path.Combine(Environment.SystemDirectory, "cmd.exe"), Array.Empty<string>(), HomeDir),
        };

        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(powershell))
            list.Add(new ProfileDefinition("Windows PowerShell", powershell, Array.Empty<string>(), HomeDir));

        const string gitBash = @"C:\Program Files\Git\bin\bash.exe";
        if (File.Exists(gitBash))
            list.Add(new ProfileDefinition("Git Bash", gitBash, new[] { "--login", "-i" }, HomeDir));

        if (TryResolveOnPath("claude", out var claudePath))
            list.Add(new ProfileDefinition("Claude Code", claudePath, Array.Empty<string>(), HomeDir));

        return list;
    }

    private static bool TryResolveOnPath(string name, out string fullPath)
    {
        fullPath = "";
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".exe;.cmd;.bat").Split(';');

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in pathExt)
            {
                string candidate;
                try { candidate = Path.Combine(dir.Trim(), name + ext); }
                catch (ArgumentException) { continue; } // malformed PATH entry

                if (File.Exists(candidate))
                {
                    fullPath = candidate;
                    return true;
                }
            }
        }
        return false;
    }

    private sealed class StoreFile
    {
        public List<ProfileDefinition> Profiles { get; set; } = new();
        public string? DefaultProfileId { get; set; }
        public List<string> FavoriteProfileIds { get; set; } = new();
    }
}
