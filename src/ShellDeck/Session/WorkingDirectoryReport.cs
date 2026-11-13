using System.Text.RegularExpressions;

namespace ShellDeck.Session;

/// <summary>
/// Reads the current directory a shell reports through OSC 7 (<c>file://host/path</c>) or ConEmu's OSC 9;9
/// (<c>9;"C:\path"</c>). The payload comes from the terminal, so anything that is not a plain absolute path is refused.
/// </summary>
public static partial class WorkingDirectoryReport
{
    private const string FilePrefix = "file://";
    private const string ConEmuPrefix = "9;";

    [GeneratedRegex(@"^[A-Za-z]:[\\/]")]
    private static partial Regex DriveRoot();

    [GeneratedRegex(@"^/[A-Za-z]:[\\/]")]
    private static partial Regex UriDriveRoot();

    /// <summary>
    /// <paramref name="data"/> is the OSC 7 payload (<c>file://host/path</c>) or the OSC 9 payload starting with <c>9;</c>.
    /// Progress (<c>9;4;…</c>), plain notifications, relative paths, other URI schemes, quotes and control characters give <c>false</c>.
    /// </summary>
    public static bool TryParse(string? data, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(data))
        {
            return false;
        }

        string? candidate = data.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)
            ? FromFileUri(data[FilePrefix.Length..])
            : data.StartsWith(ConEmuPrefix, StringComparison.Ordinal) ? FromConEmu(data[ConEmuPrefix.Length..]) : null;

        if (candidate is null || !IsAbsolutePlainPath(candidate))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    private static string? FromFileUri(string afterScheme)
    {
        int slash = afterScheme.IndexOf('/');
        if (slash < 0 || afterScheme.Contains('?') || afterScheme.Contains('#'))
        {
            return null;
        }

        string unescaped;
        try
        {
            unescaped = Uri.UnescapeDataString(afterScheme[slash..]);
        }
        catch (UriFormatException)
        {
            return null;
        }

        // "/C:/Users/me" is a Windows path, "/home/me" stays a Linux one.
        return UriDriveRoot().IsMatch(unescaped) ? unescaped[1..].Replace('/', '\\') : unescaped;
    }

    private static string? FromConEmu(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        return value;
    }

    private static bool IsAbsolutePlainPath(string path)
    {
        if (path.Length == 0 || path.Any(character => character == '"' || char.IsControl(character)))
        {
            return false;
        }

        return DriveRoot().IsMatch(path) || path.StartsWith('/') || path.StartsWith(@"\\", StringComparison.Ordinal);
    }
}
