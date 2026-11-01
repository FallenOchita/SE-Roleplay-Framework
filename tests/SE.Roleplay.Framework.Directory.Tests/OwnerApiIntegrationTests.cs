using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace SE.Roleplay.Framework.Directory.Tests;

public sealed class OwnerApiIntegrationTests
{
    private const string OwnerA = "76561198012345678";
    private const string OwnerB = "76561198087654321";
    private const string Origin = "https://directory.test";

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

        public DirectoryApiFactory()
        {
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

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Directory:PublicBaseUrl"] = Origin + "/",
                    ["Directory:DataPath"] = Path.Combine(_root, "servers.json"),
                    ["Directory:DataProtectionKeysPath"] = Path.Combine(_root, "keys")
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestSteamAuthHandler.SchemeNameName;
                    options.DefaultChallengeScheme = TestSteamAuthHandler.SchemeName;
                    options.DefaultForbidScheme = TestSteamAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestSteamAuthHandler>(
                    TestSteamAuthHandler.SchemeName, _ => { });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && System.IO.Directory.Exists(_root))
                System.IO.Directory.Delete(_root, recursive: true);
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
