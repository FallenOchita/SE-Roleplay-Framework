using System.Windows;
using SE.Roleplay.Framework.Launcher.Services;

namespace SE.Roleplay.Framework.Launcher;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Keep the launcher window hidden until the update check has completed.
        // If an update is installed, the updater shuts the process down and starts setup.
        var launcherWindow = new MainWindow();
        var updater = new LauncherUpdateService();
        if (await updater.CheckAndStartUpdateAsync(launcherWindow))
            return;

        MainWindow = launcherWindow;
        launcherWindow.Show();
    }
}
