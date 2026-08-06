using System.Windows;
using System.Windows.Media;
using Noted.Rendering;
using Noted.Services;

namespace Noted;

public partial class App : Application
{
    public AppSettings Settings { get; private set; } = new();

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = AppSettings.Load();
        ApplyTheme(Settings.Theme);

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            MessageBox.Show(
                args.Exception.Message,
                "Noted ran into a problem",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        MainWindow = new MainWindow(e.Args);
        MainWindow.Show();
    }

    /// <summary>Repoints the shared brush resources so the whole shell restyles in place.</summary>
    public void ApplyTheme(AppTheme mode)
    {
        var theme = EditorTheme.Resolve(mode, Settings);
        Settings.Theme = mode;

        Set("Brush.Background", theme.Background);
        Set("Brush.Margin", theme.Margin);
        Set("Brush.Surface", theme.Surface);
        Set("Brush.SurfaceAlt", theme.SurfaceAlt);
        Set("Brush.Border", theme.Border);
        Set("Brush.Text", theme.Text);
        Set("Brush.Muted", theme.Muted);
        Set("Brush.Faint", theme.Faint);
        Set("Brush.Accent", theme.Accent);

        void Set(string key, Brush brush) => Resources[key] = brush;
    }

    /// <summary>Appends a crash to <c>%APPDATA%\Noted\crash.log</c> so failures can be diagnosed after the fact.</summary>
    private static void LogCrash(Exception exception)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppSettings.DirectoryPath);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppSettings.DirectoryPath, "crash.log"),
                $"{DateTimeOffset.Now:u}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
        }
    }
}
