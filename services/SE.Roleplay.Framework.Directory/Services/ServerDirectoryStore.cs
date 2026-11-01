using System.Security.Cryptography;
using System.Text.Json;
using SE.Roleplay.Framework.Directory.Models;

namespace SE.Roleplay.Framework.Directory.Services;

public sealed class ServerDirectoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;

    public ServerDirectoryStore(IConfiguration configuration)
    {
        _filePath = configuration["Directory:DataPath"]
            ?? Environment.GetEnvironmentVariable("SERPF_DIRECTORY_DATA_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "data", "servers.json");
    }

    public async Task<IReadOnlyList<DirectoryServer>> GetPublicServersAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var servers = await ReadUnsafeAsync(cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var server in servers)
            {
                server.Status = now - server.LastHeartbeatUtc <= TimeSpan.FromSeconds(90) ? "Online" : "Offline";
                server.ApiKeyHash = string.Empty;
            }
            return servers;
        }
        finally { _gate.Release(); }
    }

    public async Task<(DirectoryServer Server, string ApiKey)> RegisterAsync(RegisterServerRequest request, CancellationToken cancellationToken)
    {
        var apiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var server = new DirectoryServer
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            Address = request.Address.Trim(),
            Port = request.Port,
            MaxPlayers = request.MaxPlayers,
            Region = request.Region.Trim(),
            WebsiteUrl = request.WebsiteUrl.Trim(),
            DiscordUrl = request.DiscordUrl.Trim(),
            RulesUrl = request.RulesUrl.Trim(),
            Tags = request.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList(),
            ApiKeyHash = HashKey(apiKey),
            LastHeartbeatUtc = DateTime.MinValue
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var servers = await ReadUnsafeAsync(cancellationToken);
            servers.Add(server);
            await WriteUnsafeAsync(servers, cancellationToken);
        }
        finally { _gate.Release(); }
        return (server, apiKey);
    }

    public async Task<bool> HeartbeatAsync(string id, string apiKey, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var servers = await ReadUnsafeAsync(cancellationToken);
            var server = servers.FirstOrDefault(item => item.Id == id);
            if (server == null || !VerifyKey(apiKey, server.ApiKeyHash)) return false;
            server.MaxPlayers = request.MaxPlayers > 0 ? Math.Min(request.MaxPlayers, 10000) : server.MaxPlayers;
            server.Players = Math.Clamp(request.Players, 0, server.MaxPlayers);
            server.Version = request.Version?.Trim() ?? string.Empty;
            server.LastHeartbeatUtc = DateTime.UtcNow;
            await WriteUnsafeAsync(servers, cancellationToken);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var servers = await ReadUnsafeAsync(cancellationToken);
            var removed = servers.RemoveAll(server => server.Id == id) > 0;
            if (removed) await WriteUnsafeAsync(servers, cancellationToken);
            return removed;
        }
        finally { _gate.Release(); }
    }

    public static string HashKey(string key) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));

    public static bool VerifyKey(string provided, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(provided) || string.IsNullOrWhiteSpace(expectedHash)) return false;
        var actual = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(provided));
        byte[] expected;
        try { expected = Convert.FromHexString(expectedHash); }
        catch (FormatException) { return false; }
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private async Task<List<DirectoryServer>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return new List<DirectoryServer>();
        await using var stream = File.OpenRead(_filePath);
        var document = await JsonSerializer.DeserializeAsync<DirectoryDocument>(stream, JsonOptions, cancellationToken);
        return document?.Servers ?? new List<DirectoryServer>();
    }

    private async Task WriteUnsafeAsync(List<DirectoryServer> servers, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var tempPath = _filePath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, new DirectoryDocument { Servers = servers }, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
