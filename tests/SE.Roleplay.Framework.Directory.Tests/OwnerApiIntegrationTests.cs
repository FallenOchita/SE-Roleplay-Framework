using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using SE.Roleplay.Framework.Directory.Services;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class OwnerApiIntegrationTests
{
    private const string OwnerA = "76561198012345678";
    private const string OwnerB = "76561198087654321";
    private const string Origin = "https://directory.test";

    [Fact]
    public async Task LauncherBearerOwnerApi_EnforcesOwnershipAcrossCrudAndKeyRotation()
    {
        using var factory = new DirectoryApiFactory();
        using var ownerA = factory.CreateBearerOwnerClient(OwnerA);
        using var ownerB = factory.CreateBearerOwnerClient(OwnerB);

        using var createResponse = await ownerA.PostAsJsonAsync(
            "/api/launcher/owner/servers", Registration("Bearer-owned server"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;
        var firstKey = created.RootElement.GetProperty("apiKey").GetString()!;

        var ownerBList = await ownerB.GetAsync("/api/launcher/owner/servers");
        ownerBList.EnsureSuccessStatusCode();
        using var ownerBJson = JsonDocument.Parse(await ownerBList.Content.ReadAsStringAsync());
        Assert.Empty(ownerBJson.RootElement.GetProperty("servers").EnumerateArray());

        using var rejectedUpdate = await ownerB.PutAsJsonAsync(
            $"/api/launcher/owner/servers/{id}", Registration("Unauthorized update"));
        using var rejectedDelete = await ownerB.DeleteAsync($"/api/launcher/owner/servers/{id}");
        using var rejectedRotate = await ownerB.PostAsync($"/api/launcher/owner/servers/{id}/rotate-key", null);
        Assert.Equal(HttpStatusCode.NotFound, rejectedUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rejectedDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rejectedRotate.StatusCode);

        using var update = await ownerA.PutAsJsonAsync(
            $"/api/launcher/owner/servers/{id}", Registration("Updated by launcher"));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        using var rotate = await ownerA.PostAsync($"/api/launcher/owner/servers/{id}/rotate-key", null);
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        using var rotated = JsonDocument.Parse(await rotate.Content.ReadAsStringAsync());
        var secondKey = rotated.RootElement.GetProperty("apiKey").GetString()!;
        Assert.NotEqual(firstKey, secondKey);

        using var list = await ownerA.GetAsync("/api/launcher/owner/servers");
        list.EnsureSuccessStatusCode();
        var listText = await list.Content.ReadAsStringAsync();
        Assert.Contains("Updated by launcher", listText, StringComparison.Ordinal);
        Assert.DoesNotContain(firstKey, listText, StringComparison.Ordinal);
        Assert.DoesNotContain(secondKey, listText, StringComparison.Ordinal);

        using var delete = await ownerA.DeleteAsync($"/api/launcher/owner/servers/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task LauncherBearerOwnerApi_ManagesAllowlistOnlyForOwningSteamAccount()
    {
        using var factory = new DirectoryApiFactory();
        using var ownerA = factory.CreateBearerOwnerClient(OwnerA);
        using var ownerB = factory.CreateBearerOwnerClient(OwnerB);

        using var createResponse = await ownerA.PostAsJsonAsync(
            "/api/launcher/owner/servers", Registration("Allowlist-owned server"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var serverId = created.RootElement.GetProperty("id").GetString()!;

        using var initialSetting = await ownerA.GetAsync(
            $"/api/launcher/owner/servers/{serverId}/admission");
        Assert.Equal(HttpStatusCode.OK, initialSetting.StatusCode);
        using var initialJson = JsonDocument.Parse(await initialSetting.Content.ReadAsStringAsync());
        Assert.False(initialJson.RootElement.GetProperty("allowlistEnabled").GetBoolean());

        using var rejectedSetting = await ownerB.GetAsync(
            $"/api/launcher/owner/servers/{serverId}/admission");
        Assert.Equal(HttpStatusCode.NotFound, rejectedSetting.StatusCode);

        using var rejectedAdd = await ownerB.PostAsJsonAsync(
            $"/api/launcher/owner/servers/{serverId}/members",
            new { steamId64 = "76561198011111111", note = "unauthorized" });
        Assert.Equal(HttpStatusCode.NotFound, rejectedAdd.StatusCode);

        using var add = await ownerA.PostAsJsonAsync(
            $"/api/launcher/owner/servers/{serverId}/members",
            new { steamId64 = "76561198022222222", note = "community member" });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        using var membersResponse = await ownerA.GetAsync(
            $"/api/launcher/owner/servers/{serverId}/members");
        Assert.Equal(HttpStatusCode.OK, membersResponse.StatusCode);
        using var membersJson = JsonDocument.Parse(await membersResponse.Content.ReadAsStringAsync());
        var member = Assert.Single(membersJson.RootElement.GetProperty("members").EnumerateArray());
        Assert.Equal("76561198022222222", member.GetProperty("steamId64").GetString());
        Assert.Equal("community member", member.GetProperty("note").GetString());

        using var enable = await ownerA.PutAsJsonAsync(
            $"/api/launcher/owner/servers/{serverId}/admission",
            new { allowlistEnabled = true });
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);

        using var enabledSetting = await ownerA.GetAsync(
            $"/api/launcher/owner/servers/{serverId}/admission");
        using var enabledJson = JsonDocument.Parse(await enabledSetting.Content.ReadAsStringAsync());
        Assert.True(enabledJson.RootElement.GetProperty("allowlistEnabled").GetBoolean());

        using var rejectedRemove = await ownerB.DeleteAsync(
            $"/api/launcher/owner/servers/{serverId}/members/76561198022222222");
        Assert.Equal(HttpStatusCode.NotFound, rejectedRemove.StatusCode);

        using var remove = await ownerA.DeleteAsync(
            $"/api/launcher/owner/servers/{serverId}/members/76561198022222222");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
    }

    [Fact]
    public async Task LauncherBearerOwnerApi_RejectsMissingBearerToken()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/launcher/owner/servers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OwnerApi_RequiresAuthentication()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/owner/servers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OwnerCannotReadOrMutateAnotherOwnersListing()
    {
        using var factory = new DirectoryApiFactory();
        using var ownerA = factory.CreateOwnerClient(OwnerA);
        using var ownerB = factory.CreateOwnerClient(OwnerB);

        var created = await RegisterAsync(ownerA);
        var id = created.GetProperty("id").GetString()!;

        var ownerBList = await ownerB.GetAsync("/api/owner/servers");
        ownerBList.EnsureSuccessStatusCode();
        using var ownerBJson = JsonDocument.Parse(await ownerBList.Content.ReadAsStringAsync());
        Assert.Empty(ownerBJson.RootElement.GetProperty("servers").EnumerateArray());

        var update = await SendProtectedAsync(ownerB, HttpMethod.Put, $"/api/owner/servers/{id}", Registration("Hijacked"));
        var delete = await SendProtectedAsync(ownerB, HttpMethod.Delete, $"/api/owner/servers/{id}");
        var rotate = await SendProtectedAsync(ownerB, HttpMethod.Post, $"/api/owner/servers/{id}/rotate-key");

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rotate.StatusCode);

        var ownerAList = await ownerA.GetAsync("/api/owner/servers");
        ownerAList.EnsureSuccessStatusCode();
        using var ownerAJson = JsonDocument.Parse(await ownerAList.Content.ReadAsStringAsync());
        var onlyServer = Assert.Single(ownerAJson.RootElement.GetProperty("servers").EnumerateArray());
        Assert.Equal("Owner A server", onlyServer.GetProperty("name").GetString());
        Assert.DoesNotContain(OwnerA, await ownerAList.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnerWrites_RejectMissingOrMismatchedCsrfProof()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateOwnerClient(OwnerA);
        var token = await GetCsrfTokenAsync(client);

        using var mismatchedOrigin = new HttpRequestMessage(HttpMethod.Post, "/api/owner/servers")
        {
            Content = JsonContent.Create(Registration("Rejected origin"))
        };
        mismatchedOrigin.Headers.Add("Origin", "https://attacker.test");
        mismatchedOrigin.Headers.Add("X-CSRF-TOKEN", token);
        var originResponse = await client.SendAsync(mismatchedOrigin);

        using var missingToken = new HttpRequestMessage(HttpMethod.Post, "/api/owner/servers")
        {
            Content = JsonContent.Create(Registration("Missing token"))
        };
        missingToken.Headers.Add("Origin", Origin);
        var tokenResponse = await client.SendAsync(missingToken);

        Assert.Equal(HttpStatusCode.BadRequest, originResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tokenResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_RequiresCsrfProofAndClearsAuthenticationCookies()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateOwnerClient(OwnerA);

        using var missingToken = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        missingToken.Headers.Add("Origin", Origin);
        var rejected = await client.SendAsync(missingToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var logout = await SendProtectedAsync(client, HttpMethod.Post, "/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var cookies = string.Join(";", logout.Headers.TryGetValues("Set-Cookie", out var values)
            ? values
            : Array.Empty<string>());
        Assert.Contains("__Host-serpf-owner", cookies, StringComparison.Ordinal);
        Assert.Contains("__Host-serpf-browser", cookies, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registration_ReturnsApiKeyOnceAndDoesNotExposeOwnerIdentity()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateOwnerClient(OwnerA);

        var created = await RegisterAsync(client);
        Assert.True(created.TryGetProperty("apiKey", out var key));
        Assert.False(string.IsNullOrWhiteSpace(key.GetString()));
        var responseText = created.GetRawText();
        Assert.DoesNotContain(OwnerA, responseText, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKeyHash", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerSteamId64", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OwnerCanUpdateOwnListing()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateOwnerClient(OwnerA);
        var created = await RegisterAsync(client);
        var id = created.GetProperty("id").GetString()!;

        var update = await SendProtectedAsync(client, HttpMethod.Put, $"/api/owner/servers/{id}", Registration("Updated server"));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var list = await client.GetAsync("/api/owner/servers");
        list.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var server = Assert.Single(document.RootElement.GetProperty("servers").EnumerateArray());
        Assert.Equal("Updated server", server.GetProperty("name").GetString());
    }

    [Fact]
    public async Task OwnerCanRotateOwnKeyAndOldKeyStopsWorking()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateOwnerClient(OwnerA);
        var created = await RegisterAsync(client);
        var id = created.GetProperty("id").GetString()!;
        var oldKey = created.GetProperty("apiKey").GetString()!;

        var oldHeartbeat = await SendHeartbeatAsync(client, id, oldKey);
        Assert.Equal(HttpStatusCode.NoContent, oldHeartbeat.StatusCode);

        var rotation = await SendProtectedAsync(client, HttpMethod.Post, $"/api/owner/servers/{id}/rotate-key");
        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);
        using var rotationJson = JsonDocument.Parse(await rotation.Content.ReadAsStringAsync());
        var newKey = rotationJson.RootElement.GetProperty("apiKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newKey));
        Assert.NotEqual(oldKey, newKey);

        var rejectedOldHeartbeat = await SendHeartbeatAsync(client, id, oldKey);
        var acceptedNewHeartbeat = await SendHeartbeatAsync(client, id, newKey!);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedOldHeartbeat.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, acceptedNewHeartbeat.StatusCode);

        var list = await client.GetAsync("/api/owner/servers");
        list.EnsureSuccessStatusCode();
        var listText = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain(oldKey, listText, StringComparison.Ordinal);
        Assert.DoesNotContain(newKey!, listText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnerCanDeleteOwnListing()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateOwnerClient(OwnerA);
        var created = await RegisterAsync(client);
        var id = created.GetProperty("id").GetString()!;

        var deleted = await SendProtectedAsync(client, HttpMethod.Delete, $"/api/owner/servers/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var list = await client.GetAsync("/api/owner/servers");
        list.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Empty(document.RootElement.GetProperty("servers").EnumerateArray());
    }



    [Fact]
    public async Task TorchHeartbeat_RequiresServerKeyAndRejectsInvalidPlayerCounts()
    {
        using var factory = new DirectoryApiFactory();
        using var owner = factory.CreateOwnerClient(OwnerA);
        var created = await RegisterAsync(owner);
        var id = created.GetProperty("id").GetString()!;
        var apiKey = created.GetProperty("apiKey").GetString()!;

        using var missingKeyRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/servers/{id}/heartbeat")
        {
            Content = JsonContent.Create(new { players = 1, maxPlayers = 32, version = "SE1" })
        };
        using var missingKey = await owner.SendAsync(missingKeyRequest);
        using var wrongKey = await SendHeartbeatAsync(owner, id, "not-the-server-key");
        using var tooManyPlayers = await SendHeartbeatAsync(owner, id, apiKey, players: 33);
        using var negativePlayers = await SendHeartbeatAsync(owner, id, apiKey, players: -1);
        using var zeroCapacity = await SendHeartbeatAsync(owner, id, apiKey, maxPlayers: 0);
        using var excessiveCapacity = await SendHeartbeatAsync(owner, id, apiKey, maxPlayers: 10001);

        Assert.Equal(HttpStatusCode.Unauthorized, missingKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKey.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooManyPlayers.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negativePlayers.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zeroCapacity.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, excessiveCapacity.StatusCode);
    }

    [Fact]
    public async Task TorchHeartbeat_UpdatesOwnerListingWithLiveRuntimeState()
    {
        using var factory = new DirectoryApiFactory();
        using var owner = factory.CreateOwnerClient(OwnerA);
        var created = await RegisterAsync(owner);
        var id = created.GetProperty("id").GetString()!;
        var apiKey = created.GetProperty("apiKey").GetString()!;

        using var heartbeat = await SendHeartbeatAsync(
            owner, id, apiKey, players: 7, maxPlayers: 48, version: "SE1 integration build");
        Assert.Equal(HttpStatusCode.NoContent, heartbeat.StatusCode);

        using var listingResponse = await owner.GetAsync("/api/owner/servers");
        listingResponse.EnsureSuccessStatusCode();
        using var listingJson = JsonDocument.Parse(await listingResponse.Content.ReadAsStringAsync());
        var listing = Assert.Single(listingJson.RootElement.GetProperty("servers").EnumerateArray());

        Assert.Equal(id, listing.GetProperty("id").GetString());
        Assert.Equal(7, listing.GetProperty("players").GetInt32());
        Assert.Equal(48, listing.GetProperty("maxPlayers").GetInt32());
        Assert.Equal("SE1 integration build", listing.GetProperty("version").GetString());
        Assert.Equal("Online", listing.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SteamCallback_SignsInVerifiedSteamIdentityAndRotatesBrowserCookie()
    {
        using var factory = new DirectoryApiFactory(
            useSuccessfulSteamVerifier: true,
            useTestAuthentication: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var login = await client.GetAsync("/auth/steam/login");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var loginQuery = QueryHelpers.ParseQuery(login.Headers.Location!.Query);
        var returnTo = Assert.Single(loginQuery["openid.return_to"])
            ?? throw new InvalidOperationException("Steam login redirect did not include a return URL.");
        var state = Assert.Single(QueryHelpers.ParseQuery(new Uri(returnTo).Query)["state"])
            ?? throw new InvalidOperationException("Steam login return URL did not include state.");
        var claimedId = "https://steamcommunity.com/openid/id/" + OwnerA;
        var callbackParameters = new Dictionary<string, string>
        {
            ["state"] = state,
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
            ["openid.claimed_id"] = claimedId,
            ["openid.identity"] = claimedId,
            ["openid.return_to"] = returnTo,
            ["openid.response_nonce"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") + "integration-success",
            ["openid.assoc_handle"] = "integration-test-association",
            ["openid.signed"] = "op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
            ["openid.sig"] = "mock-signature"
        };
        var callbackPath = QueryHelpers.AddQueryString(
            "/auth/steam/callback",
            callbackParameters.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));

        var callback = await client.GetAsync(callbackPath);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location?.OriginalString);
        var cookies = string.Join(";", callback.Headers.TryGetValues("Set-Cookie", out var values)
            ? values
            : Array.Empty<string>());
        Assert.Contains("__Host-serpf-owner", cookies, StringComparison.Ordinal);
        Assert.Contains("__Host-serpf-browser", cookies, StringComparison.Ordinal);

        // The test factory deliberately has no header-based authentication handler here.
        // This request must authenticate solely from the protected cookie issued by the callback.
        var ownerResponse = await client.GetAsync("/api/owner/me");
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        using var ownerJson = JsonDocument.Parse(await ownerResponse.Content.ReadAsStringAsync());
        Assert.Equal(OwnerA, ownerJson.RootElement.GetProperty("steamId64").GetString());

        // Obtain a real antiforgery token and complete logout using the callback-issued cookie.
        var csrfResponse = await client.GetAsync("/api/owner/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        using var csrfJson = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync());
        var requestToken = csrfJson.RootElement.GetProperty("requestToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(requestToken));

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        logoutRequest.Headers.Add("Origin", Origin);
        logoutRequest.Headers.Add("X-CSRF-TOKEN", requestToken);
        var logout = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.GetAsync("/api/owner/me");
        // Cookie authentication redirects unauthenticated browser requests to the Steam login route.
        // A 200 response here would mean the callback-issued owner cookie remained valid.
        Assert.Equal(HttpStatusCode.Found, afterLogout.StatusCode);
        Assert.NotNull(afterLogout.Headers.Location);
        Assert.Equal("/auth/steam/login", afterLogout.Headers.Location!.IsAbsoluteUri
            ? afterLogout.Headers.Location.AbsolutePath
            : afterLogout.Headers.Location.OriginalString.Split('?', 2)[0]);
    }

    [Fact]
    public async Task SteamCallback_RejectsMissingAndDuplicateState()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            AllowAutoRedirect = false
        });

        var missing = await client.GetAsync("/auth/steam/callback");
        var duplicate = await client.GetAsync("/auth/steam/callback?state=first&state=second");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, duplicate.StatusCode);
    }

    [Fact]
    public async Task SteamCallback_RejectsMismatchedBrowserSession()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var state = await BeginSteamLoginAsync(client);
        using var callback = new HttpRequestMessage(HttpMethod.Get, "/auth/steam/callback?state=" + Uri.EscapeDataString(state));
        callback.Headers.Add("Cookie", "__Host-serpf-browser=" + new string('A', 43));

        var response = await client.SendAsync(callback);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SteamCallback_ConsumesStateBeforeRejectingInvalidAssertion()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var state = await BeginSteamLoginAsync(client);
        var first = await client.GetAsync("/auth/steam/callback?state=" + Uri.EscapeDataString(state));
        var replay = await client.GetAsync("/auth/steam/callback?state=" + Uri.EscapeDataString(state));

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task SteamCallback_RejectsDuplicateOpenIdParameters()
    {
        using var factory = new DirectoryApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var state = await BeginSteamLoginAsync(client);
        var response = await client.GetAsync(
            "/auth/steam/callback?state=" + Uri.EscapeDataString(state) +
            "&openid.mode=id_res&openid.mode=cancel");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> BeginSteamLoginAsync(HttpClient client)
    {
        var login = await client.GetAsync("/auth/steam/login");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.NotNull(login.Headers.Location);

        var steamQuery = QueryHelpers.ParseQuery(login.Headers.Location!.Query);
        var returnTo = Assert.Single(steamQuery["openid.return_to"])
            ?? throw new InvalidOperationException("Steam login redirect did not include a return URL.");
        var callbackQuery = QueryHelpers.ParseQuery(new Uri(returnTo).Query);
        var state = Assert.Single(callbackQuery["state"])
            ?? throw new InvalidOperationException("Steam login return URL did not include state.");
        Assert.False(string.IsNullOrWhiteSpace(state));
        return state;
    }

    private static async Task<HttpResponseMessage> SendHeartbeatAsync(
        HttpClient client, string id, string apiKey,
        int players = 1, int maxPlayers = 32, string version = "integration-test")
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/servers/{id}/heartbeat")
        {
            Content = JsonContent.Create(new { players, maxPlayers, version })
        };
        request.Headers.Add("X-Server-Api-Key", apiKey);
        return await client.SendAsync(request);
    }

    private static object Registration(string name) => new
    {
        name,
        description = "Integration test server",
        address = "localhost",
        port = 27016,
        maxPlayers = 32,
        region = "Test",
        tags = new[] { "roleplay" }
    };

    private static async Task<JsonElement> RegisterAsync(HttpClient client)
    {
        var response = await SendProtectedAsync(client, HttpMethod.Post, "/api/owner/servers", Registration("Owner A server"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> SendProtectedAsync(
        HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var token = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("Origin", Origin);
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/owner/csrf");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("requestToken").GetString()!;
    }

    private sealed class DirectoryApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "serpf-api-tests", Guid.NewGuid().ToString("N"));
        private readonly bool _useSuccessfulSteamVerifier;
        private readonly bool _useTestAuthentication;

        public DirectoryApiFactory(
            bool useSuccessfulSteamVerifier = false,
            bool useTestAuthentication = true)
        {
            _useSuccessfulSteamVerifier = useSuccessfulSteamVerifier;
            _useTestAuthentication = useTestAuthentication;
            System.IO.Directory.CreateDirectory(_root);
        }

        public HttpClient CreateOwnerClient(string steamId)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri(Origin),
                AllowAutoRedirect = false,
                HandleCookies = true
            });
            client.DefaultRequestHeaders.Add("X-Test-Steam-Id", steamId);
            return client;
        }

        public HttpClient CreateBearerOwnerClient(string steamId)
        {
            var pair = Services.GetRequiredService<DesktopTokenStore>().Issue(
                steamId, DesktopTokenStore.RegisteredClientId)
                ?? throw new InvalidOperationException("Could not issue test launcher token.");
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri(Origin),
                AllowAutoRedirect = false
            });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pair.AccessToken);
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Directory:PublicBaseUrl"] = Origin + "/",
                    ["Directory:DataPath"] = Path.Combine(_root, "servers.json"),
                    ["Directory:AuthDatabasePath"] = Path.Combine(_root, "auth.db"),
                    ["Directory:DataProtectionKeysPath"] = Path.Combine(_root, "keys")
                }));
            builder.ConfigureTestServices(services =>
            {
                if (_useSuccessfulSteamVerifier)
                {
                    services.AddHttpClient("SteamOpenIdVerifier")
                        .ConfigurePrimaryHttpMessageHandler(() => new SuccessfulSteamConfirmationHandler());
                }

                if (_useTestAuthentication)
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestSteamAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = TestSteamAuthHandler.SchemeName;
                        options.DefaultForbidScheme = TestSteamAuthHandler.SchemeName;
                    }).AddScheme<AuthenticationSchemeOptions, TestSteamAuthHandler>(
                        TestSteamAuthHandler.SchemeName, _ => { });
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && System.IO.Directory.Exists(_root))
                System.IO.Directory.Delete(_root, recursive: true);
        }
    }


    private sealed class SuccessfulSteamConfirmationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://steamcommunity.com/openid/login", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n")
            });
        }
    }

    private sealed class TestSteamAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "IntegrationTestSteam";

        public TestSteamAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Steam-Id", out var values) ||
                values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, values[0]!) }, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
    }
}
