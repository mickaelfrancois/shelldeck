using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ShellDeck.Shells;

namespace ShellDeck.Git;

/// <summary>What to probe. For Windows, <paramref name="Directory"/> is <paramref name="KnownTopLevel"/>, the root already resolved; for WSL it is the pane directory and the root is <c>null</c>.</summary>
public sealed record GitTarget(ShellProfile Profile, string Directory, string? KnownTopLevel);

/// <summary>Outcome of a probe: the state, "not a repository" (clear the state) or a failure (keep the last known state).</summary>
public abstract record GitProbeResult
{
    private GitProbeResult()
    {
    }

    public sealed record Ok(GitStatus Status, string TopLevel) : GitProbeResult;

    public sealed record NotARepo : GitProbeResult;

    public sealed record Failed : GitProbeResult;
}

public interface IGitStatusProbe
{
    Task<GitProbeResult> ProbeAsync(GitTarget target, CancellationToken ct);
}

/// <summary>Maps the outcome of a git process to a result. Pure.</summary>
public static class GitProbe
{
    private const int WslNotARepoExitCode = 3;
    private const int GitNotARepoExitCode = 128;

    /// <summary>Exit codes only: git messages are translated, stderr is never read.</summary>
    /// <param name="knownTopLevel">The root for a Windows target; <c>null</c> for WSL, whose first output line is the root.</param>
    public static GitProbeResult Interpret(int exitCode, string stdout, string? knownTopLevel)
    {
        if (exitCode is WslNotARepoExitCode or GitNotARepoExitCode)
            return new GitProbeResult.NotARepo();

        if (exitCode != 0)
            return new GitProbeResult.Failed();

        string topLevel;
        string porcelain;
        if (knownTopLevel is null)
        {
            int newline = stdout.IndexOf('\n');
            if (newline < 0)
                return new GitProbeResult.Failed();

            topLevel = stdout[..newline].TrimEnd('\r');
            porcelain = stdout[(newline + 1)..];
        }
        else
        {
            topLevel = knownTopLevel;
            porcelain = stdout;
        }

        return GitStatusParser.TryParse(topLevel, porcelain, out GitStatus status)
            ? new GitProbeResult.Ok(status, topLevel)
            : new GitProbeResult.Failed();
    }
}

/// <summary>Runs git in a hidden process, off the calling thread. A cancelled token kills the process tree.</summary>
public sealed class ProcessGitStatusProbe : IGitStatusProbe
{
    public async Task<GitProbeResult> ProbeAsync(GitTarget target, CancellationToken ct)
    {
        GitCommandLine? command = GitCommand.For(target.Profile, target.Directory);
        if (command is null)
            return new GitProbeResult.Failed();

        var startInfo = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (string argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("git did not start");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new GitProbeResult.Failed();
        }

        using (process)
        {
            try
            {
                // Both streams are drained so a full pipe never blocks git; stderr is discarded.
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
                Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                string output = await stdout.ConfigureAwait(false);
                await stderr.ConfigureAwait(false);
                return GitProbe.Interpret(process.ExitCode, output, target.KnownTopLevel);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                return new GitProbeResult.Failed();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
            {
                Kill(process);
                return new GitProbeResult.Failed();
            }
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
    }
}
