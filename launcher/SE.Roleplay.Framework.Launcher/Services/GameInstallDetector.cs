using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SE.Roleplay.Framework.Launcher.Services;

public sealed class GameInstallDetector
{
    private const string GameRelativePath = "steamapps/common/SpaceEngineers/Bin64/SpaceEngineers.exe";
    private static readonly Regex LibraryPathPattern = new(
        "\\"path\\"\\s*\\"((?:\\\\\\\\.|[^\\"])*)\\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string? FindGameExecutable()
    {
        return FindGameInstallations().FirstOrDefault();
    }

    public IReadOnlyList<string> FindGameInstallations()
    {
        var installations = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var steamRoot in GetSteamRoots())
        {
            AddCandidate(Path.Combine(steamRoot, GameRelativePath.Replace('/', Path.DirectorySeparatorChar)));

            var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
                continue;

            try
            {
                var contents = File.ReadAllText(libraryFile);
                foreach (Match match in LibraryPathPattern.Matches(contents))
                {
                    var libraryPath = match.Groups[1].Value
                        .Replace(@"\\\\", @"\\", StringComparison.Ordinal)
                        
                    if (string.IsNullOrWhiteSpace(libraryPath))
                        continue;

                    AddCandidate(Path.Combine(libraryPath, GameRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                }
            }
            catch (IOException)
            {
                // An unreadable Steam library must not prevent detection of other libraries.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue scanning libraries that are accessible to the current user.
            }
        }

        return installations;
        
        void AddCandidate(string candidate)
        {
            if (!File.Exists(candidate) || !string.Equals(Path.GetFileName(candidate), "SpaceEngineers.exe", StringComparison.OrdinalIgnoreCase))
                return;

            var fullPath = Path.GetFullPath(candidate);
            if (seen.Add(fullPath))
                installations.Add(fullPath);
        }
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyPath in new[]
        {
            @"HKEY_CURRENT_USER\\Software\\Valve\\Steam",
            @"HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Valve\\Steam",
            @"HKEY_LOCAL_MACHINE\\SOFTWARE\\Valve\\Steam"
        })
        {
            foreach (var valueName in new[] { "SteamPath", "InstallPath" })
            {
                try
                {
                    if (Registry.GetValue(keyPath, valueName, null) is string path && !string.IsNullOrWhiteSpace(path))
                        roots.Add(Environment.ExpandEnvironmentVariables(path));
                }
                catch (System.Security.SecurityException)
                {
                    // Registry access can be restricted; other discovery sources remain available.
                }
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFiles))
            roots.Add(Path.Combine(programFiles, "Steam"));

        var programFiles64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles64))
            roots.Add(Path.Combine(programFiles64, "Steam"));

        return roots.Where(Directory.Exists);
    }
}
