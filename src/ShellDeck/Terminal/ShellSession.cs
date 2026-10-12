using System.Diagnostics;
using System.IO;
using System.Text;
using ShellDeck.Pty;

namespace ShellDeck.Terminal;

/// <summary>Where a shell's output goes while a terminal view shows it.</summary>
public interface IOutputSink
{
    /// <summary>Takes the text without blocking; called under the session's lock, from the output pump.</summary>
    void Append(ReadOnlySpan<char> text);

    /// <summary>Drops the text not yet shown: the history replayed on attach already holds it.</summary>
    void Clear();

    /// <summary>Blocks the output pump while the view is behind, which throttles the program.</summary>
    void WaitForRoom();
}

/// <summary>
/// A running shell, owned by its pane rather than by the view that shows it: WPF may unload and rebuild
/// the views at any time (session locked, display turned off), and the shell must outlive them.
/// </summary>
public sealed class ShellSession : IDisposable
{
    private const int OutputBufferSize = 16 * 1024;
    private const int HistoryCapacity = 512 * 1024;

    // ConPTY may still be rendering the last output when the process ends: let it reach the page first.
    private static readonly TimeSpan ExitFlushDelay = TimeSpan.FromMilliseconds(150);

    private readonly PtySession _pty;
    private readonly OutputHistory _history = new(HistoryCapacity);
    private readonly Lock _outputLock = new();
    private IOutputSink? _sink;
    private Task _pendingInput = Task.CompletedTask;
    private volatile bool _isDisposed;

    private ShellSession(PtySession pty) => _pty = pty;

    public bool HasRunningCommand => _pty.HasRunningCommand;

    /// <summary>Starts the shell. Call from the UI thread: <paramref name="onExited"/> is raised there.</summary>
    public static ShellSession Start(string commandLine, string? workingDirectory, TerminalSize size, Action<int, TimeSpan> onExited)
    {
        var session = new ShellSession(PtySession.Start(commandLine, workingDirectory, size));

        // The read blocks for the whole life of the shell: a dedicated thread rather than one taken from the pool.
        new Thread(session.PumpOutput) { IsBackground = true, Name = "ShellDeck output pump" }.Start();
        _ = session.ReportExitAsync(onExited);
        return session;
    }

    /// <summary>Routes the output to <paramref name="sink"/>.</summary>
    /// <returns>The recent output, to write to the page before anything the sink receives.</returns>
    public string Attach(IOutputSink sink)
    {
        lock (_outputLock)
        {
            _sink?.Clear();
            _sink = sink;
            sink.Clear();
            return _history.ToString();
        }
    }

    public void Detach(IOutputSink sink)
    {
        lock (_outputLock)
        {
            if (_sink == sink)
            {
                _sink = null;
            }
        }
    }

    public void Resize(TerminalSize size) => _pty.Resize(size);

    public void WriteInput(string data)
    {
        // The input pipe holds a few KB: a large paste into a busy program would block until it reads,
        // so the write happens off the UI thread. Writes stay in order: each waits for the previous one.
        var bytes = Encoding.UTF8.GetBytes(data);
        _pendingInput = _pendingInput.ContinueWith(
            _ =>
            {
                try
                {
                    _pty.Input.Write(bytes);
                    _pty.Input.Flush();
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    // The shell is gone: its input no longer matters.
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Ends the shell. Before Windows 11 24H2, closing a pseudo console waits for its programs to handle
    /// the close event: off the UI thread, so closing a tab never freezes the window.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        lock (_outputLock)
        {
            _sink = null;
        }

        _ = Task.Run(_pty.Dispose);
    }

    private void PumpOutput()
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[OutputBufferSize];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(OutputBufferSize)];

        try
        {
            int read;
            while ((read = _pty.Output.Read(bytes)) > 0)
            {
                var charCount = decoder.GetChars(bytes, 0, read, chars, 0);
                Publish(chars.AsSpan(0, charCount))?.WaitForRoom();
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Disposing the session closes the pipe under the pending read: this is the normal end of the pump.
        }
    }

    /// <returns>The sink that received the text, if a view is attached.</returns>
    private IOutputSink? Publish(ReadOnlySpan<char> text)
    {
        lock (_outputLock)
        {
            _history.Append(text);
            _sink?.Append(text);
            return _sink;
        }
    }

    private async Task ReportExitAsync(Action<int, TimeSpan> onExited)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var exitCode = await _pty.WaitForExitAsync();
        if (exitCode is not { } code)
        {
            return;
        }

        var lifetime = Stopwatch.GetElapsedTime(startedAt);
        await Task.Delay(ExitFlushDelay);
        if (_isDisposed)
        {
            return;
        }

        Publish($"\r\n\x1b[90m[processus terminé, code {code}]\x1b[0m\r\n");
        onExited(code, lifetime);
    }
}
