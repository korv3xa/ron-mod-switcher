using System.Globalization;
using RoNModSwitcher.Core.Models;

namespace RoNModSwitcher.Core.Services;

public sealed class AppController
{
    private readonly PlatformDetector _detector;
    private readonly IniSettingsService _iniService;
    private readonly GameProcessService _processService;
    private readonly ModEngine _modEngine;
    private readonly ProfileService _profileService;
    private readonly ProfileShareService _shareService;
    private readonly DetectionEnvironment? _detectionEnv;
    private readonly string? _publicModIoRootOverride;

    private string? _lastLegacyMigrationNotice;
    private bool _legacyMigrationChecked;

    public AppController(
        PlatformDetector? detector = null,
        IniSettingsService? iniService = null,
        GameProcessService? processService = null,
        ModEngine? modEngine = null,
        ProfileService? profileService = null,
        ProfileShareService? shareService = null,
        DetectionEnvironment? detectionEnv = null,
        string? publicModIoRootOverride = null)
    {
        _detector = detector ?? new PlatformDetector();
        _iniService = iniService ?? new IniSettingsService();
        _processService = processService ?? new GameProcessService();
        _modEngine = modEngine ?? new ModEngine();
        _profileService = profileService ?? new ProfileService();
        _shareService = shareService ?? new ProfileShareService();
        _detectionEnv = detectionEnv;
        _publicModIoRootOverride = publicModIoRootOverride;
    }

    public WorkspaceSnapshot GetSnapshot()
    {
        var settings = _profileService.LoadSettings();
        var detected = _detector.DetectAll(_detectionEnv).ToList();
        var activeInstall = ResolveActiveInstallation(settings, detected);

        ModScanSummary? modSummary = null;
        ChecksumSettings? checksum = null;

        if (activeInstall is not null)
        {
            var paths = ModWorkspacePaths.FromInstallation(activeInstall, _publicModIoRootOverride);

            if (!_legacyMigrationChecked)
            {
                _legacyMigrationChecked = true;
                var migration = _modEngine.DetectAndMigrateLegacy(paths);
                if (migration.Migrated)
                {
                    _lastLegacyMigrationNotice = $"Migrated {migration.MovedItems} legacy mod item(s) to the new RoN Mod Switcher storage.";
                }
            }

            modSummary = _modEngine.Scan(paths);
            checksum = _iniService.ReadChecksumSettings(activeInstall.ConfigIniPath);
        }

        var (profiles, isFirstRun) = _profileService.LoadProfiles();
        if (isFirstRun)
        {
            var seedIds = modSummary?.Mods
                .Where(m => m.IsEnabled)
                .Select(m => m.Id)
                .ToList() ?? new List<string>();
            var seedDisabledIds = modSummary?.Mods
                .Where(m => !m.IsEnabled)
                .Select(m => m.Id)
                .ToList() ?? new List<string>();
            profiles = ProfileService.CreateDefaultProfiles(seedIds, seedDisabledIds);
            _profileService.SaveProfiles(profiles);

            // Detect current real state to pick initial active profile badge
            if (modSummary is not null)
            {
                if (modSummary.WorkspaceMode == WorkspaceMode.Vanilla)
                    settings.ActiveProfileId = "vanilla";
                else if (checksum is { IsCoopModFriendly: true })
                    settings.ActiveProfileId = "coop";
                else
                    settings.ActiveProfileId = "solo";

                _profileService.SaveSettings(settings);
            }
        }

        bool gameRunning = _processService.IsGameRunning(activeInstall?.InstallPath);
        string resolvedLang = ResolveLanguage(settings.Language);

        return new WorkspaceSnapshot
        {
            ResolvedLanguage = resolvedLang,
            GameRunning = gameRunning,
            ActiveInstallation = activeInstall,
            DetectedInstallations = detected,
            ModSummary = modSummary,
            Checksum = checksum,
            Profiles = profiles,
            Settings = settings,
            LegacyMigrationNotice = _lastLegacyMigrationNotice
        };
    }

    public OperationResult DismissMigrationNotice()
    {
        _lastLegacyMigrationNotice = null;
        return OperationResult.Ok("NOTICE_DISMISSED", "Notice dismissed.");
    }

