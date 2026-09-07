using System;
using System.IO;
using System.Linq;

namespace TileTerm.Terminal;

/// <summary>
/// Turns a <see cref="ProfileDefinition"/> into the actual (exe, args) pair to
/// hand to ConPTY.
///
/// This exists because of a real ConPTY/CreateProcess quirk: Windows can only
/// directly launch a native executable (.exe/.com). Many real console tools —
/// most npm-installed CLIs among them — install as a <c>.cmd</c> or
/// <c>.bat</c> shim rather than a .exe, and pointing ConPTY straight at one
/// of those fails. The fix is to run the shim through <c>cmd.exe /c</c>
/// instead, same as double-clicking it would.
/// </summary>
internal static class ProcessLaunchResolver
{
    public static (string Exe, string[] Args) Resolve(ProfileDefinition profile)
    {
        var ext = Path.GetExtension(profile.Executable);
        if (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var args = new[] { "/c", profile.Executable }.Concat(profile.Arguments).ToArray();
            return (cmdExe, args);
        }

        return (profile.Executable, profile.Arguments);
    }
}
