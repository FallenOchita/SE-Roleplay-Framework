namespace SE.Roleplay.Framework.Directory.Models;

public sealed class AddServerMemberRequest
{
    public string SteamId64 { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

public sealed record ServerMember(
    string ServerId,
    string SteamId64,
    string AddedBySteamId64,
    DateTime AddedAtUtc,
    string Note);

public sealed record ServerMembershipStatus(string ServerId, string SteamId64, bool IsAllowed);
