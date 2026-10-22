using System.Windows;

namespace GpuSwitcher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        if (e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
            window.WindowState = WindowState.Minimized;

        MainWindow = window;
        window.Show();
    }
}
