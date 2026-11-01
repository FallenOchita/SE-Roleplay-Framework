using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using SE.Roleplay.Framework.Directory.Models;
using SE.Roleplay.Framework.Directory.Services;

const string BrowserCookieName = "__Host-serpf-browser";
const string OwnerCookieName = "__Host-serpf-owner";
const string SteamLoginPolicy = "steam-login";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ServerDirectoryStore>();
builder.Services.AddSingleton<SteamLoginStateStore>();
builder.Services.AddSingleton<DesktopTokenStore>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddHttpClient<SteamOpenIdVerifier>();

var keyPath = builder.Configuration["Directory:DataProtectionKeysPath"]
    ?? Environment.GetEnvironmentVariable("SERPF_DATA_PROTECTION_KEYS_PATH")
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".aspnet", "DataProtection-Keys", "SE-Roleplay-Framework");
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection()
    .SetApplicationName("SE-Roleplay-Framework.Directory")
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = OwnerCookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.LoginPath = "/auth/steam/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-serpf-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(SteamLoginPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("owner-writes", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            (context.Connection.RemoteIpAddress?.ToString() ?? "unknown") + ":" +
            (context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);

var app = builder.Build();
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "SE Roleplay Framework Directory" }));
app.MapGet("/api/servers", async (ServerDirectoryStore store, CancellationToken cancellationToken) =>
    Results.Ok(new { servers = await store.GetPublicServersAsync(cancellationToken) }));

app.MapGet("/auth/steam/login", (HttpContext context, SteamLoginStateStore stateStore, IConfiguration configuration) =>
{
    var publicBaseUrl = GetPublicBaseUrl(configuration);
    if (publicBaseUrl is null)
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Steam sign-in is not configured.");

    var browserSessionId = context.Request.Cookies[BrowserCookieName];
    if (!IsBrowserSessionId(browserSessionId))
        browserSessionId = CreateBrowserSessionId();

    context.Response.Cookies.Append(BrowserCookieName, browserSessionId!, BrowserCookieOptions());
    var callback = new Uri(publicBaseUrl, "/auth/steam/callback");
    var returnTo = callback.AbsoluteUri + "?state=" + Uri.EscapeDataString(stateStore.Create(browserSessionId!));
    var redirect = SteamOpenIdRequestBuilder.Build(returnTo, publicBaseUrl.AbsoluteUri);
    return Results.Redirect(redirect.AbsoluteUri);
}).RequireRateLimiting(SteamLoginPolicy);

app.MapGet("/auth/steam/callback", async Task<IResult> (
    HttpContext context,
    SteamLoginStateStore stateStore,
    SteamOpenIdVerifier verifier,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var publicBaseUrl = GetPublicBaseUrl(configuration);
    if (publicBaseUrl is null)
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Steam sign-in is not configured.");

    var stateValues = context.Request.Query["state"];
    var browserSessionId = context.Request.Cookies[BrowserCookieName];
    if (stateValues.Count != 1 || string.IsNullOrWhiteSpace(stateValues[0]) ||
        !IsBrowserSessionId(browserSessionId) ||
        !stateStore.TryConsume(stateValues[0]!, browserSessionId!))
        return Results.Unauthorized();

    var assertion = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var pair in context.Request.Query)
    {
        if (!pair.Key.StartsWith("openid.", StringComparison.Ordinal))
            continue;
        if (pair.Value.Count != 1 || string.IsNullOrWhiteSpace(pair.Value[0]))
            return Results.Unauthorized();
        assertion[pair.Key] = pair.Value[0]!;
    }

    var callback = new Uri(publicBaseUrl, "/auth/steam/callback");
    var expectedReturnTo = callback.AbsoluteUri + "?state=" + Uri.EscapeDataString(stateValues[0]!);
    var steamId = await verifier.VerifyAsync(assertion, expectedReturnTo, cancellationToken);
    if (steamId is null)
        return Results.Unauthorized();

    var identity = new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.NameIdentifier, steamId), new Claim(ClaimTypes.Name, steamId) },
        CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));

    // Rotate the transient state-binding cookie after authentication.
    context.Response.Cookies.Append(BrowserCookieName, CreateBrowserSessionId(), BrowserCookieOptions());
    return Results.Redirect("/");
}).RequireRateLimiting(SteamLoginPolicy);

