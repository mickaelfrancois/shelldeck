using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShellDeck.Session;

/// <summary>Reads and writes session.json, next to settings.json. Best effort: a session is never worth failing for.</summary>
public sealed class SessionStore(string filePath)
{
    public static readonly string DefaultPath = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "session.json");

    // Same options as AppSettings: camelCase enums, comments and trailing commas tolerated for hand edits.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public SessionStore() : this(DefaultPath)
    {
    }

    public string FilePath { get; } = filePath;

    /// <summary>Null when the file is missing, unreadable, corrupt or written by an unknown version.</summary>
    public SessionLayout? Load()
    {
        try
        {
            return File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SessionLayout? Parse(string json)
    {
        try
        {
            var layout = JsonSerializer.Deserialize<SessionLayout>(json, SerializerOptions);
            return layout is { Version: SessionLayout.CurrentVersion, Tabs: not null }
                && layout.Tabs.All(tab => tab?.Root is { IsWellFormed: true })
                ? layout
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string ToJson(SessionLayout layout) => JsonSerializer.Serialize(layout, SerializerOptions);

    /// <summary>Written aside then swapped in, so an interrupted write never leaves a truncated file.</summary>
    public void Save(SessionLayout layout)
    {
        var temporaryPath = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(temporaryPath, ToJson(layout));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The layout is kept in memory; the next change tries again.
        }
    }
}
