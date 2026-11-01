using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SE.Roleplay.Framework.Directory.Services;

/// <summary>
/// Validates launcher bearer credentials against the opaque token store.
/// The caller's Steam identity is never accepted from request data.
/// </summary>
public sealed class DesktopBearerAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "LauncherBearer";
    private readonly DesktopTokenStore _tokens;

    public DesktopBearerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        DesktopTokenStore tokens)
        : base(options, logger, encoder) => _tokens = tokens;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values) ||
            values.Count != 1 ||
            !AuthenticationHeaderValue.TryParse(values[0], out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = _tokens.ValidateAccessToken(
            header.Parameter, DesktopTokenStore.RegisteredClientId);
        if (identity is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token."));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, identity.SteamId64),
            new Claim(ClaimTypes.Name, identity.SteamId64),
            new Claim("client_id", identity.ClientId)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
