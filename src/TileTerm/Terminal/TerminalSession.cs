using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Porta.Pty;
using XTerm;
using XTerm.Options;

namespace TileTerm.Terminal;

/// <summary>
/// Owns one console-app session end to end: the child process (spawned behind
/// a real Windows pseudo console via Porta.Pty/ConPTY) and the XTerm.NET
/// virtual terminal that interprets its VT100/ANSI output into a screen buffer
/// a UI can render.
///
/// This is intentionally UI-agnostic: <see cref="TerminalCanvas"/> only reads
/// <see cref="Terminal"/>'s buffer and calls <see cref="SendAsync"/>.
/// </summary>
public sealed class TerminalSession : IDisposable
{
    /// <summary>Lines of output kept above the screen to scroll back to — the Windows console's own default.</summary>
    private const int ScrollbackLines = 9001;

    private readonly CancellationTokenSource _cts = new();
    private IPtyConnection? _pty;

    public XTerm.Terminal Terminal { get; }

    /// <summary>Raised (on a background thread) whenever new output has been parsed into the buffer.</summary>
    public event Action? OutputReceived;

    /// <summary>Raised when the child process exits.</summary>
    public event Action<int>? Exited;

    public TerminalSession(int cols, int rows)
    {
        Terminal = new XTerm.Terminal(new TerminalOptions
        {
            Cols = cols,
            Rows = rows,
            Scrollback = ScrollbackLines,
        });

        // Some escape sequences (e.g. device-attribute queries) are answered
        // by the terminal itself; those replies must be sent back to the
        // child process, not displayed.
        Terminal.DataReceived += (_, e) => _ = SendRawAsync(Encoding.UTF8.GetBytes(e.Data));
    }

    public async Task StartAsync(ProfileDefinition profile)
    {
        var (exe, args) = ProcessLaunchResolver.Resolve(profile);
        var options = new PtyOptions
        {
            Name = profile.Name,
            App = exe,
            CommandLine = args,
            Cwd = profile.WorkingDirectory ?? Environment.CurrentDirectory,
            Cols = Terminal.Cols,
            Rows = Terminal.Rows,
            Environment = new Dictionary<string, string>(),
        };

        _pty = await PtyProvider.SpawnAsync(options, _cts.Token).ConfigureAwait(false);
        _pty.ProcessExited += (_, _) => Exited?.Invoke(_pty.ExitCode);

        _ = Task.Run(ReadLoopAsync);
    }

    private async Task ReadLoopAsync()
    {
        if (_pty is null) return;

        var buffer = new byte[4096];
        var chars = new char[4096];
        var decoder = Encoding.UTF8.GetDecoder();

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int read = await _pty.ReaderStream.ReadAsync(buffer, 0, buffer.Length, _cts.Token).ConfigureAwait(false);
                if (read <= 0) break;

                int charCount = decoder.GetChars(buffer, 0, read, chars, 0);
                if (charCount > 0)
                {
                    lock (Terminal) Terminal.Write(new string(chars, 0, charCount));
                    OutputReceived?.Invoke();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Session is being torn down; nothing to do.
        }
        catch (Exception)
        {
            // The pty pipe breaks once the child process exits; ProcessExited
            // already reports that, so swallow the resulting read error here.
        }
    }

    /// <summary>Sends user input (already translated to the wire format) to the child process.</summary>
    public Task SendRawAsync(byte[] data) => SendRawAsync(data, data.Length);

    private async Task SendRawAsync(byte[] data, int length)
    {
        if (_pty is null || length == 0) return;
        try
        {
            await _pty.WriterStream.WriteAsync(data, 0, length, _cts.Token).ConfigureAwait(false);
            await _pty.WriterStream.FlushAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Sends plain text (as UTF-8) to the child process.</summary>
    public Task SendTextAsync(string text) => SendRawAsync(Encoding.UTF8.GetBytes(text));

    public void Resize(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0) return;
        lock (Terminal) Terminal.Resize(cols, rows);
        _pty?.Resize(cols, rows);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _pty?.Kill(); } catch { /* already exited */ }
        _pty?.Dispose();
        Terminal.Dispose();
        _cts.Dispose();
    }
}
