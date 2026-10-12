using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TileTerm;

/// <summary>
/// Keeps every TileTerm window of one installation in a single process, the way VSCode does: launching
/// the app again does not start a second, independent copy — it asks the running one to open another
/// window and exits. Because all windows then live in one process they share one copy of the settings
/// (<see cref="AppSettings.Current"/>, <see cref="Terminal.ProfileStore.Shared"/>, the theme), so a change
/// made in one window's settings dialog reaches every window at once.
///
/// The first process owns a named mutex and listens on a named pipe; a later launch finds the mutex
/// taken, connects to the pipe, sends <see cref="OpenWindowCommand"/> and waits for "ok". Both names are
/// derived from the folder the exe runs from, so the installed app, a portable copy and a build run from
/// an IDE are separate instances (they are separate programs and may be different versions). The pipe
/// only accepts connections from the same user (<see cref="PipeOptions.CurrentUserOnly"/>).
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string OpenWindowCommand = "open-window";
    private const string OkReply = "ok";

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Func<bool>? _openWindow;
    private Dispatcher? _dispatcher;

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>
    /// Returns the instance object when this process is the first one (it then owns the app and must
    /// call <see cref="Listen"/>), or null when another TileTerm already runs and has been asked to open
    /// a new window — the caller should exit. If the other process cannot be reached (it is in the middle
    /// of closing, say), this keeps trying to become the first one for a few seconds before giving up and
    /// running on its own.
    /// </summary>
    public static SingleInstance? Start()
    {
        var id = InstanceId();
        var pipeName = $"TileTerm-{id}-{Process.GetCurrentProcess().SessionId}";
        var mutex = new Mutex(false, $@"Local\TileTerm-{id}");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            if (TryAcquire(mutex)) return new SingleInstance(mutex, pipeName);
            if (TryForwardToRunningInstance(pipeName)) { mutex.Dispose(); return null; }

            if (DateTime.UtcNow > deadline)
            {
                // Something is badly stuck (a hung first instance holding the mutex). Starting a
                // separate copy beats doing nothing at all when the user asked for a window.
                return new SingleInstance(mutex, pipeName);
            }
            Thread.Sleep(200);
        }
    }

    /// <summary>Starts answering other launches. <paramref name="openWindow"/> runs on
    /// <paramref name="dispatcher"/>'s thread and returns false when no window could be opened
    /// (the app is shutting down) — the other launch then tries again to become the first instance.</summary>
    public void Listen(Dispatcher dispatcher, Func<bool> openWindow)
    {
        _dispatcher = dispatcher;
        _openWindow = openWindow;
        _ = Task.Run(ServeAsync);
    }

    private async Task ServeAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { await Task.Delay(500).ConfigureAwait(false); continue; }

            _ = Task.Run(() => HandleClientAsync(server));
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server)
    {
        await using var _ = server;
        try
        {
            using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

            var command = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
            bool opened = command == OpenWindowCommand && OpenWindowOnUiThread();
            await writer.WriteLineAsync(opened ? OkReply : "busy").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The other launch gave up waiting or went away; it retries on its own.
        }
    }

    private bool OpenWindowOnUiThread()
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || _openWindow is null || dispatcher.HasShutdownStarted) return false;
        try
        {
            return dispatcher.Invoke(_openWindow, DispatcherPriority.Normal, CancellationToken.None, TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryAcquire(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;   // the previous owner died without releasing it; it is ours now
        }
    }

    private static bool TryForwardToRunningInstance(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect(2000);

            // This process was just started by the user, so it may bring a window to the front; the
            // running instance may not. Hand that permission over so the new window shows up on top.
            if (GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out var serverPid))
                AllowSetForegroundWindow(serverPid);

            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
            writer.WriteLine(OpenWindowCommand);

            var reply = reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            return reply == OkReply;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A short, stable id for "this copy of TileTerm": a hash of the folder the exe runs from.
    /// The installed app always runs from its <c>current</c> folder, so the id survives updates.</summary>
    private static string InstanceId()
    {
        var folder = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\', '/').ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(folder));
        return Convert.ToHexString(hash, 0, 8);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned (the give-up path) */ }
        _mutex.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
