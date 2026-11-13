using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShellDeck.Shells;

namespace ShellDeck;

public enum PanelSide
{
    Left,
    Right,
}

/// <summary>Whether the tabs and panes of the last session are reopened at startup.</summary>
public enum RestoreSession
{
    Never,
    Ask,
    Always,
}

/// <summary>User preferences, stored as JSON in %USERPROFILE%\.shelldeck.</summary>
/// <param name="DefaultProfileName">Written by earlier versions: the name of the terminal opened at startup. Read until <see cref="DefaultProfileId"/> replaces it.</param>
/// <param name="Window">Last size and position of the main window; null until the window has been closed once.</param>
public sealed record AppSettings(
    PanelSide PanelSide = PanelSide.Left,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DefaultProfileName = null,
    WindowPlacement? Window = null)
{
    /// <summary>Terminal opened at startup, by <see cref="ShellProfile.Id"/>; null means the first shell found.</summary>
    public string? DefaultProfileId { get; init; }

    /// <summary>Reopen the tabs and panes of the last session at startup: "never", "ask" or "always".</summary>
    public RestoreSession RestoreSession { get; init; } = RestoreSession.Always;

    /// <summary>Terminals added, hidden, reordered or given a starting directory in the launcher.</summary>
    public ProfileSettings Profiles { get; init; } = new();

    /// <summary>
    /// Next to ~/.ssh rather than under %LOCALAPPDATA%: once packaged (MSIX), what the app writes in AppData is
    /// redirected to a private folder, and the editor opened on this file from the settings button would not see it.
    /// </summary>
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".shelldeck", "settings.json");

    /// <summary>Where version 0.1, then named Pupitre, kept the file.</summary>
    private static readonly string PreviousFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pupitre", "settings.json");

    // "left"/"right" rather than 0/1: the file is meant to be edited by hand. Numbers are still read.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Defaults when the file is missing or unreadable: preferences are not worth failing startup for.</summary>
    public static AppSettings Load()
    {
        MoveFromPreviousLocation(PreviousFilePath, FilePath);
        return TryLoad(out var settings) ? settings : new AppSettings();
    }

    /// <summary>
    /// Copies the file of an earlier version to its new place, once. Copied rather than moved: Pupitre 0.1
    /// still installed beside this one keeps its settings.
    /// </summary>
    public static void MoveFromPreviousLocation(string previousPath, string currentPath)
    {
        try
        {
            if (File.Exists(previousPath) && !File.Exists(currentPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
                File.Copy(previousPath, currentPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The defaults apply; the previous file is still there for the next start.
        }
    }

    /// <summary>False when the file exists but cannot be read or parsed, e.g. while being edited.</summary>
    public static bool TryLoad(out AppSettings settings)
    {
        settings = new AppSettings();
        try
        {
            if (File.Exists(FilePath))
            {
                settings = Parse(File.ReadAllText(FilePath));
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static AppSettings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        return settings with { Profiles = ProfileSettings.Repair(settings.Profiles) };
    }

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>
    /// Best effort, like <see cref="Load"/>: a locked file (antivirus, OneDrive) must not crash the app.
    /// Written aside then swapped in, so an interrupted write never leaves a truncated file.
    /// </summary>
    public void Save()
    {
        var temporaryPath = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(temporaryPath, ToJson());
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The preference is kept in memory for this session.
        }
    }

    /// <summary>Calls <paramref name="onChanged"/> (on a background thread) whenever the file is written, by anyone.</summary>
    public static IDisposable Watch(Action onChanged)
    {
        var folder = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(folder);
        var watcher = new FileSystemWatcher(folder, Path.GetFileName(FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        watcher.Changed += (_, _) => onChanged();
        watcher.Created += (_, _) => onChanged();
        watcher.Renamed += (_, _) => onChanged(); // Save() swaps the file in by renaming it.
        watcher.EnableRaisingEvents = true;
        return watcher;
    }
}
