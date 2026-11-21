using System.Reflection;

namespace ShellDeck;

/// <summary>The version the release was built with (<c>-p:Version</c>), shown in the footer.</summary>
public static class AppVersion
{
    public static string Current { get; } = Format(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>"1.4.1+3f2a9c" (the SDK appends the commit) becomes "v1.4.1"; empty when unknown.</summary>
    internal static string Format(string? informationalVersion)
    {
        var version = informationalVersion?.Split('+')[0].Trim();
        return string.IsNullOrEmpty(version) ? string.Empty : $"v{version}";
    }
}