app.MapPost("/api/admin/servers", async Task<IResult> (HttpContext context, RegisterServerRequest request, ServerDirectoryStore store, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!IsAdmin(context, configuration)) return Results.Unauthorized();
    if (!IsValidRegistration(request, out var error))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["server"] = new[] { error } });

    var (server, apiKey) = await store.RegisterAsync(request, cancellationToken);
    return Results.Created($"/api/servers/{server.Id}", new
    {
        server.Id, server.Name, server.Address, server.Port, apiKey,
        message = "Store this API key securely. It is returned only once."
    });
});

app.MapPut("/api/servers/{id}/heartbeat", async Task<IResult> (string id, HttpContext context, HeartbeatRequest request, ServerDirectoryStore store, CancellationToken cancellationToken) =>
{
    if (!context.Request.Headers.TryGetValue("X-Server-Api-Key", out var key)) return Results.Unauthorized();
    if (request.Players < 0 || request.MaxPlayers < 1 || request.MaxPlayers > 10000 || request.Players > request.MaxPlayers)
        return Results.BadRequest(new { error = "Player counts must satisfy 0 <= players <= maxPlayers <= 10000." });
    var updated = await store.HeartbeatAsync(id, key.ToString(), request, cancellationToken);
    return updated ? Results.NoContent() : Results.Unauthorized();
});

app.MapDelete("/api/admin/servers/{id}", async Task<IResult> (string id, HttpContext context, ServerDirectoryStore store, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!IsAdmin(context, configuration)) return Results.Unauthorized();
    return await store.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
});


app.MapGet("/api/owner/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { requestToken = tokens.RequestToken, headerName = "X-CSRF-TOKEN" });
}).RequireAuthorization();

app.MapGet("/api/owner/me", (ClaimsPrincipal user) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    return steamId is null ? Results.Unauthorized() : Results.Ok(new { steamId64 = steamId });
}).RequireAuthorization();

app.MapGet("/api/owner/servers", async Task<IResult> (
    ClaimsPrincipal user, ServerDirectoryStore store, CancellationToken cancellationToken) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (steamId is null) return Results.Unauthorized();
    var servers = await store.GetOwnedServersAsync(steamId, cancellationToken);
    return Results.Ok(new { servers });
}).RequireAuthorization();

app.MapPost("/api/owner/servers", async Task<IResult> (
    HttpContext context, RegisterServerRequest request, ServerDirectoryStore store,
    IAntiforgery antiforgery, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!await OwnerRequestSecurity.ValidateMutationAsync(context, antiforgery, GetPublicBaseUrl(configuration)))
        return Results.BadRequest(new { error = "A valid same-origin request and antiforgery token are required." });
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(context.User);
    if (steamId is null) return Results.Unauthorized();
    if (!IsValidRegistration(request, out var error))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["server"] = new[] { error } });
    var (server, apiKey) = await store.RegisterOwnedAsync(request, steamId, cancellationToken);
    return Results.Created($"/api/owner/servers/{server.Id}", new
    {
        server.Id, server.Name, server.Address, server.Port, apiKey,
        message = "Store this API key securely. It is returned only once."
    });
}).RequireAuthorization().RequireRateLimiting("owner-writes");

app.MapPut("/api/owner/servers/{id}", async Task<IResult> (
    string id, HttpContext context, RegisterServerRequest request, ServerDirectoryStore store,
    IAntiforgery antiforgery, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!await OwnerRequestSecurity.ValidateMutationAsync(context, antiforgery, GetPublicBaseUrl(configuration)))
        return Results.BadRequest(new { error = "A valid same-origin request and antiforgery token are required." });
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(context.User);
    if (steamId is null) return Results.Unauthorized();
    if (!IsValidRegistration(request, out var error))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["server"] = new[] { error } });
    return await store.UpdateOwnedAsync(id, steamId, request, cancellationToken)
        ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization().RequireRateLimiting("owner-writes");

