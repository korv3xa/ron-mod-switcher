using System.Text.Json;
using RoNModSwitcher.Core.Models;

namespace RoNModSwitcher.Core.Services;

public sealed class ProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _storageDir;
    private readonly string _settingsPath;
    private readonly string _profilesPath;

    public ProfileService(string? storageDirOverride = null)
    {
        _storageDir = string.IsNullOrWhiteSpace(storageDirOverride)
            ? ResolveDefaultStorageDir()
            : storageDirOverride;

        _settingsPath = Path.Combine(_storageDir, "settings.json");
        _profilesPath = Path.Combine(_storageDir, "profiles.json");
    }

    public string StorageDirectory => _storageDir;

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to defaults on corrupted settings file
        }

        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(_storageDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public (List<ModProfile> Profiles, bool IsFirstRun) LoadProfiles()
    {
        try
        {
            if (File.Exists(_profilesPath))
            {
                var json = File.ReadAllText(_profilesPath);
                var loaded = JsonSerializer.Deserialize<List<ModProfile>>(json, JsonOptions);
                if (loaded is { Count: > 0 })
                {
                    EnsureBuiltInsPresent(loaded);
                    return (loaded, false);
                }
            }
        }
        catch
        {
            // Recreate defaults on parse error
        }

        var defaults = CreateDefaultProfiles();
        return (defaults, true);
    }

    public void SaveProfiles(IEnumerable<ModProfile> profiles)
    {
        Directory.CreateDirectory(_storageDir);
        var json = JsonSerializer.Serialize(profiles.ToList(), JsonOptions);
        File.WriteAllText(_profilesPath, json);
    }

    public static List<ModProfile> CreateDefaultProfiles(
        IEnumerable<string>? seedEnabledModIds = null,
        IEnumerable<string>? seedDisabledModIds = null)
    {
        var seed = seedEnabledModIds?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        var seedDisabled = seedDisabledModIds?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        return new List<ModProfile>
        {
            new()
            {
                Id = "coop",
                Name = "Co-Op",
                BuiltInType = BuiltInProfileType.Coop,
                Description = "Modded friend lobby profile with server checksum disabled.",
                IsModdedMode = true,
                EnableModIo = true,
                DisableChecksum = true,
                TrackNewModsAsEnabled = false,
                EnabledModIds = new List<string>(seed),
                DisabledModIds = new List<string>()
            },
            new()
            {
                Id = "solo",
                Name = "Solo",
                BuiltInType = BuiltInProfileType.Solo,
                Description = "Full single-player modded experience with all mods enabled.",
                IsModdedMode = true,
                EnableModIo = true,
                DisableChecksum = true,
                TrackNewModsAsEnabled = true,
                EnabledModIds = new List<string>(),
                DisabledModIds = new List<string>(seedDisabled)
            },
            new()
            {
                Id = "vanilla",
                Name = "Vanilla",
                BuiltInType = BuiltInProfileType.Vanilla,
                Description = "Clean online matchmaking mode with all mods safely stashed.",
                IsModdedMode = false,
                EnableModIo = false,
                DisableChecksum = false,
                TrackNewModsAsEnabled = false,
                EnabledModIds = new List<string>(),
                DisabledModIds = new List<string>()
            }
        };
    }

    public void SyncModStateToProfile(ModProfile profile, string modId, bool enabled)
    {
        if (!profile.IsModdedMode) return;

        if (profile.TrackNewModsAsEnabled)
        {
            if (enabled)
            {
                profile.DisabledModIds.RemoveAll(id => id.Equals(modId, StringComparison.OrdinalIgnoreCase));
            }
            else if (!profile.DisabledModIds.Any(id => id.Equals(modId, StringComparison.OrdinalIgnoreCase)))
            {
                profile.DisabledModIds.Add(modId);
            }
        }
        else
        {
            if (enabled)
            {
                if (!profile.EnabledModIds.Any(id => id.Equals(modId, StringComparison.OrdinalIgnoreCase)))
                {
                    profile.EnabledModIds.Add(modId);
                }
            }
            else
            {
                profile.EnabledModIds.RemoveAll(id => id.Equals(modId, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    public void SyncMultipleModsToProfile(ModProfile profile, IEnumerable<string> enabledIds, bool enabled)
    {
        foreach (var id in enabledIds)
        {
            SyncModStateToProfile(profile, id, enabled);
        }
    }

    private static void EnsureBuiltInsPresent(List<ModProfile> profiles)
    {
        var defaults = CreateDefaultProfiles();
        foreach (var def in defaults)
        {
            if (!profiles.Any(p => p.Id.Equals(def.Id, StringComparison.OrdinalIgnoreCase)))
            {
                profiles.Add(def);
            }
        }
    }

    private static string ResolveDefaultStorageDir()
    {
        var baseDir = AppContext.BaseDirectory;
        var portableConfig = Path.Combine(baseDir, "config");
        var portableFlag = Path.Combine(baseDir, "portable.txt");
        if (File.Exists(portableFlag) || Directory.Exists(portableConfig))
        {
            return portableConfig;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VosienStudio",
            "RoNModSwitcher");
    }
}
