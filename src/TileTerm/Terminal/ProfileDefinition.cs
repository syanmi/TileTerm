using System;

namespace TileTerm.Terminal;

/// <summary>
/// Describes how to launch one console application: the executable, its
/// startup arguments, and the working directory it should start in.
///
/// This is the "プロファイル" (profile) concept from the project's CLAUDE.md
/// concept notes: the terminal engine itself never hard-codes which console
/// app it runs — every session is started from one of these definitions.
/// Profiles are user-editable (see <see cref="TileTerm.SettingsWindow"/>) and
/// persisted by <see cref="ProfileStore"/>, which also tracks which one is
/// the default and which are favorites.
/// </summary>
public sealed class ProfileDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Profile";

    /// <summary>Short text shown as this profile's icon (an emoji, or a couple of
    /// characters like "PS" or "$") — kept as plain text rather than an image so it
    /// never depends on a bundled asset or icon font.</summary>
    public string Icon { get; set; } = "";

    public string Executable { get; set; } = "";
    public string[] Arguments { get; set; } = Array.Empty<string>();
    public string? WorkingDirectory { get; set; }

    public ProfileDefinition() { }

    public ProfileDefinition(string name, string executable, string[] arguments, string? workingDirectory = null, string icon = "")
    {
        Name = name;
        Executable = executable;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        Icon = icon;
    }

    /// <summary>The icon to actually display: the configured one, or the profile
    /// name's first letter if none was set.</summary>
    public string DisplayIcon() =>
        string.IsNullOrWhiteSpace(Icon) ? (Name.Length > 0 ? Name[0].ToString().ToUpperInvariant() : "?") : Icon;

    public ProfileDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        Icon = Icon,
        Executable = Executable,
        Arguments = (string[])Arguments.Clone(),
        WorkingDirectory = WorkingDirectory,
    };
}
