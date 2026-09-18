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

    /// <summary>Where this profile's icon comes from: an image file (shown as-is), or any
    /// other file (that file's own shell icon is used). Null/empty means "use the icon of
    /// <see cref="Executable"/>". See <see cref="TileTerm.ProfileIcons"/>.</summary>
    public string? IconPath { get; set; }

    public string Executable { get; set; } = "";
    public string[] Arguments { get; set; } = Array.Empty<string>();
    public string? WorkingDirectory { get; set; }

    public ProfileDefinition() { }

    public ProfileDefinition(string name, string executable, string[] arguments, string? workingDirectory = null)
    {
        Name = name;
        Executable = executable;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
    }

    public ProfileDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        IconPath = IconPath,
        Executable = Executable,
        Arguments = (string[])Arguments.Clone(),
        WorkingDirectory = WorkingDirectory,
    };
}