app.MapDelete("/api/owner/servers/{id}", async Task<IResult> (
    string id, HttpContext context, ServerDirectoryStore store, IAntiforgery antiforgery,
    IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!await OwnerRequestSecurity.ValidateMutationAsync(context, antiforgery, GetPublicBaseUrl(configuration)))
        return Results.BadRequest(new { error = "A valid same-origin request and antiforgery token are required." });
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(context.User);
    if (steamId is null) return Results.Unauthorized();
    return await store.DeleteOwnedAsync(id, steamId, cancellationToken)
        ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization().RequireRateLimiting("owner-writes");

app.MapPost("/api/owner/servers/{id}/rotate-key", async Task<IResult> (
    string id, HttpContext context, ServerDirectoryStore store, IAntiforgery antiforgery,
    IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!await OwnerRequestSecurity.ValidateMutationAsync(context, antiforgery, GetPublicBaseUrl(configuration)))
        return Results.BadRequest(new { error = "A valid same-origin request and antiforgery token are required." });
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(context.User);
    if (steamId is null) return Results.Unauthorized();
    var apiKey = await store.RotateOwnedApiKeyAsync(id, steamId, cancellationToken);
    return apiKey is null ? Results.NotFound() : Results.Ok(new
    {
        apiKey,
        message = "Store this API key securely. It is returned only once."
    });
}).RequireAuthorization().RequireRateLimiting("owner-writes");

app.MapPost("/auth/logout", async Task<IResult> (
    HttpContext context, IAntiforgery antiforgery, IConfiguration configuration) =>
{
    if (!await OwnerRequestSecurity.ValidateMutationAsync(context, antiforgery, GetPublicBaseUrl(configuration)))
        return Results.BadRequest(new { error = "A valid same-origin request and antiforgery token are required." });
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    context.Response.Cookies.Delete(BrowserCookieName, BrowserCookieOptions());
    return Results.NoContent();
}).RequireAuthorization().RequireRateLimiting("owner-writes");

app.Run();

static Uri? GetPublicBaseUrl(IConfiguration configuration)
{
    var configured = configuration["Directory:PublicBaseUrl"]
        ?? Environment.GetEnvironmentVariable("SERPF_DIRECTORY_PUBLIC_BASE_URL");
    if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) ||
        uri.Scheme != Uri.UriSchemeHttps ||
        !string.IsNullOrEmpty(uri.Query) ||
        !string.IsNullOrEmpty(uri.Fragment) ||
        uri.AbsolutePath != "/")
        return null;
    return new Uri(uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/') + "/");
}

static string CreateBrowserSessionId() =>
    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

static bool IsBrowserSessionId(string? value) =>
    value is { Length: 43 } && value.All(character =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

static CookieOptions BrowserCookieOptions() => new()
{
    HttpOnly = true,
    Secure = true,
    SameSite = SameSiteMode.Lax,
    Path = "/",
    IsEssential = true
};

static bool IsAdmin(HttpContext context, IConfiguration configuration)
{
    var configured = configuration["Directory:AdminKey"] ?? Environment.GetEnvironmentVariable("SERPF_DIRECTORY_ADMIN_KEY");
    if (string.IsNullOrWhiteSpace(configured) || !context.Request.Headers.TryGetValue("X-Directory-Admin-Key", out var provided))
        return false;
    return ServerDirectoryStore.VerifyKey(provided.ToString(), ServerDirectoryStore.HashKey(configured));
}

static bool IsValidRegistration(RegisterServerRequest request, out string error)
{
    error = string.Empty;
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80) error = "Name is required and must be 80 characters or fewer.";
    else if (request.Description.Length > 1000) error = "Description must be 1000 characters or fewer.";
    else if (string.IsNullOrWhiteSpace(request.Address) || request.Address.Length > 253) error = "A valid server address is required.";
    else if (!IPAddress.TryParse(request.Address, out _) && Uri.CheckHostName(request.Address) != UriHostNameType.Dns) error = "Address must be an IP address or DNS hostname.";
    else if (request.Port is < 1 or > 65535) error = "Port must be between 1 and 65535.";
    else if (request.MaxPlayers is < 1 or > 10000) error = "MaxPlayers must be between 1 and 10000.";
    else if (request.Tags == null || request.Tags.Count > 20 || request.Tags.Any(tag => tag == null || tag.Length > 40)) error = "A maximum of 20 tags, each 40 characters or fewer, is allowed.";
    return error.Length == 0;
}

public partial class Program { }
