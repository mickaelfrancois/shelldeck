using System.IO;

namespace ShellDeck.Git;

/// <summary>Finds the root of a git repository by walking up the folders.</summary>
public static class GitRepository
{
    /// <summary>Whether a folder holds a <c>.git</c> entry: a folder, or a file for worktrees and submodules. Touches the disk.</summary>
    public static bool HasGitEntryOnDisk(string folder)
    {
        string path = Path.Combine(folder, ".git");
        return Directory.Exists(path) || File.Exists(path);
    }

    /// <summary>The nearest folder, from <paramref name="directory"/> up to the drive root, that holds a <c>.git</c> entry; <c>null</c> when none does.</summary>
    /// <param name="hasGitEntry">Tells whether a folder holds a <c>.git</c> entry (<see cref="HasGitEntryOnDisk"/> in production).</param>
    public static string? FindRoot(string directory, Func<string, bool> hasGitEntry)
    {
        string? current = directory;
        while (!string.IsNullOrEmpty(current))
        {
            if (hasGitEntry(current))
                return current;

            current = Path.GetDirectoryName(current);
        }

        return null;
    }
}
