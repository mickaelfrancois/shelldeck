using ShellDeck.Git;
using ShellDeck.ViewModels;

namespace ShellDeck.TabLines;

/// <summary>What a line template can show about a tab.</summary>
/// <param name="Directory">What the main pane's shell last reported; null before it did.</param>
/// <param name="Git">Null outside a repository, in SSH, and while there is nothing to show.</param>
public sealed record TabLineContext(
    string Title,
    string ProfileName,
    string ProfileLocation,
    bool IsSsh,
    string? Directory,
    GitStatus? Git,
    TerminalStatus Status);
