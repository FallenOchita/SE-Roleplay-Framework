using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SE.Roleplay.Framework.Launcher.Models;

namespace SE.Roleplay.Framework.Launcher.Services;

public sealed class LauncherDirectoryApiClient(DesktopAuthorizationService authorization)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PlayerProfileDto> GetProfileAsync(string directoryUrl, CancellationToken cancellationToken = default) =>
        await SendAsync<PlayerProfileDto>(HttpMethod.Get, directoryUrl, "/api/player/profile", null, cancellationToken)
        ?? throw new InvalidOperationException("The directory returned no player profile.");

    public async Task<PlayerProfileDto> SaveProfileAsync(string directoryUrl, string displayName, string biography,
        CancellationToken cancellationToken = default) =>
        await SendAsync<PlayerProfileDto>(HttpMethod.Put, directoryUrl, "/api/player/profile",
            new UpdatePlayerProfileDto(displayName, biography), cancellationToken)
        ?? throw new InvalidOperationException("The directory returned no saved profile.");

    public async Task<IReadOnlyList<ServerListing>> GetOwnedServersAsync(string directoryUrl,
        CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<OwnedServersResponse>(HttpMethod.Get, directoryUrl,
            "/api/launcher/owner/servers", null, cancellationToken);
        return result?.Servers ?? Array.Empty<ServerListing>();
    }

    public async Task<CreatedOwnedServerDto> CreateOwnedServerAsync(string directoryUrl, RegisterOwnedServerDto request,
        CancellationToken cancellationToken = default) =>
        await SendAsync<CreatedOwnedServerDto>(HttpMethod.Post, directoryUrl,
            "/api/launcher/owner/servers", request, cancellationToken)
        ?? throw new InvalidOperationException("The directory did not return the new server credentials.");

    public async Task<string> RotateServerKeyAsync(string directoryUrl, string serverId,
        CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<RotatedServerKeyDto>(HttpMethod.Post, directoryUrl,
            $"/api/launcher/owner/servers/{Uri.EscapeDataString(serverId)}/rotate-key", new { }, cancellationToken);
        return result?.ApiKey ?? throw new InvalidOperationException("The directory did not return a replacement API key.");
    }

    public async Task DeleteOwnedServerAsync(string directoryUrl, string serverId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(HttpMethod.Delete, directoryUrl,
            $"/api/launcher/owner/servers/{Uri.EscapeDataString(serverId)}", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string directoryUrl, string path, object? body,
        CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, directoryUrl, path, body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string directoryUrl, string path,
        object? body, CancellationToken cancellationToken)
    {
        if (!authorization.HasAccessToken || string.IsNullOrWhiteSpace(authorization.AccessToken))
            throw new InvalidOperationException("Sign in with Steam and connect the directory before using account features.");
        if (!Uri.TryCreate(directoryUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The directory must be configured with an HTTPS URL.");
        var endpoint = new Uri(baseUri, path);
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorization.AccessToken);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        try
        {
            var response = await Http.SendAsync(request, cancellationToken);
            request.Dispose();
            return response;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }
}

public sealed record PlayerProfileDto(string SteamId64, string DisplayName, string Biography, DateTime UpdatedAtUtc);
public sealed record UpdatePlayerProfileDto(string DisplayName, string Biography);
public sealed record OwnedServersResponse(List<ServerListing> Servers);
public sealed record CreatedOwnedServerDto(string Id, string Name, string Address, int Port, string ApiKey, string Message);
public sealed record RotatedServerKeyDto(string ApiKey, string Message);
public sealed record RegisterOwnedServerDto(
    string Name, string Description, string Address, int Port, int MaxPlayers,
    string Region, string WebsiteUrl, string DiscordUrl, string RulesUrl, List<string> Tags);
