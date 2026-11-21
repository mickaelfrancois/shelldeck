using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShellDeck.Shells;
using ShellDeck.ViewModels;

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

    /// <summary>
    /// The three lines of a tab, each a text with <c>{fields}</c> such as <c>{title}</c>, <c>{parent}{folder}</c>, <c>{git}</c>.
    /// Missing lines are empty, extra ones ignored; null (not written) means the default lines.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? TabLines { get; init; }

    /// <summary>
    /// A program and its arguments, run for each tab (never through a shell) with the tab's data as JSON on its standard
    /// input. It prints a JSON object whose keys become fields for <see cref="TabLines"/> (<c>{model}</c>). Null: none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? TabCommand { get; init; }

    /// <summary>Layout of the session opened for the default profile at startup and by the new-tab shortcut.</summary>
    public PaneTemplate DefaultTemplate { get; init; } = PaneTemplate.Single;

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
    public static AppSettings Load() => Load(out _);

    /// <summary>
    /// <paramref name="error"/> says why the defaults apply (null when the file is fine or missing).
    /// </summary>
    public static AppSettings Load(out string? error)
    {
        MoveFromPreviousLocation(PreviousFilePath, FilePath);
        return TryLoad(out var settings, out error) ? settings : new AppSettings();
    }

    private static void BackUpInvalidFile()
    {
        try
        {
            File.Copy(FilePath, FilePath + ".invalid", overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The file stays where it is; only the safety copy is missing.
        }
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
    public static bool TryLoad(out AppSettings settings) => TryLoad(out settings, out _);

    /// <summary>
    /// <paramref name="error"/> is a sentence for the user (line and column of the mistake), null when it worked. A file that
    /// does not parse is copied to <c>settings.json.invalid</c>: the app saves its settings when the user changes one
    /// in the window, which would otherwise overwrite what they were typing.
    /// </summary>
    public static bool TryLoad(out AppSettings settings, out string? error)
    {
        settings = new AppSettings();
        error = null;
        string json;
        try
        {
            if (!File.Exists(FilePath))
            {
                return true;
            }

            json = File.ReadAllText(FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"settings.json ne peut pas être lu : {exception.Message}";
            return false;
        }

        if (TryParse(json, out settings, out error))
        {
            return true;
        }

        BackUpInvalidFile();
        return false;
    }

    /// <summary>Like <see cref="Parse"/>, without the exception: <paramref name="error"/> says where the file is wrong.</summary>
    public static bool TryParse(string json, out AppSettings settings, out string? error)
    {
        try
        {
            settings = Parse(json);
            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            settings = new AppSettings();
            error = Describe(exception);
            return false;
        }
    }

    private static string Describe(JsonException exception)
    {
        List<string> where = [];
        if (exception.LineNumber is { } line)
        {
            where.Add($"ligne {line + 1}, colonne {(exception.BytePositionInLine ?? 0) + 1}");
        }

        if (exception.Path is { Length: > 1 } path)
        {
            where.Add($"clé {path.TrimStart('$', '.')}");
        }

        var reason = exception.Message.Split(" Path:")[0].Trim();
        return where.Count == 0 ? $"settings.json est invalide : {reason}" : $"settings.json est invalide ({string.Join(", ", where)}) : {reason}";
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