    public OperationResult SetLanguage(string languageCode)
    {
        var settings = _profileService.LoadSettings();
        settings.Language = languageCode is "tr" or "en" or "auto" ? languageCode : "auto";
        _profileService.SaveSettings(settings);
        return OperationResult.Ok("LANGUAGE_UPDATED", $"Language set to {settings.Language}.");
    }

    public OperationResult SelectPlatform(GamePlatform platform)
    {
        var settings = _profileService.LoadSettings();
        settings.PreferredPlatform = platform;
        _profileService.SaveSettings(settings);
        _legacyMigrationChecked = false;
        return OperationResult.Ok("PLATFORM_SELECTED", $"Platform switched to {platform}.");
    }

    public OperationResult SetCustomGamePath(string folderPath)
    {
        var resolved = _detector.ResolveCustomInstallation(folderPath, env: _detectionEnv);
        if (resolved is null)
        {
            return OperationResult.Fail("INVALID_GAME_PATH", "Could not find 'ReadyOrNot\\Content\\Paks' inside the selected folder.");
        }

        var settings = _profileService.LoadSettings();
        settings.CustomGamePath = resolved.InstallPath;
        settings.PreferredPlatform = GamePlatform.Custom;
        _profileService.SaveSettings(settings);
        _legacyMigrationChecked = false;
        return OperationResult.Ok("CUSTOM_PATH_SET", $"Game path set to {resolved.InstallPath}.");
    }

    public OperationResult UpdatePreferences(LaunchMode launchMode, bool minimizeOnLaunch, bool includeModIoInSwitch)
    {
        var settings = _profileService.LoadSettings();
        settings.LaunchMode = launchMode;
        settings.MinimizeOnLaunch = minimizeOnLaunch;
        settings.IncludeModIoInSwitch = includeModIoInSwitch;
        _profileService.SaveSettings(settings);
        return OperationResult.Ok("SETTINGS_SAVED", "Preferences saved.");
    }

    public OperationResult ApplyProfile(string profileId, bool launchGameAfter = false)
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        if (_processService.IsGameRunning(install.InstallPath))
        {
            return OperationResult.Fail("GAME_RUNNING", "Ready or Not is currently running. Close the game before switching mods.");
        }

        var (profiles, _) = _profileService.LoadProfiles();
        var profile = profiles.FirstOrDefault(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return OperationResult.Fail("PROFILE_NOT_FOUND", $"Profile '{profileId}' not found.");
        }

        var settings = _profileService.LoadSettings();
        var paths = ModWorkspacePaths.FromInstallation(install, _publicModIoRootOverride);

        var modResult = _modEngine.ApplyProfile(paths, profile, settings.IncludeModIoInSwitch);
        if (!modResult.Success)
        {
            return modResult;
        }

        var warnings = new List<string>(modResult.Warnings);
        if (install.ConfigExists || File.Exists(install.ConfigIniPath))
        {
            var iniRes = _iniService.WriteChecksumSettings(install.ConfigIniPath, profile.DisableChecksum);
            if (!iniRes.Success)
            {
                warnings.Add(iniRes.Message);
            }
        }
        else
        {
            warnings.Add("GameUserSettings.ini was not found yet (launch the game once to generate it).");
        }

        settings.ActiveProfileId = profile.Id;
        _profileService.SaveSettings(settings);

        if (launchGameAfter)
        {
            var launchRes = _processService.LaunchGame(install, settings.LaunchMode);
            if (!launchRes.Success)
            {
                warnings.Add(launchRes.Message);
            }
        }

