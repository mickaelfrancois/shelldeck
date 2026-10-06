using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pupitre;

public enum PanelSide
{
    Left,
    Right,
}

/// <summary>User preferences, stored as JSON under %LOCALAPPDATA%\Pupitre.</summary>
/// <param name="DefaultProfileName">Shell or SSH connection opened at startup; null means the first shell found.</param>
/// <param name="Window">Last size and position of the main window; null until the window has been closed once.</param>
public sealed record AppSettings(
    PanelSide PanelSide = PanelSide.Left,
    string? DefaultProfileName = null,
    WindowPlacement? Window = null)
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.ProductName, "settings.json");

    // "left"/"right" rather than 0/1: the file is meant to be edited by hand. Numbers are still read.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Defaults when the file is missing or unreadable: preferences are not worth failing startup for.</summary>
    public static AppSettings Load() => TryLoad(out var settings) ? settings : new AppSettings();

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

    public static AppSettings Parse(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();

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
