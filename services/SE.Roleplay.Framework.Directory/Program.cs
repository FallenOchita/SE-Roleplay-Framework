using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
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
builder.Services.AddSingleton<PlayerProfileStore>();
builder.Services.AddSingleton<ServerMembershipStore>();
builder.Services.AddSingleton<DirectoryAuthDatabase>();
builder.Services.AddSingleton<SteamLoginStateStore>();
builder.Services.AddSingleton<DesktopTokenStore>();
builder.Services.AddSingleton<DesktopAuthorizationCodeStore>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddHttpClient("SteamOpenIdVerifier");
builder.Services.AddTransient<SteamOpenIdVerifier>(services =>
    new SteamOpenIdVerifier(
        services.GetRequiredService<IHttpClientFactory>().CreateClient("SteamOpenIdVerifier"),
        services.GetRequiredService<DirectoryAuthDatabase>(),
        services.GetRequiredService<TimeProvider>()));

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
    })
    .AddScheme<AuthenticationSchemeOptions, DesktopBearerAuthenticationHandler>(
        DesktopBearerAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("LauncherBearer", policy =>
    {
        policy.AddAuthenticationSchemes(DesktopBearerAuthenticationHandler.SchemeName);
        policy.RequireAuthenticatedUser();
    });
});
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
    options.AddPolicy("oauth-token", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("oauth-authorize", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
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
    options.AddPolicy("player-writes", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            (context.Connection.RemoteIpAddress?.ToString() ?? "unknown") + ":" +
            (context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);

var app = builder.Build();

// Create/upgrade the shared authentication database before serving requests.
await app.Services.GetRequiredService<DirectoryAuthDatabase>().InitializeAsync();
await app.Services.GetRequiredService<ServerDirectoryStore>().InitializeAsync();
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/oauth/authorize", (HttpContext context,
    DesktopAuthorizationCodeStore authorizationCodes, IAntiforgery antiforgery) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Pragma = "no-cache";

    var query = context.Request.Query;
    var clientId = query["client_id"].ToString();
    var redirectUri = query["redirect_uri"].ToString();
    var challenge = query["code_challenge"].ToString();
    var challengeMethod = query["code_challenge_method"].ToString();
    var launcherState = query["state"].ToString();

    if (query["response_type"] != "code" ||
        clientId != DesktopTokenStore.RegisteredClientId ||
        !DesktopAuthorizationCodeStore.IsLoopbackRedirect(redirectUri) ||
        challengeMethod != "S256" ||
        challenge.Length != 43 ||
        challenge.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) ||
        launcherState.Length is < 1 or > 256 ||
        launcherState.Any(char.IsControl))
        return Results.BadRequest("Invalid authorization request.");

    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(context.User);
    if (steamId is null)
    {
        var returnTo = context.Request.Path.ToUriComponent() + context.Request.QueryString.ToUriComponent();
        return Results.Redirect("/auth/steam/login?returnTo=" +
            Uri.EscapeDataString(returnTo));
    }

    var browserSession = context.Request.Cookies[BrowserCookieName];
    if (!IsBrowserSessionId(browserSession))
        return Results.BadRequest("A valid browser session is required. Restart Steam sign-in and retry.");

    var pendingState = authorizationCodes.CreateState(
        clientId, redirectUri, challenge, browserSession!, launcherState);
    var tokens = antiforgery.GetAndStoreTokens(context);
    var encodedState = HtmlEncoder.Default.Encode(pendingState);
    var encodedToken = HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty);
    var html = $"""
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Authorize SE Roleplay Framework</title></head>
        <body>
          <main>
            <h1>Authorize SE Roleplay Framework Launcher</h1>
            <p>The launcher is requesting permission to sign in as your Steam account.</p>
            <p>Steam identity: {HtmlEncoder.Default.Encode(steamId)}</p>
            <form method="post" action="/oauth/authorize/decision">
              <input type="hidden" name="state" value="{encodedState}">
              <input type="hidden" name="__RequestVerificationToken" value="{encodedToken}">
              <button type="submit" name="decision" value="approve">Approve</button>
              <button type="submit" name="decision" value="deny">Deny</button>
            </form>
          </main>
        </body>
        </html>
        """;
    return Results.Content(html, "text/html; charset=utf-8");
}).RequireRateLimiting("oauth-authorize");

