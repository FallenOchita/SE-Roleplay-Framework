namespace SE.Roleplay.Framework.Directory.Models;

public sealed class RegisterServerRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public int MaxPlayers { get; set; }
    public string Region { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public string DiscordUrl { get; set; } = string.Empty;
    public string RulesUrl { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}

public sealed class HeartbeatRequest
{
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public string Version { get; set; } = string.Empty;
}
