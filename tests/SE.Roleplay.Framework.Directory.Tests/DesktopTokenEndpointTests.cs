using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SE.Roleplay.Framework.Directory.Services;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class DesktopTokenEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Client = DesktopTokenStore.RegisteredClientId;
    private const string Redirect = "http://127.0.0.1:49152/oauth/callback/test";
    private const string Browser = "browser-session-id-with-more-than-32-characters";
    private const string SteamId = "76561198012345678";
    private readonly WebApplicationFactory<Program> _factory;

    public DesktopTokenEndpointTests(WebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task AuthorizationCodeGrant_ReturnsNoStoreTokenPair()
    {
        using var client = _factory.CreateClient();
        var (code, verifier) = CreateAuthorizationCode();
        using var response = await client.PostAsJsonAsync("/oauth/token", new DesktopTokenRequest(
            "authorization_code", Client, code, Redirect, verifier, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-cache", response.Headers.Pragma.ToString());
        var pair = await response.Content.ReadFromJsonAsync<DesktopTokenPair>();
        Assert.NotNull(pair);
        Assert.False(string.IsNullOrWhiteSpace(pair.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(pair.RefreshToken));
        Assert.Equal("Bearer", pair.TokenType);
    }

    [Fact]
    public async Task AuthorizationCodeGrant_CannotBeRedeemedTwice()
    {
        using var client = _factory.CreateClient();
        var (code, verifier) = CreateAuthorizationCode();
        var request = new DesktopTokenRequest("authorization_code", Client, code, Redirect, verifier, null);

        using var first = await client.PostAsJsonAsync("/oauth/token", request);
        using var replay = await client.PostAsJsonAsync("/oauth/token", request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task RefreshGrant_RotatesTokenAndRejectsReplay()
    {
        using var client = _factory.CreateClient();
        var (code, verifier) = CreateAuthorizationCode();
        using var initialResponse = await client.PostAsJsonAsync("/oauth/token",
            new DesktopTokenRequest("authorization_code", Client, code, Redirect, verifier, null));
        var initial = await initialResponse.Content.ReadFromJsonAsync<DesktopTokenPair>();
        Assert.NotNull(initial);

        using var rotatedResponse = await client.PostAsJsonAsync("/oauth/token",
            new DesktopTokenRequest("refresh_token", Client, null, null, null, initial.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, rotatedResponse.StatusCode);
        var rotated = await rotatedResponse.Content.ReadFromJsonAsync<DesktopTokenPair>();
        Assert.NotNull(rotated);
        Assert.NotEqual(initial.RefreshToken, rotated.RefreshToken);

        using var replayResponse = await client.PostAsJsonAsync("/oauth/token",
            new DesktopTokenRequest("refresh_token", Client, null, null, null, initial.RefreshToken));
        Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);
    }

    [Fact]
    public async Task TokenEndpoint_RejectsUnknownClientAndMalformedGrant()
    {
        using var client = _factory.CreateClient();

        using var unknownClient = await client.PostAsJsonAsync("/oauth/token",
            new DesktopTokenRequest("refresh_token", "unknown-client", null, null, null, "some-token"));
        using var malformed = await client.PostAsJsonAsync("/oauth/token",
            new DesktopTokenRequest("unsupported", Client, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, unknownClient.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    private (string Code, string Verifier) CreateAuthorizationCode()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<DesktopAuthorizationCodeStore>();
        var verifier = "desktop-launcher-pkce-verifier-string-0123456789";
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var state = store.CreateState(Client, Redirect, challenge, Browser);
        var code = store.CompleteState(state, Browser, SteamId);
        Assert.False(string.IsNullOrWhiteSpace(code));
        return (code!, verifier);
    }
}
