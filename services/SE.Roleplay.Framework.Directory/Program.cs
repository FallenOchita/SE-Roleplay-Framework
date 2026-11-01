using System.Net;
using SE.Roleplay.Framework.Directory.Models;
using SE.Roleplay.Framework.Directory.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ServerDirectoryStore>();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);

var app = builder.Build();
app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "SE Roleplay Framework Directory" }));
app.MapGet("/api/servers", async (ServerDirectoryStore store, CancellationToken cancellationToken) =>
    Results.Ok(new { servers = await store.GetPublicServersAsync(cancellationToken) }));

app.MapPost("/api/admin/servers", async (HttpContext context, RegisterServerRequest request, ServerDirectoryStore store, IConfiguration configuration, CancellationToken cancellationToken) =>
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

app.MapPut("/api/servers/{id}/heartbeat", async (string id, HttpContext context, HeartbeatRequest request, ServerDirectoryStore store, CancellationToken cancellationToken) =>
{
    if (!context.Request.Headers.TryGetValue("X-Server-Api-Key", out var key)) return Results.Unauthorized();
    if (request.Players < 0 || request.MaxPlayers < 1 || request.MaxPlayers > 10000 || request.Players > request.MaxPlayers)
        return Results.BadRequest(new { error = "Player counts must satisfy 0 <= players <= maxPlayers <= 10000." });
    var updated = await store.HeartbeatAsync(id, key.ToString(), request, cancellationToken);
    return updated ? Results.NoContent() : Results.Unauthorized();
});

app.MapDelete("/api/admin/servers/{id}", async (string id, HttpContext context, ServerDirectoryStore store, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!IsAdmin(context, configuration)) return Results.Unauthorized();
    return await store.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
});

app.Run();

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
    else if (request.Tags.Count > 20 || request.Tags.Any(tag => tag.Length > 40)) error = "A maximum of 20 tags, each 40 characters or fewer, is allowed.";
    return error.Length == 0;
}

public partial class Program { }