app.MapPost("/oauth/authorize/decision", async (HttpContext context,
    DesktopAuthorizationCodeStore authorizationCodes, IAntiforgery antiforgery,
    IConfiguration configuration) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Pragma = "no-cache";

    if (!await OwnerRequestSecurity.ValidateMutationAsync(
        context, antiforgery, GetPublicBaseUrl(configuration)))
        return Results.BadRequest("A valid same-origin request and antiforgery token are required.");

    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(context.User);
    var browserSession = context.Request.Cookies[BrowserCookieName];
    if (steamId is null || !IsBrowserSessionId(browserSession))
        return Results.Unauthorized();

    var form = await context.Request.ReadFormAsync();
    var state = form["state"].ToString();
    var decision = form["decision"].ToString();
    if (state.Length is < 1 or > 128 || state.Any(char.IsControl))
        return Results.BadRequest("Invalid authorization transaction.");

    if (decision == "deny")
    {
        var denied = authorizationCodes.DenyAuthorization(state, browserSession!);
        if (denied is null)
            return Results.BadRequest("Authorization request expired or was already used.");
        var denialUri = denied.RedirectUri + "?error=access_denied&state=" +
            Uri.EscapeDataString(denied.ClientState ?? string.Empty);
        return Results.Redirect(denialUri);
    }

    if (decision != "approve")
        return Results.BadRequest("Invalid authorization decision.");

    var approved = authorizationCodes.CompleteAuthorization(state, browserSession!, steamId);
    if (approved is null)
        return Results.BadRequest("Authorization request expired or was already used.");
    var callbackUri = approved.RedirectUri + "?code=" + Uri.EscapeDataString(approved.Code) +
        "&state=" + Uri.EscapeDataString(approved.ClientState ?? string.Empty);
    return Results.Redirect(callbackUri);
}).RequireAuthorization().RequireRateLimiting("oauth-authorize");

app.MapPost("/oauth/token", (HttpContext context, DesktopTokenRequest request,
    DesktopAuthorizationCodeStore authorizationCodes, DesktopTokenStore tokens) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Pragma = "no-cache";

    if (request.ClientId != DesktopTokenStore.RegisteredClientId)
        return Results.BadRequest(new { error = "invalid_grant" });
    var registeredClientId = DesktopTokenStore.RegisteredClientId;

    DesktopTokenPair? pair = request.GrantType switch
    {
        "authorization_code" when
            !string.IsNullOrWhiteSpace(request.Code) &&
            !string.IsNullOrWhiteSpace(request.RedirectUri) &&
            !string.IsNullOrWhiteSpace(request.CodeVerifier)
                => RedeemAuthorizationCode(),
        "refresh_token" when !string.IsNullOrWhiteSpace(request.RefreshToken)
                => tokens.RotateRefreshToken(request.RefreshToken, registeredClientId),
        _ => null
    };

    return pair is null
        ? Results.BadRequest(new { error = "invalid_grant" })
        : Results.Json(pair);
    
    DesktopTokenPair? RedeemAuthorizationCode()
    {
        var grant = authorizationCodes.RedeemCode(
            request.Code!, registeredClientId, request.RedirectUri!, request.CodeVerifier!);
        return grant is null ? null : tokens.Issue(grant.SteamId64, grant.ClientId);
    }
}).RequireRateLimiting("oauth-token");

