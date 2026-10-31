using Microsoft.Win32;
namespace SE.Roleplay.Framework.Launcher.Services;
public sealed class GameInstallDetector
{
    private const string RelativeExecutable = "steamapps/common/SpaceEngineers/Bin64/SpaceEngineers.exe";
    public string? FindGameExecutable()
    {
        foreach (var keyPath in new[] {@"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"})
        {
            var steamPath = Registry.GetValue(keyPath, "SteamPath", null) as string ?? Registry.GetValue(keyPath, "InstallPath", null) as string;
            if (string.IsNullOrWhiteSpace(steamPath)) continue;
            var candidate = Path.Combine(steamPath, RelativeExecutable.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
