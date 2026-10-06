using System.IO;
using System.Text.Json;

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
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.ProductName, "settings.json");

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerOptions.Web) { WriteIndented = true };

    /// <summary>Defaults when the file is missing or unreadable: preferences are not worth failing startup for.</summary>
    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), SerializerOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SerializerOptions));
    }
}
