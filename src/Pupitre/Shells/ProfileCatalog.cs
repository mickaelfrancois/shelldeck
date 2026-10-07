namespace Pupitre.Shells;

/// <summary>What the launcher shows: two sections of visible terminals, and the hidden ones apart.</summary>
public sealed record LauncherProfiles(
    IReadOnlyList<ShellProfile> Shells,
    IReadOnlyList<ShellProfile> SshConnections,
    IReadOnlyList<ShellProfile> Hidden)
{
    public IEnumerable<ShellProfile> Visible => Shells.Concat(SshConnections);
}

/// <summary>Applies the user's <see cref="ProfileSettings"/> to the terminals discovered on this machine.</summary>
public static class ProfileCatalog
{
    public static LauncherProfiles Arrange(
        IReadOnlyList<ShellProfile> discoveredShells,
        IReadOnlyList<ShellProfile> sshConnections,
        ProfileSettings settings)
    {
        var (shells, ssh) = Sections(discoveredShells, sshConnections, settings);
        var hidden = settings.Hidden.ToHashSet();
        return new LauncherProfiles(
            [.. shells.Where(profile => !hidden.Contains(profile.Id))],
            [.. ssh.Where(profile => !hidden.Contains(profile.Id))],
            [.. shells.Concat(ssh).Where(profile => hidden.Contains(profile.Id))]);
    }

    /// <summary>
    /// Swaps the terminal with its next visible neighbor in its own section (<paramref name="offset"/> is -1 or 1).
    /// Hidden terminals keep their place, so showing one again puts it back where it was.
    /// </summary>
    public static ProfileSettings Move(
        IReadOnlyList<ShellProfile> discoveredShells,
        IReadOnlyList<ShellProfile> sshConnections,
        ProfileSettings settings,
        string id,
        int offset)
    {
        var (shells, ssh) = Sections(discoveredShells, sshConnections, settings);
        var section = shells.Exists(profile => profile.Id == id) ? shells : ssh;
        var index = section.FindIndex(profile => profile.Id == id);
        if (index < 0)
        {
            return settings;
        }

        var neighbor = index + offset;
        while (neighbor >= 0 && neighbor < section.Count && settings.Hidden.Contains(section[neighbor].Id))
        {
            neighbor += offset;
        }

        if (neighbor < 0 || neighbor >= section.Count)
        {
            return settings;
        }

        (section[index], section[neighbor]) = (section[neighbor], section[index]);
        return settings with { Order = [.. shells.Concat(ssh).Select(profile => profile.Id)] };
    }

    /// <summary>Every terminal, hidden ones included, with the chosen starting directories and in the chosen order.</summary>
    private static (List<ShellProfile> Shells, List<ShellProfile> Ssh) Sections(
        IReadOnlyList<ShellProfile> discoveredShells,
        IReadOnlyList<ShellProfile> sshConnections,
        ProfileSettings settings)
    {
        var positions = settings.Order
            .Select((id, position) => (Id: id, Position: position))
            .DistinctBy(entry => entry.Id)
            .ToDictionary(entry => entry.Id, entry => entry.Position);

        // OrderBy is stable: terminals missing from the saved order stay in discovery order, after the others.
        List<ShellProfile> InOrder(IEnumerable<ShellProfile> profiles) =>
            [.. profiles.OrderBy(profile => positions.GetValueOrDefault(profile.Id, int.MaxValue))];

        var shells = discoveredShells
            .Select(profile => settings.StartingDirectories.TryGetValue(profile.Id, out var directory) && profile.HasStartingDirectory
                ? profile with { Location = directory }
                : profile)
            .Concat(settings.Custom.Select(custom => custom.ToProfile()));

        return (InOrder(shells), InOrder(sshConnections));
    }
}
