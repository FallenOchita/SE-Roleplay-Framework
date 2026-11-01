namespace SE.Roleplay.Framework.Directory.Services;

public sealed record DesktopTokenRequest(
    string? GrantType,
    string? ClientId,
    string? Code,
    string? RedirectUri,
    string? CodeVerifier,
    string? RefreshToken);
