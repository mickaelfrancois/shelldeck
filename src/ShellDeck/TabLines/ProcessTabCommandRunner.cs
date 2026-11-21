using System.Diagnostics;
using System.IO;
using System.Text;

namespace ShellDeck.TabLines;

/// <summary>Runs the tab command once with the tab's data on its standard input.</summary>
public interface ITabCommandRunner
{
    /// <summary>The standard output of the command; null when it could not start, failed, was too slow or printed too much.</summary>
    Task<string?> RunAsync(IReadOnlyList<string> command, string input, TimeSpan timeout, CancellationToken cancellation);
}

/// <summary>
/// The command is a program and its arguments, started directly: no shell, so nothing in the input can be taken for a
/// command. It has a delay, then its whole process tree is killed.
/// </summary>
public sealed class ProcessTabCommandRunner : ITabCommandRunner
{
    public async Task<string?> RunAsync(IReadOnlyList<string> command, string input, TimeSpan timeout, CancellationToken cancellation)
    {
        if (command.Count == 0 || string.IsNullOrWhiteSpace(command[0]))
        {
            return null;
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
        };
        foreach (var argument in command.Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }

        using (process)
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            limit.CancelAfter(timeout);
            try
            {
                // Drained at once, or a chatty command would block on a full pipe and look slow.
                var error = process.StandardError.ReadToEndAsync(limit.Token);
                var output = ReadLimitedAsync(process.StandardOutput, limit.Token);
                try
                {
                    await process.StandardInput.WriteAsync(input.AsMemory(), limit.Token);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The command did not read its input: its output is still worth reading.
                }

                var text = await output;
                await process.WaitForExitAsync(limit.Token);
                await error;
                return process.ExitCode == 0 ? text : null;
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                return null;
            }
        }
    }

    private static async Task<string?> ReadLimitedAsync(StreamReader reader, CancellationToken cancellation)
    {
        var buffer = new char[4096];
        var text = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellation)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > TabCommandOutput.MaxOutputBytes)
            {
                return null;
            }
        }

        return text.ToString();
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
