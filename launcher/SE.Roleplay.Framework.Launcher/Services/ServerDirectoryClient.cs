using System.Net;
using System.Net.Http;
using System.Text.Json;
using SE.Roleplay.Framework.Launcher.Models;

namespace SE.Roleplay.Framework.Launcher.Services;

public sealed class ServerDirectoryClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task<IReadOnlyList<ServerListing>> GetServersAsync(string directoryUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(directoryUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("Enter a valid HTTP or HTTPS server-directory URL in Settings.");
        if (uri.Scheme == Uri.UriSchemeHttp && !IPAddress.IsLoopback(uri.Host) &&
            !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Remote server directories must use HTTPS.");

        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > 2_000_000)
            throw new InvalidOperationException("The server directory response is too large.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var doc = await JsonSerializer.DeserializeAsync<ServerDirectoryDocument>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (doc?.Servers ?? new List<ServerListing>())
            .Where(IsValidListing)
            .Where(server => string.IsNullOrWhiteSpace(server.Id) || seenIds.Add(server.Id))
            .Take(200)
            .ToList();
    }

    private static bool IsValidListing(ServerListing server)
    {
        if (server == null || string.IsNullOrWhiteSpace(server.Name) ||
            string.IsNullOrWhiteSpace(server.Address) || server.Port is < 1 or > 65535)
            return false;

        var host = server.Address.Trim();
        var validHost = IPAddress.TryParse(host, out _) ||
            (Uri.CheckHostName(host) == UriHostNameType.Dns && host.Length <= 253 &&
             !host.Contains('/') && !host.Contains('\\') && !host.Contains(':'));
        if (!validHost) return false;

        server.Name = server.Name.Trim();
        server.Description = server.Description?.Trim() ?? string.Empty;
        server.Region = server.Region?.Trim() ?? string.Empty;
        server.Status = server.Status?.Trim() ?? "Unknown";
        server.Version = server.Version?.Trim() ?? string.Empty;
        server.Players = Math.Max(0, server.Players);
        server.MaxPlayers = Math.Max(0, server.MaxPlayers);
        if (server.MaxPlayers > 0) server.Players = Math.Min(server.Players, server.MaxPlayers);
        server.Tags ??= new List<string>();
        server.Tags = server.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).Take(20).ToList();
        return true;
    }
}
