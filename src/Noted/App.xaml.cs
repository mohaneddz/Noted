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
        var theme = EditorTheme.For(mode);
        Settings.Theme = mode;

        Set("Brush.Background", theme.Background);
        Set("Brush.Surface", theme.Surface);
        Set("Brush.SurfaceAlt", theme.SurfaceAlt);
        Set("Brush.Border", theme.Border);
        Set("Brush.Text", theme.Text);
        Set("Brush.Muted", theme.Muted);
        Set("Brush.Faint", theme.Faint);
        Set("Brush.Accent", theme.Accent);

        void Set(string key, Brush brush) => Resources[key] = brush;
    }
}
