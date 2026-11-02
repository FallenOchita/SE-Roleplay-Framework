using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace SE.Roleplay.Framework.Launcher.Services;

/// <summary>Checks the project's GitHub development release and installs verified launcher updates.</summary>
public sealed class LauncherUpdateService
{
    private const string ReleaseApiUrl = "https://api.github.com/repos/FallenOchita/SE-Roleplay-Framework/releases/tags/launcher-dev";
    private const string InstallerName = "SE1-RP-Launcher-Setup-0.2.0-x64.exe";
    private static readonly HttpClient Client = CreateClient();

    public async Task<bool> CheckAndStartUpdateAsync(Window owner)
    {
        try
        {
            using var response = await Client.GetAsync(ReleaseApiUrl);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return false;
            response.EnsureSuccessStatusCode();

            using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = release.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                return false;
            if (root.TryGetProperty("prerelease", out var prerelease) && !prerelease.GetBoolean())
                return false;

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return false;

            var manifestUrl = FindAssetUrl(assets, "launcher-update.json");
            if (manifestUrl is null)
                return false;

            using var manifest = JsonDocument.Parse(await Client.GetStringAsync(manifestUrl));
            var latestCommit = manifest.RootElement.TryGetProperty("commit", out var commit)
                ? commit.GetString() ?? string.Empty : string.Empty;
            var installedCommit = GetInstalledCommit();
            if (string.IsNullOrWhiteSpace(latestCommit))
                return false;

            var hasInstalledCommit = installedCommit.Length == 40 && installedCommit.All(Uri.IsHexDigit);
            if (hasInstalledCommit)
            {
                if (string.Equals(latestCommit, installedCommit, StringComparison.OrdinalIgnoreCase))
                    return false;

                // Only offer a build that is ahead of the installed commit; never downgrade a newer local build.
                var compareUrl = "https://api.github.com/repos/FallenOchita/SE-Roleplay-Framework/compare/" +
                                 Uri.EscapeDataString(installedCommit) + "..." + Uri.EscapeDataString(latestCommit);
                using var compareResponse = await Client.GetAsync(compareUrl);
                if (!compareResponse.IsSuccessStatusCode)
                    return false;
                using var comparison = JsonDocument.Parse(await compareResponse.Content.ReadAsStringAsync());
                var compareStatus = comparison.RootElement.TryGetProperty("status", out var status)
                    ? status.GetString() : null;
                if (!string.Equals(compareStatus, "ahead", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            // Legacy 0.2.0 installs have no embedded commit yet; offer the first metadata-enabled build.

            var installerUrl = FindAssetUrl(assets, InstallerName);
            var checksumUrl = FindAssetUrl(assets, InstallerName + ".sha256");
            if (installerUrl is null || checksumUrl is null)
                return false;

            var releaseName = root.TryGetProperty("name", out var name) ? name.GetString() : null;
            var prompt = $"A launcher update is available{(string.IsNullOrWhiteSpace(releaseName) ? string.Empty : $": {releaseName}")}.\n\nInstall the latest GitHub build now? The launcher will close during installation.";
            if (MessageBox.Show(owner, prompt, "SE1 Roleplay Launcher Update",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return false;

            var tempDirectory = Path.Combine(Path.GetTempPath(), "SE1RoleplayLauncherUpdate");
            Directory.CreateDirectory(tempDirectory);
            var installerPath = Path.Combine(tempDirectory, InstallerName);
            var checksumPath = Path.Combine(tempDirectory, InstallerName + ".sha256");

            await DownloadAssetAsync(installerUrl, installerPath);
            await DownloadAssetAsync(checksumUrl, checksumPath);
            VerifyChecksum(installerPath, checksumPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /SUPPRESSMSGBOXES",
                UseShellExecute = true
            };
            Process.Start(startInfo);
            Application.Current.Shutdown();
            return true;
        }
        catch (HttpRequestException)
        {
            // Network and GitHub rate-limit failures must not prevent the launcher from opening.
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            MessageBox.Show(owner,
                $"The launcher could not install the update. You can keep using the current version.\n\n{exception.Message}",
                "Launcher update unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SE1-Roleplay-Launcher-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static string GetInstalledCommit()
    {
        var metadata = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "LauncherCommit");
        return metadata?.Value ?? string.Empty;
    }

    private static string? FindAssetUrl(JsonElement assets, string fileName)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name) ||
                !string.Equals(name.GetString(), fileName, StringComparison.Ordinal))
                continue;
            if (!asset.TryGetProperty("browser_download_url", out var url) ||
                !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsed) ||
                parsed.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(parsed.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                return null;
            return parsed.ToString();
        }
        return null;
    }

    private static async Task DownloadAssetAsync(string url, string destination)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output);
    }

    private static void VerifyChecksum(string installerPath, string checksumPath)
    {
        var line = File.ReadLines(checksumPath).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(line))
            throw new InvalidDataException("The GitHub release checksum file is empty.");

        var expected = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidDataException("The GitHub release checksum is not a valid SHA-256 hash.");

        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installerPath))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected), Convert.FromHexString(actual)))
            throw new InvalidDataException("The downloaded installer failed SHA-256 verification and will not be run.");
    }
}
