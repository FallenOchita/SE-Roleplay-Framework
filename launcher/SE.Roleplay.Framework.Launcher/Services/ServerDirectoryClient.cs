using System.Text.Json;
using SE.Roleplay.Framework.Launcher.Models;
namespace SE.Roleplay.Framework.Launcher.Services;
public sealed class ServerDirectoryClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    public async Task<IReadOnlyList<ServerListing>> GetServersAsync(string directoryUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(directoryUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("Enter a valid HTTP or HTTPS server-directory URL in Settings.");
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            throw new InvalidOperationException("Remote server directories must use HTTPS.");
        using var response = await Http.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var doc = await JsonSerializer.DeserializeAsync<ServerDirectoryDocument>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        return (doc?.Servers ?? new List<ServerListing>()).Where(s => !string.IsNullOrWhiteSpace(s.Name)).Take(200).ToList();
    }
}
