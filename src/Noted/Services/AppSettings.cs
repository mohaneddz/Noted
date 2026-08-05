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

    public const double DefaultFontSize = 15.5;

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public string FontFamily { get; set; } = "Segoe UI Variable Text, Segoe UI";

    public string MonospaceFontFamily { get; set; } = "Cascadia Mono, Consolas, Courier New";

    public double FontSize { get; set; } = DefaultFontSize;

    /// <summary>Saves the active document automatically ~1.5s after you stop typing.</summary>
    public bool AutoSaveEnabled { get; set; } = true;

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

    /// <summary>Horizontal padding on either side of the reading column.</summary>
    public double MarginHorizontal { get; set; } = 48;

    /// <summary>Padding above the first line.</summary>
    public double MarginTop { get; set; } = 20;

    /// <summary>Padding below the last line.</summary>
    public double MarginBottom { get; set; } = 60;

    /// <summary>Multiplier for interface breathing room — title bar and status bar height, tab padding. 1.0 is normal.</summary>
    public double Spacing { get; set; } = 1.0;

    /// <summary>Draws an underline beneath heading text.</summary>
    public bool HeadingUnderline { get; set; }

    /// <summary>Overlays a subtle animated-free noise texture on the editor surface.</summary>
    public bool GrainEnabled { get; set; }

    /// <summary>Opacity of the grain overlay, when enabled.</summary>
    public double GrainOpacity { get; set; } = 0.05;

    /// <summary>Hex colour overrides for the active theme; a null entry falls back to the theme default.</summary>
    public ThemeColorOverrides Colors { get; set; } = new();

    /// <summary>Per-level (h1..h6) heading colour overrides; a null entry falls back to <see cref="ThemeColorOverrides.Heading"/>.</summary>
    public List<string?> HeadingColors { get; set; } = [null, null, null, null, null, null];

    [JsonIgnore]
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Noted");

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(DirectoryPath, "settings.json");

    /// <summary>Where autosaved drafts of never-saved documents are cached between sessions.</summary>
    [JsonIgnore]
    public static string DraftsDirectoryPath { get; } = Path.Combine(DirectoryPath, "Drafts");

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

/// <summary>Hex string overrides for the named theme tokens. Null means "use the active theme's default".</summary>
public sealed class ThemeColorOverrides
{
    public string? Background { get; set; }
    public string? Surface { get; set; }
    public string? Text { get; set; }
    public string? Muted { get; set; }
    public string? Accent { get; set; }
    public string? Heading { get; set; }
    public string? Link { get; set; }
    public string? Code { get; set; }
    public string? Quote { get; set; }
    public string? RuleLine { get; set; }
}
