using System.Text.Json;
using SE.Roleplay.Framework.Launcher.Models;
namespace SE.Roleplay.Framework.Launcher.Services;
public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SERoleplayLauncher", "settings.json");
    public async Task<LauncherSettings> LoadAsync()
    {
        if (!File.Exists(_path)) return new LauncherSettings();
        try { await using var stream = File.OpenRead(_path); return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream) ?? new LauncherSettings(); }
        catch (JsonException) { return new LauncherSettings(); }
        catch (IOException) { return new LauncherSettings(); }
    }
    public async Task SaveAsync(LauncherSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true });
        File.Move(temp, _path, true);
    }
}
