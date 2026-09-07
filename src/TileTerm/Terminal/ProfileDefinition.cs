using System;
using System.IO;

namespace TileTerm.Terminal;

/// <summary>
/// Describes how to launch one console application: the executable, its
/// startup arguments, and the working directory it should start in.
///
/// This is the "プロファイル" (profile) concept from the project's CLAUDE.md
/// concept notes: the terminal engine itself never hard-codes which console
/// app it runs — every session is started from one of these definitions.
/// A future settings UI will let the user create/edit/select profiles;
/// for this first milestone only <see cref="Default"/> is wired up.
/// </summary>
public sealed record ProfileDefinition(
    string Name,
    string Executable,
    string[] Arguments,
    string? WorkingDirectory = null)
{
    /// <summary>
    /// The hard-coded profile used by the first milestone: plain cmd.exe.
    /// </summary>
    public static ProfileDefinition Default { get; } = new(
        Name: "Command Prompt",
        Executable: Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        Arguments: Array.Empty<string>(),
        WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}
