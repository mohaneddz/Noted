using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noted.Rendering;

namespace Noted.Services;

/// <summary>User preferences, persisted to <c>%APPDATA%\Noted\settings.json</c>.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public string FontFamily { get; set; } = "Segoe UI Variable Text, Segoe UI";

    public string MonospaceFontFamily { get; set; } = "Cascadia Mono, Consolas, Courier New";

    public double FontSize { get; set; } = 15.5;

    public bool WordWrap { get; set; } = true;

    public bool ShowLineNumbers { get; set; }

    /// <summary>When true, markdown syntax collapses on every line except the one being edited.</summary>
    public bool LiveMarkdown { get; set; } = true;

    /// <summary>Maximum text column width in pixels; wider windows just get bigger margins.</summary>
    public double ReadingWidth { get; set; } = 820;

    public double WindowWidth { get; set; } = 1120;

    public double WindowHeight { get; set; } = 760;

    public bool WindowMaximized { get; set; }

    public List<string> OpenFiles { get; set; } = [];

    [JsonIgnore]
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Noted");

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable settings file should never stop the editor from opening.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
