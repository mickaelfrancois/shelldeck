namespace Pupitre.Shells;

/// <summary>A terminal the user added to the launcher.</summary>
/// <param name="StartingDirectory">Windows directory; empty means the user's folder.</param>
public sealed record CustomProfile(string Id, string Name, string CommandLine, string StartingDirectory = "")
{
    public ShellProfile ToProfile() => new(Name, CommandLine, ShellKind.Custom, StartingDirectory) { Id = Id };
}

/// <summary>
/// How the user arranged the launcher, kept as differences from what discovery finds: a WSL distribution installed
/// tomorrow shows up without touching the file, and a discovered terminal is hidden rather than deleted, since the
/// next discovery would bring it back. Profiles are referred to by <see cref="ShellProfile.Id"/>.
/// </summary>
public sealed record ProfileSettings
{
    public IReadOnlyList<CustomProfile> Custom { get; init; } = [];

    /// <summary>Starting directory chosen for a discovered terminal, by id.</summary>
    public IReadOnlyDictionary<string, string> StartingDirectories { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> Hidden { get; init; } = [];

    /// <summary>Launcher order; terminals missing from it come last, in the order they were discovered.</summary>
    public IReadOnlyList<string> Order { get; init; } = [];

    /// <summary>
    /// A file edited by hand can say <c>null</c> where a list is expected, or leave a field of an added terminal out:
    /// read it as empty rather than fail later, and drop a terminal that has no id, name or command line.
    /// </summary>
    public static ProfileSettings Repair(ProfileSettings? profiles) =>
        profiles is null
            ? new ProfileSettings()
            : new ProfileSettings
            {
                Custom = [.. (profiles.Custom ?? [])
                    .Where(custom => custom is { Id: not null, Name: not null, CommandLine: not null })
                    .Select(custom => custom.StartingDirectory is null ? custom with { StartingDirectory = string.Empty } : custom)],
                StartingDirectories = (profiles.StartingDirectories ?? new Dictionary<string, string>())
                    .Where(pair => pair.Value is not null)
                    .ToDictionary(),
                Hidden = [.. (profiles.Hidden ?? []).OfType<string>()],
                Order = [.. (profiles.Order ?? []).OfType<string>()],
            };

    public ProfileSettings Hide(string id) =>
        Hidden.Contains(id) ? this : this with { Hidden = [.. Hidden, id] };

    public ProfileSettings Show(string id) =>
        this with { Hidden = [.. Hidden.Where(hidden => hidden != id)] };

    /// <summary>An empty directory removes the choice: the terminal starts where discovery says again.</summary>
    public ProfileSettings WithStartingDirectory(string id, string directory)
    {
        var directories = StartingDirectories.Where(pair => pair.Key != id).ToDictionary();
        if (!string.IsNullOrWhiteSpace(directory))
        {
            directories[id] = directory.Trim();
        }

        return this with { StartingDirectories = directories };
    }

    /// <summary>Adds the terminal, or replaces the one with the same id in place.</summary>
    public ProfileSettings SaveCustom(CustomProfile profile) =>
        this with
        {
            Custom = Custom.Any(custom => custom.Id == profile.Id)
                ? [.. Custom.Select(custom => custom.Id == profile.Id ? profile : custom)]
                : [.. Custom, profile],
        };

    /// <summary>Forgets everything about the terminal, so no stale id is left behind.</summary>
    public ProfileSettings Remove(string id) =>
        this with
        {
            Custom = [.. Custom.Where(custom => custom.Id != id)],
            StartingDirectories = StartingDirectories.Where(pair => pair.Key != id).ToDictionary(),
            Hidden = [.. Hidden.Where(hidden => hidden != id)],
            Order = [.. Order.Where(ordered => ordered != id)],
        };

    // Compared by content: reloading the file after our own save must not count as a change.
    public bool Equals(ProfileSettings? other) =>
        other is not null
        && Custom.SequenceEqual(other.Custom)
        && Hidden.SequenceEqual(other.Hidden)
        && Order.SequenceEqual(other.Order)
        && StartingDirectories.Count == other.StartingDirectories.Count
        && StartingDirectories.All(pair => other.StartingDirectories.TryGetValue(pair.Key, out var directory) && directory == pair.Value);

    public override int GetHashCode() => HashCode.Combine(Custom.Count, StartingDirectories.Count, Hidden.Count, Order.Count);
}
