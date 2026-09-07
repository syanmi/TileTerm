using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TileTerm.Terminal;

/// <summary>
/// Loads/saves the user's console-app profiles as JSON under
/// <c>%AppData%\TileTerm\profiles.json</c>, and seeds sensible defaults the
/// first time the app runs (only for apps actually found on this machine).
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public List<ProfileDefinition> Profiles { get; private set; } = new();

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
            try
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<List<ProfileDefinition>>(json, JsonOptions);
                if (loaded is { Count: > 0 })
                {
                    Profiles = loaded;
                    return;
                }
            }
            catch
            {
                // Corrupt/unreadable file: fall through and reseed defaults below.
            }
        }

        Profiles = CreateDefaults();
        Save();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Profiles, JsonOptions);
        File.WriteAllText(_filePath, json);
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
}