        return OperationResult.Ok("PROFILE_APPLIED", modResult.Message, warnings);
    }

    public OperationResult ToggleWorkspaceModded(bool enableModded)
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        if (_processService.IsGameRunning(install.InstallPath))
        {
            return OperationResult.Fail("GAME_RUNNING", "Ready or Not is currently running. Close the game first.");
        }

        var settings = _profileService.LoadSettings();
        var paths = ModWorkspacePaths.FromInstallation(install, _publicModIoRootOverride);
        var res = _modEngine.SetWorkspaceModded(paths, enableModded, settings.IncludeModIoInSwitch);

        if (res.Success)
        {
            if (!enableModded)
            {
                settings.ActiveProfileId = "vanilla";
            }
            else if (settings.ActiveProfileId == "vanilla")
            {
                settings.ActiveProfileId = "solo";
            }
            _profileService.SaveSettings(settings);
        }

        return res;
    }

    public OperationResult ToggleMod(string modId, bool enable)
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        if (_processService.IsGameRunning(install.InstallPath))
        {
            return OperationResult.Fail("GAME_RUNNING", "Ready or Not is currently running. Close the game first.");
        }

        var settings = _profileService.LoadSettings();
        var (profiles, _) = _profileService.LoadProfiles();
        var activeProfile = profiles.FirstOrDefault(p => p.Id.Equals(settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        bool preferVanillaStash = activeProfile is not null && !activeProfile.IsModdedMode;

        var paths = ModWorkspacePaths.FromInstallation(install, _publicModIoRootOverride);
        var res = _modEngine.SetModEnabled(paths, modId, enable, preferVanillaStash);
        if (!res.Success)
        {
            return res;
        }

        if (activeProfile is not null && activeProfile.IsModdedMode)
        {
            _profileService.SyncModStateToProfile(activeProfile, modId, enable);
            _profileService.SaveProfiles(profiles);
        }

        return res;
    }

    public OperationResult SetBulkModsEnabled(IEnumerable<string> modIds, bool enable)
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        if (_processService.IsGameRunning(install.InstallPath))
        {
            return OperationResult.Fail("GAME_RUNNING", "Ready or Not is currently running. Close the game first.");
        }

        var idList = modIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (idList.Count == 0)
        {
            return OperationResult.Ok("MODS_BULK_UNCHANGED", "No matching mods.");
        }

        var settings = _profileService.LoadSettings();
        var (profiles, _) = _profileService.LoadProfiles();
        var activeProfile = profiles.FirstOrDefault(p => p.Id.Equals(settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        bool preferVanillaStash = activeProfile is not null && !activeProfile.IsModdedMode;

        var paths = ModWorkspacePaths.FromInstallation(install, _publicModIoRootOverride);
        var res = _modEngine.SetBulkModsEnabled(paths, idList, enable, preferVanillaStash);
        if (!res.Success)
        {
            return res;
        }

        if (activeProfile is not null && activeProfile.IsModdedMode)
        {
            _profileService.SyncMultipleModsToProfile(activeProfile, idList, enable);
            _profileService.SaveProfiles(profiles);
        }

        return res;
    }

    public OperationResult RestoreIniBackup()
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        if (_processService.IsGameRunning(install.InstallPath))
        {
            return OperationResult.Fail("GAME_RUNNING", "Ready or Not is currently running. Close the game first.");
        }

        return _iniService.RestoreBackup(install.ConfigIniPath);
    }

    public OperationResult SetLaunchModeQuick(LaunchMode mode)
    {
        var settings = _profileService.LoadSettings();
        settings.LaunchMode = mode;
        _profileService.SaveSettings(settings);
        return OperationResult.Ok("LAUNCH_MODE_UPDATED", $"Launch mode set to {mode}.");
    }

    public OperationResult ExportProfileShareText(string profileId)
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        var (profiles, _) = _profileService.LoadProfiles();
        var profile = profiles.FirstOrDefault(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return OperationResult.Fail("PROFILE_NOT_FOUND", "Profile was not found.");
        }

        var paths = ModWorkspacePaths.FromInstallation(install, _publicModIoRootOverride);
        var mods = _modEngine.Scan(paths).Mods;
        var shareText = _shareService.ExportShareText(profile, mods);
        return OperationResult.Ok("SHARE_EXPORTED", "Loadout share text ready.", data: shareText);
    }

    public OperationResult CompareSharedProfile(string rawText)
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        var paths = ModWorkspacePaths.FromInstallation(install, _publicModIoRootOverride);
        var mods = _modEngine.Scan(paths).Mods;
        return _shareService.CompareSharedText(rawText, mods);
    }

    public OperationResult ApplySharedToCoop(string rawText)
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        if (_processService.IsGameRunning(install.InstallPath))
        {
            return OperationResult.Fail("GAME_RUNNING", "Ready or Not is currently running. Close the game first.");
        }

        var paths = ModWorkspacePaths.FromInstallation(install, _publicModIoRootOverride);
        var mods = _modEngine.Scan(paths).Mods;
        var cmpRes = _shareService.CompareSharedText(rawText, mods);
        if (!cmpRes.Success || cmpRes.Data is not ProfileCompareResult cmp)
        {
            return cmpRes;
        }

        var (profiles, _) = _profileService.LoadProfiles();
        var coop = profiles.FirstOrDefault(p => p.Id.Equals("coop", StringComparison.OrdinalIgnoreCase));
        if (coop is null)
        {
            return OperationResult.Fail("PROFILE_NOT_FOUND", "Co-Op profile not found.");
        }

        coop.EnabledModIds = cmp.MatchedLocalModIds.ToList();
        coop.DisableChecksum = cmp.DisableChecksum;
        coop.EnableModIo = cmp.EnableModIo;
        _profileService.SaveProfiles(profiles);

        return ApplyProfile("coop", launchGameAfter: false);
    }

    public OperationResult SetChecksumDisabled(bool disableChecksum)
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        if (_processService.IsGameRunning(install.InstallPath))
        {
            return OperationResult.Fail("GAME_RUNNING", "Ready or Not is currently running. Close the game first.");
        }

        var res = _iniService.WriteChecksumSettings(install.ConfigIniPath, disableChecksum);
        if (res.Success)
        {
            var settings = _profileService.LoadSettings();
            var (profiles, _) = _profileService.LoadProfiles();
            var activeProfile = profiles.FirstOrDefault(p => p.Id.Equals(settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
            if (activeProfile is not null && activeProfile.IsModdedMode)
            {
                activeProfile.DisableChecksum = disableChecksum;
                _profileService.SaveProfiles(profiles);
            }
        }
        return res;
    }

    public OperationResult CreateCustomProfile(string name, string description, bool disableChecksum, bool enableModIo)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("PROFILE_NAME_REQUIRED", "Profile name cannot be empty.");
        }

        var install = ResolveActiveInstallation();
        var enabledIds = new List<string>();
        if (install is not null)
        {
            var paths = ModWorkspacePaths.FromInstallation(install, _publicModIoRootOverride);
            enabledIds = _modEngine.Scan(paths).Mods.Where(m => m.IsEnabled).Select(m => m.Id).ToList();
        }

        var (profiles, _) = _profileService.LoadProfiles();
        var slug = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(slug)) slug = "custom";
        var id = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";

        var created = new ModProfile
        {
            Id = id,
            Name = name.Trim(),
            BuiltInType = BuiltInProfileType.Custom,
            Description = description.Trim(),
            IsModdedMode = true,
            EnableModIo = enableModIo,
            DisableChecksum = disableChecksum,
            TrackNewModsAsEnabled = false,
            EnabledModIds = enabledIds
        };

        profiles.Add(created);
        _profileService.SaveProfiles(profiles);

        var settings = _profileService.LoadSettings();
        settings.ActiveProfileId = created.Id;
        _profileService.SaveSettings(settings);

        return OperationResult.Ok("PROFILE_CREATED", $"Profile '{created.Name}' created.");
    }

    public OperationResult DeleteProfile(string profileId)
    {
        var (profiles, _) = _profileService.LoadProfiles();
        var target = profiles.FirstOrDefault(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return OperationResult.Fail("PROFILE_NOT_FOUND", "Profile was not found.");
        }

        if (target.IsBuiltIn)
        {
            return OperationResult.Fail("CANNOT_DELETE_BUILTIN", "Built-in profiles cannot be deleted.");
        }

        profiles.Remove(target);
        _profileService.SaveProfiles(profiles);

        var settings = _profileService.LoadSettings();
        if (string.Equals(settings.ActiveProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            settings.ActiveProfileId = "solo";
            _profileService.SaveSettings(settings);
        }

        return OperationResult.Ok("PROFILE_DELETED", $"Deleted profile '{target.Name}'.");
    }

    public OperationResult SaveCurrentSelectionToProfile(string profileId)
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        var (profiles, _) = _profileService.LoadProfiles();
        var profile = profiles.FirstOrDefault(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return OperationResult.Fail("PROFILE_NOT_FOUND", "Profile was not found.");
        }

        if (!profile.IsModdedMode)
        {
            return OperationResult.Fail("PROFILE_READONLY", "Vanilla profile always keeps all mods disabled.");
        }

        var paths = ModWorkspacePaths.FromInstallation(install, _publicModIoRootOverride);
        var scan = _modEngine.Scan(paths);

        if (profile.TrackNewModsAsEnabled)
        {
            profile.DisabledModIds = scan.Mods.Where(m => !m.IsEnabled).Select(m => m.Id).ToList();
        }
        else
        {
            profile.EnabledModIds = scan.Mods.Where(m => m.IsEnabled).Select(m => m.Id).ToList();
        }

        _profileService.SaveProfiles(profiles);
        return OperationResult.Ok("PROFILE_SYNCED", $"Saved current mod selection to '{profile.Name}'.");
    }

    public OperationResult InstallModFiles(IEnumerable<string> inputPaths)
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        if (_processService.IsGameRunning(install.InstallPath))
        {
            return OperationResult.Fail("GAME_RUNNING", "Ready or Not is currently running. Close the game first.");
        }

        var settings = _profileService.LoadSettings();
        var (profiles, _) = _profileService.LoadProfiles();
        var activeProfile = profiles.FirstOrDefault(p => p.Id.Equals(settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        bool preferVanillaStash = activeProfile is not null && !activeProfile.IsModdedMode;

        var paths = ModWorkspacePaths.FromInstallation(install, _publicModIoRootOverride);
        var (res, addedIds) = _modEngine.InstallModFiles(paths, inputPaths, preferVanillaStash);

        if (res.Success && addedIds.Count > 0)
        {
            if (activeProfile is not null && activeProfile.IsModdedMode)
            {
                _profileService.SyncMultipleModsToProfile(activeProfile, addedIds, true);
                _profileService.SaveProfiles(profiles);
            }
        }

        return res;
    }

    public OperationResult DeleteMod(string modId, bool sendToRecycleBin = true)
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        if (_processService.IsGameRunning(install.InstallPath))
        {
            return OperationResult.Fail("GAME_RUNNING", "Ready or Not is currently running. Close the game first.");
        }

        var paths = ModWorkspacePaths.FromInstallation(install, _publicModIoRootOverride);
        var res = _modEngine.DeleteMod(paths, modId, sendToRecycleBin);

        if (res.Success)
        {
            var (profiles, _) = _profileService.LoadProfiles();
            foreach (var p in profiles)
            {
                p.EnabledModIds.RemoveAll(id => id.Equals(modId, StringComparison.OrdinalIgnoreCase));
                p.DisabledModIds.RemoveAll(id => id.Equals(modId, StringComparison.OrdinalIgnoreCase));
            }
            _profileService.SaveProfiles(profiles);
        }

        return res;
    }

    public OperationResult LaunchGame()
    {
        var install = ResolveActiveInstallation();
        if (install is null)
        {
            return OperationResult.Fail("NO_INSTALLATION", "Ready or Not installation folder is not configured.");
        }

        var settings = _profileService.LoadSettings();
        return _processService.LaunchGame(install, settings.LaunchMode);
    }

    private GameInstallation? ResolveActiveInstallation()
    {
        var settings = _profileService.LoadSettings();
        var detected = _detector.DetectAll(_detectionEnv).ToList();
        return ResolveActiveInstallation(settings, detected);
    }

    private GameInstallation? ResolveActiveInstallation(AppSettings settings, List<GameInstallation> detected)
    {
        if (settings.PreferredPlatform == GamePlatform.Custom && !string.IsNullOrWhiteSpace(settings.CustomGamePath))
        {
            var custom = _detector.ResolveCustomInstallation(settings.CustomGamePath, GamePlatform.Custom, _detectionEnv);
            if (custom is not null) return custom;
        }

        if (settings.PreferredPlatform is GamePlatform.Steam or GamePlatform.Epic)
        {
            var preferred = detected.FirstOrDefault(d => d.Platform == settings.PreferredPlatform);
            if (preferred is not null) return preferred;
        }

        if (!string.IsNullOrWhiteSpace(settings.CustomGamePath))
        {
            var customFallback = _detector.ResolveCustomInstallation(settings.CustomGamePath, env: _detectionEnv);
            if (customFallback is not null) return customFallback;
        }

        return detected.FirstOrDefault();
    }

    private static string ResolveLanguage(string configured)
    {
        if (configured is "tr" or "en") return configured;
        var iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return string.Equals(iso, "tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";
    }
}
