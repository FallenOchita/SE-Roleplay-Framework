namespace SE.Roleplay.Framework.Directory.Models;

public sealed class DirectoryServer
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline";
    public string Version { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public string DiscordUrl { get; set; } = string.Empty;
    public string RulesUrl { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string ApiKeyHash { get; set; } = string.Empty;
    public DateTime LastHeartbeatUtc { get; set; }
}

public sealed class DirectoryDocument
{
    public List<DirectoryServer> Servers { get; set; } = new();
}
