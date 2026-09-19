using System;
using System.IO;
using Velopack.Locators;

namespace TileTerm;

/// <summary>
/// Where the app keeps its user data (profiles.json, settings.json).
///
/// Normally that is <c>%AppData%\TileTerm</c> — for the installed app and for a build run from an IDE.
/// The portable package (the Velopack "Portable" zip) is different: it is meant to leave nothing
/// behind, so its data lives in a <c>data</c> folder next to the package's root. That folder must be
/// the package root and not the folder the exe runs from: an update replaces the app's <c>current</c>
/// folder wholesale, which would delete data stored inside it. If the folder cannot be written to
/// (the zip was unpacked under Program Files, say), it falls back to the normal location.
/// </summary>
internal static class AppPaths
{
    /// <summary>True when the data folder is the portable one.</summary>
    public static bool IsPortable { get; }

    /// <summary>The folder holding the app's data files. It exists once this is read.</summary>
    public static string DataDirectory { get; }

    static AppPaths()
    {
        if (VelopackLocator.IsCurrentSet && VelopackLocator.Current is { IsPortable: true, RootAppDir: { Length: > 0 } root })
        {
            var portable = Path.Combine(root, "data");
            if (TryUse(portable))
            {
                IsPortable = true;
                DataDirectory = portable;
                return;
            }
        }

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TileTerm");
        Directory.CreateDirectory(appData);
        DataDirectory = appData;
    }

    private static bool TryUse(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, Path.GetRandomFileName());
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
