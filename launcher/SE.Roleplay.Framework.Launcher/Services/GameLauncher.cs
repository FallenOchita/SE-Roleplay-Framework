using System.Diagnostics;
using System.IO;

namespace SE.Roleplay.Framework.Launcher.Services;

public sealed class GameLauncher
{
    private const string SteamLaunchUri = "steam://run/244850";

    public void Launch(string? executablePath = null)
    {
        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            Process.Start(new ProcessStartInfo { FileName = executablePath, UseShellExecute = true });
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = SteamLaunchUri, UseShellExecute = true });
    }
}