app.MapGet("/api/launcher/me", (ClaimsPrincipal user) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    var clientId = user.FindFirst("client_id")?.Value;
    return steamId is null || clientId != DesktopTokenStore.RegisteredClientId
        ? Results.Unauthorized()
        : Results.Ok(new { steamId64 = steamId, clientId });
}).RequireAuthorization("LauncherBearer");

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "SE Roleplay Framework Directory" }));
app.MapGet("/api/servers", async (ServerDirectoryStore store, CancellationToken cancellationToken) =>
    Results.Ok(new { servers = await store.GetPublicServersAsync(cancellationToken) }));

app.MapGet("/auth/steam/login", (HttpContext context, SteamLoginStateStore stateStore, IConfiguration configuration) =>
{
    var publicBaseUrl = GetPublicBaseUrl(configuration);
    if (publicBaseUrl is null)
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Steam sign-in is not configured.");

    var requestedReturnTo = context.Request.Query["returnTo"].ToString();
    var safeReturnTo = requestedReturnTo.Length is > 0 and <= 4096 &&
        requestedReturnTo.StartsWith("/oauth/authorize?", StringComparison.Ordinal) &&
        !requestedReturnTo.StartsWith("//", StringComparison.Ordinal) &&
        !requestedReturnTo.Contains('\\') && !requestedReturnTo.Any(char.IsControl)
            ? requestedReturnTo : null;

    var browserSessionId = context.Request.Cookies[BrowserCookieName];
    if (!IsBrowserSessionId(browserSessionId))
        browserSessionId = CreateBrowserSessionId();

    context.Response.Cookies.Append(BrowserCookieName, browserSessionId!, BrowserCookieOptions());
    var callback = new Uri(publicBaseUrl, "/auth/steam/callback");
    var returnTo = callback.AbsoluteUri + "?state=" +
        Uri.EscapeDataString(stateStore.Create(browserSessionId!, safeReturnTo));
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
        !stateStore.TryConsume(stateValues[0]!, browserSessionId!, out var loginReturnPath))
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
    return Results.Redirect(loginReturnPath ?? "/");
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

// Player identity is always derived from the validated launcher bearer token.
app.MapGet("/api/player/profile", async Task<IResult> (
    ClaimsPrincipal user, PlayerProfileStore profiles, CancellationToken cancellationToken) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (steamId is null) return Results.Unauthorized();
    var profile = await profiles.GetProfileAsync(steamId, cancellationToken);
    return profile is null ? Results.Unauthorized() : Results.Ok(profile);
}).RequireAuthorization("LauncherBearer");

app.MapPut("/api/player/profile", async Task<IResult> (
    ClaimsPrincipal user, UpdatePlayerProfileRequest request, PlayerProfileStore profiles,
    CancellationToken cancellationToken) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (steamId is null) return Results.Unauthorized();
    var profile = await profiles.UpdateProfileAsync(steamId, request, cancellationToken);
    return profile is null
        ? Results.ValidationProblem(new Dictionary<string, string[]>
            { ["profile"] = new[] { "Display name is required (maximum 40 characters); biography must be 500 characters or fewer." } })
        : Results.Ok(profile);
}).RequireAuthorization("LauncherBearer").RequireRateLimiting("player-writes");

app.MapGet("/api/servers/{serverId}/permissions/me", async Task<IResult> (
    string serverId, ClaimsPrincipal user, PlayerProfileStore profiles, CancellationToken cancellationToken) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (steamId is null) return Results.Unauthorized();
    var permissions = await profiles.GetPermissionsAsync(serverId, steamId, cancellationToken);
    return Results.Ok(new { serverId, steamId64 = steamId, permissions });
}).RequireAuthorization("LauncherBearer");

