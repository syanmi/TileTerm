using System;
using System.IO;

namespace TileTerm;

/// <summary>
/// Where the app keeps its user data (profiles.json, settings.json).
///
/// Normally that is <c>%AppData%\TileTerm</c>. A portable copy — the zip build — carries an empty
/// marker file, <see cref="PortableMarkerFileName"/>, next to the exe; when it is there the data
/// lives in a <c>data</c> folder beside the exe instead, so the whole app can be carried on a USB
/// stick or deleted without leaving anything behind. If that folder cannot be written to (say the
/// zip was unpacked under Program Files), it silently falls back to the normal location.
/// </summary>
internal static class AppPaths
{
    public const string PortableMarkerFileName = "TileTerm.portable";

    /// <summary>True when the data folder is the one next to the exe.</summary>
    public static bool IsPortable { get; }

    /// <summary>The folder holding the app's data files. It exists once this is read.</summary>
    public static string DataDirectory { get; }

    static AppPaths()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TileTerm");

        // ProcessPath rather than AppContext.BaseDirectory: for a single-file build it is the exe
        // itself, so the marker is looked for where the user actually put the file.
        var exeDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDirectory is not null && File.Exists(Path.Combine(exeDirectory, PortableMarkerFileName)))
        {
            var portable = Path.Combine(exeDirectory, "data");
            if (TryUse(portable))
            {
                IsPortable = true;
                DataDirectory = portable;
                return;
            }
        }

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
