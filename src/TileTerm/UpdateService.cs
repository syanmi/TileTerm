using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace TileTerm;

internal enum UpdateOutcome
{
    /// <summary>Not running from a Velopack install (an IDE build, say): there is nothing to update.</summary>
    NotInstalled,
    UpToDate,
    /// <summary>A newer version was found and its package is downloaded, waiting for a restart.</summary>
    Downloaded,
    Failed,
}

internal readonly record struct UpdateCheckResult(UpdateOutcome Outcome, string? Version = null, string? Error = null);

/// <summary>
/// Looks for a newer release of TileTerm on GitHub, downloads it, and applies it on request.
///
/// It only downloads: applying an update restarts the app and closes every open tile, so that is left
/// to the user (the title bar's "更新" button, see <see cref="MainWindow"/>). What was downloaded stays
/// on disk, so a version the user postponed is offered again at the next launch without downloading twice.
///
/// Updates come from the GitHub Releases of <see cref="RepositoryUrl"/> — the feed and packages the release
/// workflow uploads (see scripts/build_release.ps1). Only stable releases are considered.
/// </summary>
internal sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/syanmi/TileTerm";

    /// <summary>For testing only: a folder or web address holding a Velopack feed (the
    /// <c>releases.win.json</c> and packages a release publishes) that is used instead of GitHub. It is what
    /// lets an update be exercised end to end before it exists on GitHub.</summary>
    public const string SourceOverrideVariable = "TILETERM_UPDATE_SOURCE";

    public static UpdateService Instance { get; } = new();

    private readonly UpdateManager? _manager;
    private VelopackAsset? _ready;
    private int _busy;

    private UpdateService()
    {
        try
        {
            _manager = new UpdateManager(CreateSource());
            // A package downloaded in an earlier run that the user never applied.
            _ready = _manager.IsInstalled ? _manager.UpdatePendingRestart : null;
        }
        catch (Exception)
        {
            _manager = null;   // no usable Velopack context: behave as "not installed"
        }
    }

    /// <summary>True when the app was installed (or unzipped, for the portable build) by Velopack.</summary>
    public bool IsInstalled => _manager?.IsInstalled == true;

    /// <summary>The running version, e.g. "0.1.1".</summary>
    public string CurrentVersion =>
        (IsInstalled ? _manager!.CurrentVersion?.ToString() : null)
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "?";

    /// <summary>The downloaded version waiting to be applied, or null.</summary>
    public string? PendingVersion => _ready?.Version.ToString();

    /// <summary>Raised (on the thread that started the check) when <see cref="PendingVersion"/> changes.</summary>
    public event Action? PendingChanged;

    /// <summary>Checks for a newer version and, if there is one, downloads it. Never throws: a failure
    /// (offline, GitHub unreachable, ...) is reported in the result.</summary>
    public async Task<UpdateCheckResult> CheckAndDownloadAsync()
    {
        if (!IsInstalled) return new UpdateCheckResult(UpdateOutcome.NotInstalled);
        if (_ready is not null) return new UpdateCheckResult(UpdateOutcome.Downloaded, PendingVersion);
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return new UpdateCheckResult(UpdateOutcome.Failed, Error: "別の確認が進行中です。");

        try
        {
            var info = await _manager!.CheckForUpdatesAsync();
            if (info is null) return new UpdateCheckResult(UpdateOutcome.UpToDate);

            await _manager.DownloadUpdatesAsync(info);
            _ready = info.TargetFullRelease;
            PendingChanged?.Invoke();
            return new UpdateCheckResult(UpdateOutcome.Downloaded, PendingVersion);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateOutcome.Failed, Error: ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>Starts the updater, which waits for this process to end, installs the downloaded version and
    /// launches it. The caller then closes the app normally (so every session is shut down properly), which is
    /// why this does not exit the process itself. Does nothing if there is no downloaded update.</summary>
    public void ApplyAfterExitAndRestart()
    {
        if (_manager is null || _ready is null) return;
        _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: true);
    }

    private static IUpdateSource CreateSource()
    {
        var overrideValue = Environment.GetEnvironmentVariable(SourceOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            if (Directory.Exists(overrideValue)) return new SimpleFileSource(new DirectoryInfo(overrideValue));
            if (Uri.TryCreate(overrideValue, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                return new SimpleWebSource(overrideValue);
        }
        return new GithubSource(RepositoryUrl, accessToken: null, prerelease: false);
    }
}