app.MapPost("/api/owner/servers/{serverId}/players/permissions", async Task<IResult> (
    string serverId, HttpContext context, GrantPlayerPermissionRequest request,
    PlayerProfileStore profiles, IAntiforgery antiforgery, IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!await OwnerRequestSecurity.ValidateMutationAsync(context, antiforgery, GetPublicBaseUrl(configuration)))
        return Results.BadRequest(new { error = "A valid same-origin request and antiforgery token are required." });
    var ownerSteamId = OwnerRequestSecurity.GetVerifiedSteamId(context.User);
    if (ownerSteamId is null) return Results.Unauthorized();
    if (!PlayerProfileStore.IsValidSteamId64(request.SteamId64) ||
        !PlayerProfileStore.IsValidPermission(request.Permission))
        return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["permission"] = new[] { "A valid SteamID64 and lowercase permission key are required." } });
    if (request.ExpiresAtUtc is not null &&
        (request.ExpiresAtUtc <= DateTime.UtcNow || request.ExpiresAtUtc > DateTime.UtcNow.AddDays(365)))
        return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["expiresAtUtc"] = new[] { "Expiry must be in the future and no more than 365 days away." } });
    var granted = await profiles.GrantPermissionAsync(
        serverId, ownerSteamId, request.SteamId64, request.Permission, request.ExpiresAtUtc, cancellationToken);
    return granted ? Results.Ok(new { granted = true }) : Results.NotFound();
}).RequireAuthorization().RequireRateLimiting("owner-writes");

app.MapDelete("/api/owner/servers/{serverId}/players/permissions", async Task<IResult> (
    string serverId, string steamId64, string permission, HttpContext context,
    PlayerProfileStore profiles, IAntiforgery antiforgery, IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!await OwnerRequestSecurity.ValidateMutationAsync(context, antiforgery, GetPublicBaseUrl(configuration)))
        return Results.BadRequest(new { error = "A valid same-origin request and antiforgery token are required." });
    var ownerSteamId = OwnerRequestSecurity.GetVerifiedSteamId(context.User);
    if (ownerSteamId is null) return Results.Unauthorized();
    var revoked = await profiles.RevokePermissionAsync(
        serverId, ownerSteamId, steamId64, permission, cancellationToken);
    return revoked ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization().RequireRateLimiting("owner-writes");

// Launcher owner endpoints use the short-lived directory-issued bearer token.
// Unlike browser-cookie mutations, bearer requests are not ambient and do not use CSRF tokens.
app.MapGet("/api/launcher/owner/servers", async Task<IResult> (
    ClaimsPrincipal user, ServerDirectoryStore store, CancellationToken cancellationToken) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (steamId is null) return Results.Unauthorized();
    var servers = await store.GetOwnedServersAsync(steamId, cancellationToken);
    return Results.Ok(new { servers });
}).RequireAuthorization("LauncherBearer");

app.MapPost("/api/launcher/owner/servers", async Task<IResult> (
    ClaimsPrincipal user, RegisterServerRequest request, ServerDirectoryStore store,
    CancellationToken cancellationToken) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (steamId is null) return Results.Unauthorized();
    if (!IsValidRegistration(request, out var error))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["server"] = new[] { error } });
    var (server, apiKey) = await store.RegisterOwnedAsync(request, steamId, cancellationToken);
    return Results.Created($"/api/launcher/owner/servers/{server.Id}", new
    {
        server.Id, server.Name, server.Address, server.Port, apiKey,
        message = "Store this API key securely. It is returned only once."
    });
}).RequireAuthorization("LauncherBearer").RequireRateLimiting("owner-writes");

app.MapPut("/api/launcher/owner/servers/{id}", async Task<IResult> (
    string id, ClaimsPrincipal user, RegisterServerRequest request, ServerDirectoryStore store,
    CancellationToken cancellationToken) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (steamId is null) return Results.Unauthorized();
    if (!IsValidRegistration(request, out var error))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["server"] = new[] { error } });
    return await store.UpdateOwnedAsync(id, steamId, request, cancellationToken)
        ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization("LauncherBearer").RequireRateLimiting("owner-writes");

