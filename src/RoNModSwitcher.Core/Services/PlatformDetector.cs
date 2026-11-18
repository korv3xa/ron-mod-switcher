using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RoNModSwitcher.Core.Models;

namespace RoNModSwitcher.Core.Services;

public sealed record DetectionEnvironment
{
    public string LocalAppData { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string EpicManifestsDir { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");
    public IReadOnlyList<string> ExplicitSteamPaths { get; init; } = Array.Empty<string>();
    public bool QueryWindowsRegistry { get; init; } = true;
}

public sealed class PlatformDetector
{
    public const string SteamAppId = "1144200";
    private static readonly Regex SteamLibraryPathRegex = new("\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SteamInstallDirRegex = new("\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<GameInstallation> DetectAll(DetectionEnvironment? env = null)
    {
        env ??= new DetectionEnvironment();
        var results = new List<GameInstallation>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var epic in DetectEpic(env))
        {
            var norm = NormalizePath(epic.InstallPath);
            if (seenRoots.Add(norm))
            {
                results.Add(epic);
            }
        }

        foreach (var steam in DetectSteam(env))
        {
            var norm = NormalizePath(steam.InstallPath);
            if (seenRoots.Add(norm))
            {
                results.Add(steam);
            }
        }

        return results;
    }

    public GameInstallation? ResolveCustomInstallation(string selectedPath, GamePlatform? forcedPlatform = null, DetectionEnvironment? env = null)
    {
        env ??= new DetectionEnvironment();
        var root = FindGameRootFromAnySubpath(selectedPath);
        if (root is null)
        {
            return null;
        }

        var isEpic = forcedPlatform == GamePlatform.Epic ||
                     (forcedPlatform != GamePlatform.Steam && DetectIfRootIsEpic(root));

        var platform = forcedPlatform is GamePlatform.Steam or GamePlatform.Epic
            ? forcedPlatform.Value
            : (isEpic ? GamePlatform.Epic : GamePlatform.Custom);

        return BuildInstallation(
            platform: platform,
            installRoot: root,
            launchUri: platform == GamePlatform.Steam ? $"steam://rungameid/{SteamAppId}" : null,
            isAutoDetected: false,
            env: env);
    }

    public static string? FindGameRootFromAnySubpath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        string current;
        try
        {
            current = Path.GetFullPath(rawPath.Trim().Trim('"'));
        }
        catch
        {
            return null;
        }

        if (File.Exists(current))
        {
            current = Path.GetDirectoryName(current) ?? string.Empty;
        }

        if (!Directory.Exists(current))
        {
            return null;
        }

        // Check if current or up to 5 parent levels is the game root
        var dir = new DirectoryInfo(current);
        for (int i = 0; i < 6 && dir is not null; i++)
        {
            if (IsValidGameRoot(dir.FullName))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return null;
    }

    public static bool IsValidGameRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        var paks = Path.Combine(path, "ReadyOrNot", "Content", "Paks");
        return Directory.Exists(paks);
    }

    private static IEnumerable<GameInstallation> DetectEpic(DetectionEnvironment env)
    {
        if (!Directory.Exists(env.EpicManifestsDir))
        {
            yield break;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(env.EpicManifestsDir, "*.item", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            GameInstallation? found = null;
            try
            {
                using var stream = File.OpenRead(file);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                var displayName = GetJsonString(root, "DisplayName");
                var launchExe = GetJsonString(root, "LaunchExecutable");
                var installLocation = GetJsonString(root, "InstallLocation");

                var matchesName = string.Equals(displayName, "Ready or Not", StringComparison.OrdinalIgnoreCase);
                var matchesExe = !string.IsNullOrWhiteSpace(launchExe) &&
                                 launchExe.Contains("ReadyOrNotEGS", StringComparison.OrdinalIgnoreCase);

                if ((!matchesName && !matchesExe) || string.IsNullOrWhiteSpace(installLocation))
                {
                    continue;
                }

                if (!IsValidGameRoot(installLocation))
                {
                    continue;
                }

                var ns = GetJsonString(root, "CatalogNamespace");
                var itemId = GetJsonString(root, "CatalogItemId");
                var appName = GetJsonString(root, "AppName");

                string? epicUri = null;
                if (!string.IsNullOrWhiteSpace(ns) && !string.IsNullOrWhiteSpace(itemId) && !string.IsNullOrWhiteSpace(appName))
                {
                    epicUri = $"com.epicgames.launcher://apps/{Uri.EscapeDataString(ns)}%3A{Uri.EscapeDataString(itemId)}%3A{Uri.EscapeDataString(appName)}?action=launch&silent=true";
                }

                found = BuildInstallation(
                    platform: GamePlatform.Epic,
                    installRoot: installLocation,
                    launchUri: epicUri,
                    isAutoDetected: true,
                    env: env);
            }
            catch
            {
                // Ignore malformed manifest files
            }

            if (found is not null)
            {
                yield return found;
            }
        }
    }

    private static IEnumerable<GameInstallation> DetectSteam(DetectionEnvironment env)
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in env.ExplicitSteamPaths)
        {
            if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
            {
                steamRoots.Add(NormalizePath(p));
            }
        }

        if (env.QueryWindowsRegistry)
        {
            foreach (var regPath in ReadSteamPathsFromRegistry())
            {
                if (Directory.Exists(regPath))
                {
                    steamRoots.Add(NormalizePath(regPath));
                }
            }
        }

        var libraryFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in steamRoots)
        {
            libraryFolders.Add(steamRoot);
            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(vdfPath);
                foreach (Match match in SteamLibraryPathRegex.Matches(content))
                {
                    var libPath = match.Groups[1].Value.Replace("\\\\", "\\").Trim();
                    if (!string.IsNullOrWhiteSpace(libPath) && Directory.Exists(libPath))
                    {
                        libraryFolders.Add(NormalizePath(libPath));
                    }
                }
            }
            catch
            {
                // Ignore unreadable VDF
            }
        }

        foreach (var lib in libraryFolders)
        {
            var manifestPath = Path.Combine(lib, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            GameInstallation? found = null;
            try
            {
                var acf = File.ReadAllText(manifestPath);
                var match = SteamInstallDirRegex.Match(acf);
                var installDir = match.Success ? match.Groups[1].Value.Trim() : "Ready Or Not";
                var gameRoot = Path.Combine(lib, "steamapps", "common", installDir);

                if (IsValidGameRoot(gameRoot))
                {
                    found = BuildInstallation(
                        platform: GamePlatform.Steam,
                        installRoot: gameRoot,
                        launchUri: $"steam://rungameid/{SteamAppId}",
                        isAutoDetected: true,
                        env: env);
                }
            }
            catch
            {
                // Ignore invalid ACF
            }

            if (found is not null)
            {
                yield return found;
            }
        }
    }

    private static IEnumerable<string> ReadSteamPathsFromRegistry()
    {
        var candidates = new List<string>();
        try
        {
            using var hkcu = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var p = hkcu?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(p)) candidates.Add(p);
        }
        catch { }

        try
        {
            using var hklmWow = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            var p = hklmWow?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(p)) candidates.Add(p);
        }
        catch { }

        try
        {
            using var hklm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            var p = hklm?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(p)) candidates.Add(p);
        }
        catch { }

        return candidates.Select(c => c.Replace('/', '\\'));
    }

    private static GameInstallation BuildInstallation(
        GamePlatform platform,
        string installRoot,
        string? launchUri,
        bool isAutoDetected,
        DetectionEnvironment env)
    {
        var fullRoot = NormalizePath(installRoot);
        var contentPath = Path.Combine(fullRoot, "ReadyOrNot", "Content");
        var paksPath = Path.Combine(contentPath, "Paks");
        var exePath = FindExecutable(fullRoot, platform);
        var (configPath, configExists) = ResolveConfigIniPath(platform, fullRoot, env.LocalAppData);

        var label = platform switch
        {
            GamePlatform.Steam => "Steam",
            GamePlatform.Epic => "Epic Games",
            GamePlatform.Custom => DetectIfRootIsEpic(fullRoot) ? "Custom (Epic)" : "Custom",
            _ => "Unknown"
        };

        return new GameInstallation
        {
            Platform = platform,
            InstallPath = fullRoot,
            ContentPath = contentPath,
            PaksPath = paksPath,
            ExecutablePath = exePath,
            ConfigIniPath = configPath,
            ConfigExists = configExists,
            LaunchUri = launchUri,
            IsAutoDetected = isAutoDetected,
            DisplayLabel = label
        };
    }

    private static bool DetectIfRootIsEpic(string root)
    {
        return File.Exists(Path.Combine(root, "ReadyOrNotEGS.exe")) ||
               File.Exists(Path.Combine(root, "ReadyOrNot", "Binaries", "Win64", "ReadyOrNotEGS-Win64-Shipping.exe")) ||
               Directory.Exists(Path.Combine(root, ".egstore"));
    }

    private static string? FindExecutable(string root, GamePlatform platform)
    {
        var win64 = Path.Combine(root, "ReadyOrNot", "Binaries", "Win64");
        var candidates = platform == GamePlatform.Epic
            ? new[]
            {
                Path.Combine(win64, "ReadyOrNotEGS-Win64-Shipping.exe"),
                Path.Combine(root, "ReadyOrNotEGS.exe"),
                Path.Combine(win64, "ReadyOrNot-Win64-Shipping.exe"),
                Path.Combine(root, "ReadyOrNot.exe")
            }
            : new[]
            {
                Path.Combine(win64, "ReadyOrNotSteam-Win64-Shipping.exe"),
                Path.Combine(win64, "ReadyOrNot-Win64-Shipping.exe"),
                Path.Combine(root, "ReadyOrNot.exe"),
                Path.Combine(win64, "ReadyOrNotEGS-Win64-Shipping.exe"),
                Path.Combine(root, "ReadyOrNotEGS.exe")
            };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static (string Path, bool Exists) ResolveConfigIniPath(GamePlatform platform, string installRoot, string localAppData)
    {
        var isEgs = platform == GamePlatform.Epic || (platform == GamePlatform.Custom && DetectIfRootIsEpic(installRoot));
        var primaryFolder = isEgs ? "ReadyOrNotEGS" : "ReadyOrNot";

        var candidates = new List<string>
        {
            Path.Combine(localAppData, primaryFolder, "Saved", "Config", "Windows", "GameUserSettings.ini"),
            Path.Combine(localAppData, primaryFolder, "Saved", "Config", "WindowsNoEditor", "GameUserSettings.ini")
        };

        if (platform == GamePlatform.Custom && !isEgs)
        {
            candidates.Add(Path.Combine(localAppData, "ReadyOrNotEGS", "Saved", "Config", "Windows", "GameUserSettings.ini"));
            candidates.Add(Path.Combine(localAppData, "ReadyOrNotEGS", "Saved", "Config", "WindowsNoEditor", "GameUserSettings.ini"));
        }

        var existing = candidates.FirstOrDefault(File.Exists);
        return existing is not null
            ? (existing, true)
            : (candidates[0], false);
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path.Trim().TrimEnd('\\', '/'));
}
