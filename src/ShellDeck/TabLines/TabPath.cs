namespace ShellDeck.TabLines;

/// <summary>Cuts a directory into the name of its parent and the name of the folder itself.</summary>
public static class TabPath
{
    /// <summary>
    /// <c>Parent</c> is the last folder above, with its trailing separator (<c>mf/</c>); it is the root itself
    /// (<c>/</c>, <c>C:\</c>) for a folder right under the root, and empty for the root and <c>~</c>.
    /// <c>Current</c> is the folder name, or the root.
    /// </summary>
    public static (string Parent, string Current) Split(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return (string.Empty, string.Empty);
        }

        var path = directory.Trim();
        var isWindows = path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
        var separator = isWindows ? '\\' : '/';
        if (isWindows)
        {
            path = path.Replace('/', '\\');
        }

        var rootLength = isWindows ? (path.Length >= 3 && path[2] == '\\' ? 3 : 2) : path.StartsWith('/') ? 1 : 0;
        var root = path[..rootLength];
        var rest = path[rootLength..].TrimEnd(separator);
        if (rest.Length == 0)
        {
            return (string.Empty, root.Length > 0 ? root : path);
        }

        var names = rest.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        var current = names[^1];
        if (names.Length > 1)
        {
            return (names[^2] + separator, current);
        }

        return (root, current);
    }
}
