using SE.Roleplay.Framework.Launcher.Models;

namespace SE.Roleplay.Framework.Launcher.Models;

public sealed class LauncherSettings
{
    public string ServerDirectoryUrl { get; set; } = string.Empty;
    public string GameExecutablePath { get; set; } = string.Empty;
    public string SteamId64 { get; set; } = string.Empty;
    public string ProtectedDirectoryRefreshToken { get; set; } = string.Empty;
    public List<ServerListing> RecentlyLaunchedServers { get; set; } = new();
    public List<string> FavoriteServerIds { get; set; } = new();
}
