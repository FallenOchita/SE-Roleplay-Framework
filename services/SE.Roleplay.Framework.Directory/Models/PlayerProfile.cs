namespace SE.Roleplay.Framework.Directory.Models;

public sealed record PlayerProfile(
    string SteamId64,
    string DisplayName,
    string Biography,
    DateTime UpdatedAtUtc);

public sealed class UpdatePlayerProfileRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string Biography { get; set; } = string.Empty;
}

public sealed class GrantPlayerPermissionRequest
{
    public string SteamId64 { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; set; }
}

public sealed record PlayerPermission(
    string ServerId,
    string SteamId64,
    string Permission,
    string GrantedBySteamId64,
    DateTime GrantedAtUtc,
    DateTime? ExpiresAtUtc);
