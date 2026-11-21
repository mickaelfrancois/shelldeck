using System.Text.Json;
using ShellDeck.Shells;
using ShellDeck.ViewModels;

namespace ShellDeck.TabLines;

/// <summary>
/// What the tab command receives on its standard input, one JSON object. The directory, the title and the Claude session
/// come from the terminal: a script must treat them as untrusted text, never run them. They are never on the command line.
/// </summary>
public static class TabCommandInput
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Build(TerminalTab tab)
    {
        var git = tab.ShownGit;
        return JsonSerializer.Serialize(
            new
            {
                version = 1,
                title = tab.Title,
                status = tab.Status.ToString().ToLowerInvariant(),
                profile = new { id = tab.Profile.Id, name = tab.Profile.Name, kind = tab.Profile.Kind.ToString().ToLowerInvariant(), location = tab.Profile.Location },
                directory = tab.Profile.Kind is ShellKind.Ssh ? null : tab.MainPane.WorkingDirectory,
                claudeSessionId = tab.MainPane.ClaudeSessionId,
                git = git is null ? null : new { repo = git.RepoName, branch = git.Branch, detached = git.IsDetached, dirty = git.IsDirty, ahead = git.Ahead, behind = git.Behind },
            },
            Options);
    }
}
