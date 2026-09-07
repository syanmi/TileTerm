using System;

namespace TileTerm.Terminal;

/// <summary>
/// Describes how to launch one console application: the executable, its
/// startup arguments, and the working directory it should start in.
///
/// This is the "プロファイル" (profile) concept from the project's CLAUDE.md
/// concept notes: the terminal engine itself never hard-codes which console
/// app it runs — every session is started from one of these definitions.
/// Profiles are user-editable (see <see cref="ProfileSettingsWindow"/>) and
/// persisted by <see cref="ProfileStore"/>.
/// </summary>
public sealed class ProfileDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Profile";
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
        Executable = Executable,
        Arguments = (string[])Arguments.Clone(),
        WorkingDirectory = WorkingDirectory,
    };
}