app.MapDelete("/api/launcher/owner/servers/{id}", async Task<IResult> (
    string id, ClaimsPrincipal user, ServerDirectoryStore store, CancellationToken cancellationToken) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (steamId is null) return Results.Unauthorized();
    return await store.DeleteOwnedAsync(id, steamId, cancellationToken)
        ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization("LauncherBearer").RequireRateLimiting("owner-writes");

app.MapPost("/api/launcher/owner/servers/{id}/rotate-key", async Task<IResult> (
    string id, ClaimsPrincipal user, ServerDirectoryStore store, CancellationToken cancellationToken) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (steamId is null) return Results.Unauthorized();
    var apiKey = await store.RotateOwnedApiKeyAsync(id, steamId, cancellationToken);
    return apiKey is null ? Results.NotFound() : Results.Ok(new
    {
        apiKey,
        message = "Store this API key securely. It is returned only once."
    });
}).RequireAuthorization("LauncherBearer").RequireRateLimiting("owner-writes");

// Server allowlist membership is keyed only by the Steam identity in the validated launcher token.
app.MapGet("/api/servers/{serverId}/membership/me", async Task<IResult> (
    string serverId, ClaimsPrincipal user, ServerMembershipStore memberships, CancellationToken cancellationToken) =>
{
    var steamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (steamId is null) return Results.Unauthorized();
    var status = await memberships.GetStatusAsync(serverId, steamId, cancellationToken);
    return Results.Ok(status);
}).RequireAuthorization("LauncherBearer");

app.MapGet("/api/launcher/owner/servers/{serverId}/members", async Task<IResult> (
    string serverId, ClaimsPrincipal user, ServerMembershipStore memberships, CancellationToken cancellationToken) =>
{
    var ownerSteamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (ownerSteamId is null) return Results.Unauthorized();
    var members = await memberships.GetMembersAsync(serverId, ownerSteamId, cancellationToken);
    return Results.Ok(new { serverId, members });
}).RequireAuthorization("LauncherBearer");

app.MapPost("/api/launcher/owner/servers/{serverId}/members", async Task<IResult> (
    string serverId, ClaimsPrincipal user, AddServerMemberRequest request,
    ServerMembershipStore memberships, CancellationToken cancellationToken) =>
{
    var ownerSteamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (ownerSteamId is null) return Results.Unauthorized();
    if (!PlayerProfileStore.IsValidSteamId64(request.SteamId64) || request.Note is { Length: > 200 })
        return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["member"] = new[] { "A valid SteamID64 and a note of 200 characters or fewer are required." } });
    var added = await memberships.AddMemberAsync(
        serverId, ownerSteamId, request.SteamId64, request.Note, cancellationToken);
    return added ? Results.Ok(new { added = true }) : Results.NotFound();
}).RequireAuthorization("LauncherBearer").RequireRateLimiting("owner-writes");

app.MapDelete("/api/launcher/owner/servers/{serverId}/members/{steamId64}", async Task<IResult> (
    string serverId, string steamId64, ClaimsPrincipal user, ServerMembershipStore memberships,
    CancellationToken cancellationToken) =>
{
    var ownerSteamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (ownerSteamId is null) return Results.Unauthorized();
    return await memberships.RemoveMemberAsync(serverId, ownerSteamId, steamId64, cancellationToken)
        ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization("LauncherBearer").RequireRateLimiting("owner-writes");

// Server-side administration endpoints authenticate with the server's private API key.
// The key never leaves the Torch process and is not exposed to the launcher.
app.MapGet("/api/servers/{serverId}/allowlist", async Task<IResult> (
    string serverId, HttpContext context, ServerMembershipStore memberships,
    CancellationToken cancellationToken) =>
{
    if (!context.Request.Headers.TryGetValue("X-Server-Api-Key", out var key))
        return Results.Unauthorized();
    var ownerSteamId = await memberships.GetServerOwnerSteamId64Async(serverId, key.ToString(), cancellationToken);
    if (ownerSteamId is null) return Results.Unauthorized();
    var enabled = await memberships.GetAllowlistEnabledAsync(serverId, ownerSteamId, cancellationToken);
    if (enabled is null) return Results.NotFound();
    var members = await memberships.GetMembersAsync(serverId, ownerSteamId, cancellationToken);
    return Results.Ok(new { serverId, allowlistEnabled = enabled.Value, members });
});

app.MapPost("/api/servers/{serverId}/allowlist/members", async Task<IResult> (
    string serverId, HttpContext context, AddServerMemberRequest request,
    ServerMembershipStore memberships, CancellationToken cancellationToken) =>
{
    if (!context.Request.Headers.TryGetValue("X-Server-Api-Key", out var key))
        return Results.Unauthorized();
    var ownerSteamId = await memberships.GetServerOwnerSteamId64Async(serverId, key.ToString(), cancellationToken);
    if (ownerSteamId is null) return Results.Unauthorized();
    if (!PlayerProfileStore.IsValidSteamId64(request.SteamId64) || request.Note is { Length: > 200 })
        return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["member"] = new[] { "A valid SteamID64 and a note of 200 characters or fewer are required." } });
    var added = await memberships.AddMemberAsync(
        serverId, ownerSteamId, request.SteamId64, request.Note, cancellationToken);
    return added ? Results.Ok(new { added = true }) : Results.NotFound();
});

