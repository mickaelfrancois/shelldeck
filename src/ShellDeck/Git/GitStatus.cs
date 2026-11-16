namespace ShellDeck.Git;

/// <summary>Git state of a working directory, as shown in a tab subtitle.</summary>
/// <param name="Branch">Branch name, or the 7-character short SHA when HEAD is detached.</param>
public sealed record GitStatus(string RepoName, string Branch, bool IsDetached, bool IsDirty, int Ahead, int Behind);
