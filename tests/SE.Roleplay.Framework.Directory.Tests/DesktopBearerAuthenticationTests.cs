using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SE.Roleplay.Framework.Directory.Services;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class DesktopBearerAuthenticationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SteamId = "76561198012345678";
    private readonly WebApplicationFactory<Program> _factory;

    public DesktopBearerAuthenticationTests(WebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task LauncherMe_ReturnsIdentityFromValidBearerToken()
    {
        var pair = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", pair.AccessToken);

        using var response = await client.GetAsync("/api/launcher/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(SteamId, body, StringComparison.Ordinal);
        Assert.Contains(DesktopTokenStore.RegisteredClientId, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LauncherMe_RejectsMissingOrInvalidBearerToken()
    {
        using var client = _factory.CreateClient();
        using var missing = await client.GetAsync("/api/launcher/me");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-valid-token");
        using var invalid = await client.GetAsync("/api/launcher/me");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    private DesktopTokenPair IssueToken()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<DesktopTokenStore>();
        return Assert.IsType<DesktopTokenPair>(
            store.Issue(SteamId, DesktopTokenStore.RegisteredClientId));
    }
}