app.MapDelete("/api/servers/{serverId}/allowlist/members/{steamId64}", async Task<IResult> (
    string serverId, string steamId64, HttpContext context, ServerMembershipStore memberships,
    CancellationToken cancellationToken) =>
{
    if (!context.Request.Headers.TryGetValue("X-Server-Api-Key", out var key))
        return Results.Unauthorized();
    var ownerSteamId = await memberships.GetServerOwnerSteamId64Async(serverId, key.ToString(), cancellationToken);
    if (ownerSteamId is null) return Results.Unauthorized();
    return await memberships.RemoveMemberAsync(serverId, ownerSteamId, steamId64, cancellationToken)
        ? Results.NoContent() : Results.NotFound();
});

app.MapPut("/api/servers/{serverId}/allowlist", async Task<IResult> (
    string serverId, HttpContext context, UpdateServerAdmissionSettingsRequest request,
    ServerMembershipStore memberships, CancellationToken cancellationToken) =>
{
    if (!context.Request.Headers.TryGetValue("X-Server-Api-Key", out var key))
        return Results.Unauthorized();
    var ownerSteamId = await memberships.GetServerOwnerSteamId64Async(serverId, key.ToString(), cancellationToken);
    if (ownerSteamId is null) return Results.Unauthorized();
    return await memberships.SetAllowlistEnabledAsync(
        serverId, ownerSteamId, request.AllowlistEnabled, cancellationToken)
        ? Results.NoContent() : Results.NotFound();
});

// Game-server admission decisions are authenticated with the server's own API key.
// Unknown servers and invalid keys intentionally share the same response.
app.MapGet("/api/servers/{serverId}/admission/{steamId64}", async Task<IResult> (
    string serverId, string steamId64, HttpContext context, ServerMembershipStore memberships,
    CancellationToken cancellationToken) =>
{
    if (!context.Request.Headers.TryGetValue("X-Server-Api-Key", out var key))
        return Results.Unauthorized();
    var decision = await memberships.GetAdmissionDecisionAsync(
        serverId, key.ToString(), steamId64, cancellationToken);
    return decision is null ? Results.Unauthorized() : Results.Ok(decision);
});

// Only the authenticated owner can enable or disable enforcement.
app.MapPut("/api/launcher/owner/servers/{serverId}/admission", async Task<IResult> (
    string serverId, ClaimsPrincipal user, UpdateServerAdmissionSettingsRequest request,
    ServerMembershipStore memberships, CancellationToken cancellationToken) =>
{
    var ownerSteamId = OwnerRequestSecurity.GetVerifiedSteamId(user);
    if (ownerSteamId is null) return Results.Unauthorized();
    return await memberships.SetAllowlistEnabledAsync(
        serverId, ownerSteamId, request.AllowlistEnabled, cancellationToken)
        ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization("LauncherBearer").RequireRateLimiting("owner-writes");

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
