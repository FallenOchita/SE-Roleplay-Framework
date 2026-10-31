namespace SE.Roleplay.Framework.Launcher.Models;
public sealed class ServerDirectoryDocument { public List<ServerListing> Servers { get; set; } = new(); }
public sealed class ServerListing
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public List<string> Tags { get; set; } = new();
}
