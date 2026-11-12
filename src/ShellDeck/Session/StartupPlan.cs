namespace ShellDeck.Session;

/// <summary>What the app opens at startup: a saved layout or the default terminal.</summary>
public static class StartupPlan
{
    /// <summary>
    /// The layout to restore, or null to open the default terminal. Nothing to restore never asks. <paramref name="askUser"/>
    /// runs only in <see cref="RestoreSession.Ask"/> mode.
    /// </summary>
    public static SessionLayout? Decide(RestoreSession mode, SessionLayout? saved, Func<bool> askUser)
    {
        if (saved is not { Tabs.Count: > 0 })
        {
            return null;
        }

        return mode switch
        {
            RestoreSession.Always => saved,
            RestoreSession.Ask => askUser() ? saved : null,
            _ => null,
        };
    }

    /// <summary>
    /// The save action wrapped so it does nothing while <paramref name="mode"/> is <see cref="RestoreSession.Never"/>, read at each
    /// save: a settings reload to <c>never</c> stops writing session.json at once. The other way round, a reload to <c>ask</c>
    /// or <c>always</c> starts writing from the next change (the saver is always wired); only the startup read happens once.
    /// </summary>
    public static Action SaveUnlessNever(Func<RestoreSession> mode, Action save) =>
        () =>
        {
            if (mode() != RestoreSession.Never)
            {
                save();
            }
        };
}
