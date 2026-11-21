using System.Diagnostics;
using System.IO;
using System.Text;

namespace ShellDeck.TabLines;

/// <summary>Why a run gave no output.</summary>
public enum TabCommandFailure
{
    None,
    NotStarted,
    TimedOut,
    ExitCode,
    TooMuchOutput,
}

/// <summary>
/// What one run of the tab command did. <paramref name="Output"/> is null whenever <paramref name="Failure"/> is not
/// <see cref="TabCommandFailure.None"/>. <paramref name="Error"/> is the start of its standard error, for the diagnostic.
/// </summary>
public sealed record TabCommandRun(string? Output, string Error, int? ExitCode, TimeSpan Elapsed, TabCommandFailure Failure)
{
    public static TabCommandRun Succeeded(string output) => new(output, string.Empty, 0, TimeSpan.Zero, TabCommandFailure.None);

    public static TabCommandRun Failed(TabCommandFailure failure, string error = "") => new(null, error, null, TimeSpan.Zero, failure);
}

/// <summary>Runs the tab command once with the tab's data on its standard input.</summary>
public interface ITabCommandRunner
{
    Task<TabCommandRun> RunAsync(IReadOnlyList<string> command, string input, TimeSpan timeout, CancellationToken cancellation);
}

/// <summary>
/// The command is a program and its arguments, started directly: no shell, so nothing in the input can be taken for a
/// command. It has a delay, then its whole process tree is killed.
/// </summary>
public sealed class ProcessTabCommandRunner : ITabCommandRunner
{
    private const int MaxErrorLength = 4096;

    public async Task<TabCommandRun> RunAsync(IReadOnlyList<string> command, string input, TimeSpan timeout, CancellationToken cancellation)
    {
        if (command.Count == 0 || string.IsNullOrWhiteSpace(command[0]))
        {
            return TabCommandRun.Failed(TabCommandFailure.NotStarted, "Aucun programme à lancer.");
        }

        var start = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in command.Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        var watch = Stopwatch.StartNew();
        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return TabCommandRun.Failed(TabCommandFailure.NotStarted, exception.Message);
        }

        using (process)
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            limit.CancelAfter(timeout);
            try
            {
                // Drained at once, or a chatty command would block on a full pipe and look slow.
                var error = ReadLimitedAsync(process.StandardError, MaxErrorLength, limit.Token);
                var output = ReadLimitedAsync(process.StandardOutput, TabCommandOutput.MaxOutputBytes, limit.Token);
                try
                {
                    await process.StandardInput.WriteAsync(input.AsMemory(), limit.Token);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The command did not read its input: its output is still worth reading.
                }

                var (text, overflow) = await output;
                await process.WaitForExitAsync(limit.Token);
                var (errorText, _) = await error;
                var exitCode = process.ExitCode;
                var failure = overflow ? TabCommandFailure.TooMuchOutput : exitCode != 0 ? TabCommandFailure.ExitCode : TabCommandFailure.None;
                return new TabCommandRun(failure is TabCommandFailure.None ? text : null, errorText, exitCode, watch.Elapsed, failure);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                return new TabCommandRun(null, string.Empty, null, watch.Elapsed, TabCommandFailure.TimedOut);
            }
        }
    }

    /// <summary>The start of the stream, up to <paramref name="limit"/> characters; true when there was more.</summary>
    private static async Task<(string Text, bool Overflow)> ReadLimitedAsync(StreamReader reader, int limit, CancellationToken cancellation)
    {
        var buffer = new char[4096];
        var text = new StringBuilder();
        var overflow = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellation)) > 0)
        {
            if (text.Length + read > limit)
            {
                overflow = true;
                text.Append(buffer, 0, limit - text.Length);
                continue; // keep draining: stopping would block the command on a full pipe
            }

            text.Append(buffer, 0, read);
        }

        return (text.ToString(), overflow);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }
}
