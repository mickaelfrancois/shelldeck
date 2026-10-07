using System.IO;
using Microsoft.Win32;

namespace ShellDeck.Shells;

/// <summary>Lists the shells installed on this machine and the SSH connections of the user's OpenSSH config.</summary>
public static class ShellDiscovery
{
    private const string WslDistributionsRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";
    private const string WslHomeDirectory = "~";

    private static string UserProfileDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static IReadOnlyList<ShellProfile> DiscoverShells() =>
    [
        .. DiscoverWindowsShells(),
        .. DiscoverWslDistributions(),
    ];

    public static IReadOnlyList<ShellProfile> DiscoverSshConnections()
    {
        var bundledClient = Path.Combine(Environment.SystemDirectory, @"OpenSSH\ssh.exe");
        var sshClient = File.Exists(bundledClient) ? Quote(bundledClient) : "ssh.exe";

        return SshConfig.ReadUserHosts()
            .Select(host => new ShellProfile(host.Alias, $"{sshClient} {host.Alias}", ShellKind.Ssh, host.Target))
            .ToList();
    }

    private static IEnumerable<ShellProfile> DiscoverWindowsShells()
    {
        var pwsh = FindPowerShellCore();
        if (pwsh is not null)
        {
            yield return new ShellProfile("PowerShell 7", Quote(pwsh), ShellKind.PowerShell, UserProfileDirectory);
        }

        var windowsPowerShell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        if (File.Exists(windowsPowerShell))
        {
            yield return new ShellProfile("Windows PowerShell", Quote(windowsPowerShell), ShellKind.WindowsPowerShell, UserProfileDirectory);
        }

        var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
        yield return new ShellProfile("Invite de commandes", Quote(cmd), ShellKind.Cmd, UserProfileDirectory);
    }

    /// <summary>The highest stable version under Program Files\PowerShell\&lt;major&gt;; previews ("7-preview") are skipped.</summary>
    private static string? FindPowerShellCore()
    {
        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell");
        string? installed = null;
        try
        {
            installed = Directory.Exists(installRoot)
                ? Directory.EnumerateDirectories(installRoot)
                    .Select(directory => (Directory: directory, IsVersion: int.TryParse(Path.GetFileName(directory), out var major), Major: major))
                    .Where(candidate => candidate.IsVersion)
                    .OrderByDescending(candidate => candidate.Major)
                    .Select(candidate => Path.Combine(candidate.Directory, "pwsh.exe"))
                    .FirstOrDefault(File.Exists)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable install folder must not prevent startup: fall back to PATH.
        }

        return installed ?? FindOnPath("pwsh.exe");
    }

    private static string? FindOnPath(string executable) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executable))
            .FirstOrDefault(File.Exists);

    private static IEnumerable<ShellProfile> DiscoverWslDistributions()
    {
        using var distributions = Registry.CurrentUser.OpenSubKey(WslDistributionsRegistryKey);
        if (distributions is null)
        {
            return [];
        }

        return distributions.GetSubKeyNames()
            .Select(id => ReadDistributionName(distributions, id))
            .OfType<string>()
            .Order()
            .Select(name => new ShellProfile(name, $"wsl.exe -d {name}", ShellKind.Wsl, WslHomeDirectory))
            .ToList();
    }

    private static string? ReadDistributionName(RegistryKey distributions, string distributionId)
    {
        using var distribution = distributions.OpenSubKey(distributionId);
        return distribution?.GetValue("DistributionName") as string;
    }

    private static string Quote(string path) => $"\"{path}\"";
}
