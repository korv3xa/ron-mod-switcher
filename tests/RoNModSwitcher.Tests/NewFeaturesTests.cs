using RoNModSwitcher.Core.Models;
using RoNModSwitcher.Core.Services;

namespace RoNModSwitcher.Tests;

public sealed class NewFeaturesTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "RoNTests_New_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private (ModWorkspacePaths Paths, GameInstallation Install) CreateFakeGameWorkspace()
    {
        var gameRoot = Path.Combine(_tempRoot, "SteamLib", "steamapps", "common", "Ready Or Not");
        var content = Path.Combine(gameRoot, "ReadyOrNot", "Content");
        var paks = Path.Combine(content, "Paks");
        var iniPath = Path.Combine(_tempRoot, "LocalAppData", "ReadyOrNot", "Saved", "Config", "Windows", "GameUserSettings.ini");

        Directory.CreateDirectory(paks);
        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);

        var install = new GameInstallation
        {
            Platform = GamePlatform.Steam,
            InstallPath = gameRoot,
            ContentPath = content,
            PaksPath = paks,
            ConfigIniPath = iniPath,
            ConfigExists = true,
            DisplayLabel = "Steam"
        };

        var publicModIo = Path.Combine(_tempRoot, "PublicModIo");
        Directory.CreateDirectory(publicModIo);
        var paths = ModWorkspacePaths.FromInstallation(install, publicModIo);
        return (paths, install);
    }

    [Fact]
    public void IniSettingsService_RestoreBackup_RestoresOriginalIniContent()
    {
        Directory.CreateDirectory(_tempRoot);
        var iniPath = Path.Combine(_tempRoot, "GameUserSettings.ini");
        var originalIni =
            "[/Script/ReadyOrNot.ReadyOrNotGameUserSettings]\r\n" +
            "CustomSetting=42\r\n" +
            "bServerSideChecksum=True\r\n" +
            "bJoinModdedServers=False\r\n" +
            "bAllowModdedPlayers=False\r\n";

        File.WriteAllText(iniPath, originalIni);

        var svc = new IniSettingsService();

        var initialRead = svc.ReadChecksumSettings(iniPath);
        Assert.False(initialRead.HasBackup);
        Assert.Null(initialRead.BackupPath);
        Assert.False(svc.RestoreBackup(iniPath).Success);

        var writeRes = svc.WriteChecksumSettings(iniPath, disableChecksum: true);
        Assert.True(writeRes.Success);

        var modifiedRead = svc.ReadChecksumSettings(iniPath);
        Assert.True(modifiedRead.HasBackup);
        Assert.Equal(iniPath + ".ronswitch.bak", modifiedRead.BackupPath);
        Assert.False(modifiedRead.ServerSideChecksum);

        var restoreRes = svc.RestoreBackup(iniPath);
        Assert.True(restoreRes.Success);
        Assert.Equal("INI_BACKUP_RESTORED", restoreRes.Code);

        var restoredContent = File.ReadAllText(iniPath);
        Assert.Equal(originalIni, restoredContent);
    }

    [Fact]
    public void ModEngine_Scan_DetectsPackageHealthWarnings()
    {
        var (paths, _) = CreateFakeGameWorkspace();
        Directory.CreateDirectory(paths.ActiveModsDir);

        File.WriteAllBytes(Path.Combine(paths.ActiveModsDir, "pakchunk99-EmptyMod.pak"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(paths.ActiveModsDir, "pakchunk99-OnlyUcas.ucas"), "ucas_data");
        File.WriteAllText(Path.Combine(paths.ActiveModsDir, "pakchunk99-OnlyUtoc.utoc"), "utoc_data");

        File.WriteAllText(Path.Combine(paths.ActiveModsDir, "pakchunk99-Healthy.pak"), "pak_data");
        File.WriteAllText(Path.Combine(paths.ActiveModsDir, "pakchunk99-Healthy.ucas"), "ucas_data");
        File.WriteAllText(Path.Combine(paths.ActiveModsDir, "pakchunk99-Healthy.utoc"), "utoc_data");

        var engine = new ModEngine();
        var scan = engine.Scan(paths);

        Assert.Equal(4, scan.Mods.Count);

        var emptyMod = Assert.Single(scan.Mods, m => m.RawName.StartsWith("pakchunk99-EmptyMod", StringComparison.OrdinalIgnoreCase));
        Assert.True(emptyMod.HasHealthWarning);
        Assert.Equal("EMPTY_FILE", emptyMod.HealthWarningCode);

        var onlyUcas = Assert.Single(scan.Mods, m => m.RawName.StartsWith("pakchunk99-OnlyUcas", StringComparison.OrdinalIgnoreCase));
        Assert.True(onlyUcas.HasHealthWarning);
        Assert.Equal("MISSING_UTOC", onlyUcas.HealthWarningCode);

        var onlyUtoc = Assert.Single(scan.Mods, m => m.RawName.StartsWith("pakchunk99-OnlyUtoc", StringComparison.OrdinalIgnoreCase));
        Assert.True(onlyUtoc.HasHealthWarning);
        Assert.Equal("MISSING_UCAS", onlyUtoc.HealthWarningCode);

        var healthy = Assert.Single(scan.Mods, m => m.RawName.StartsWith("pakchunk99-Healthy", StringComparison.OrdinalIgnoreCase));
        Assert.False(healthy.HasHealthWarning);
        Assert.Null(healthy.HealthWarningCode);
    }

    [Fact]
    public void ModEngine_SetBulkModsEnabled_TogglesMultipleModsTogether()
    {
        var (paths, _) = CreateFakeGameWorkspace();
        Directory.CreateDirectory(paths.ActiveModsDir);

        File.WriteAllText(Path.Combine(paths.ActiveModsDir, "pakchunk99-Alpha.pak"), "a");
        File.WriteAllText(Path.Combine(paths.ActiveModsDir, "pakchunk99-Beta.pak"), "b");
        File.WriteAllText(Path.Combine(paths.ActiveModsDir, "pakchunk99-Gamma.pak"), "c");

        var engine = new ModEngine();
        var initialScan = engine.Scan(paths);
        var targetIds = initialScan.Mods
            .Where(m => m.RawName.Contains("Alpha", StringComparison.OrdinalIgnoreCase) ||
                        m.RawName.Contains("Beta", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Id)
            .ToList();

        Assert.Equal(2, targetIds.Count);

        var disableRes = engine.SetBulkModsEnabled(paths, targetIds, enable: false);
        Assert.True(disableRes.Success);

        var afterDisable = engine.Scan(paths);
        Assert.Equal(1, afterDisable.ActivePakCount);
        Assert.Equal(2, afterDisable.SingleDisabledCount);

        var enableRes = engine.SetBulkModsEnabled(paths, targetIds, enable: true);
        Assert.True(enableRes.Success);

        var afterEnable = engine.Scan(paths);
        Assert.Equal(3, afterEnable.ActivePakCount);
        Assert.Equal(0, afterEnable.SingleDisabledCount);
    }

    [Fact]
    public void ModEngine_Scan_ParsesPublicModIoStateJsonAndPaksEntries()
    {
        var (paths, _) = CreateFakeGameWorkspace();

        var subDirModIo = Path.Combine(paths.ActiveModIoPaksDir, "42424");
        Directory.CreateDirectory(subDirModIo);
        File.WriteAllText(Path.Combine(subDirModIo, "CustomMap.pak"), "map_data");
        File.WriteAllText(Path.Combine(paths.ActiveModIoPaksDir, "DirectItem.pak"), "direct_data");

        var metaDir = Path.Combine(paths.PublicModIoDir, "metadata");
        Directory.CreateDirectory(metaDir);
        File.WriteAllText(
            Path.Combine(metaDir, "state.json"),
            """{"Mods":[{"ID":9911,"Profile":{"Name":"Tactical HUD"}},{"ID":7722,"Name":"Visor Overlay"}],"version":1}""");

        var cacheLogo = Path.Combine(paths.PublicModIoDir, "cache", "mods", "9911", "logos", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(cacheLogo)!);
        File.WriteAllText(cacheLogo, "png_bytes");

        var engine = new ModEngine();
        var scan = engine.Scan(paths);

        Assert.Contains(scan.ModIoEntries, e => e.Id == "modio:sub:9911" && e.DisplayName == "Tactical HUD" && e.TotalSizeBytes > 0);
        Assert.Contains(scan.ModIoEntries, e => e.Id == "modio:sub:7722" && e.DisplayName == "Visor Overlay");
        Assert.Contains(scan.ModIoEntries, e => e.Id == "modiopak:dir:42424");
        Assert.Contains(scan.ModIoEntries, e => e.Id == "modiopak:file:directitem.pak");
        Assert.Contains(scan.ModIoEntries, e => e.Id == "modio:cache:active");
    }

    [Fact]
    public void ProfileShareService_ExportAndCompare_SupportsTokenAndPlainList()
    {
        var share = new ProfileShareService();

        var localMods = new List<ModUnit>
        {
            new()
            {
                Id = "pak:pakchunk99-nocrackai_p",
                RawName = "pakchunk99-NoCrackAI_P.pak",
                DisplayName = "NoCrackAI",
                IsEnabled = true
            },
            new()
            {
                Id = "pak:pakchunk99-extralocal_p",
                RawName = "pakchunk99-ExtraLocal_P.pak",
                DisplayName = "ExtraLocal",
                IsEnabled = true
            }
        };

        var coopProfile = new ModProfile
        {
            Id = "coop",
            Name = "Co-Op",
            IsModdedMode = true,
            DisableChecksum = true,
            EnableModIo = true,
            EnabledModIds = new List<string> { "pak:pakchunk99-nocrackai_p" }
        };

        var exported = share.ExportShareText(coopProfile, localMods);
        Assert.Contains("RONMODS1:", exported, StringComparison.Ordinal);
        Assert.Contains("pakchunk99-NoCrackAI_P.pak", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("pakchunk99-ExtraLocal_P.pak", exported, StringComparison.Ordinal);

        var tokenCompareRes = share.CompareSharedText(exported, localMods);
        Assert.True(tokenCompareRes.Success);
        var tokenCmp = Assert.IsType<ProfileCompareResult>(tokenCompareRes.Data);
        Assert.Single(tokenCmp.MatchingMods, "pakchunk99-NoCrackAI_P.pak");
        Assert.Empty(tokenCmp.MissingLocalMods);
        Assert.Single(tokenCmp.ExtraLocalEnabledMods, "pakchunk99-ExtraLocal_P.pak");

        var plainCompareRes = share.CompareSharedText(
            "- pakchunk99-NoCrackAI_P.pak\n- pakchunk99-FriendOnly_P.pak\n",
            localMods);
        Assert.True(plainCompareRes.Success);
        var plainCmp = Assert.IsType<ProfileCompareResult>(plainCompareRes.Data);
        Assert.Single(plainCmp.MatchingMods, "pakchunk99-NoCrackAI_P.pak");
        Assert.Single(plainCmp.MissingLocalMods, "pakchunk99-FriendOnly_P.pak");
        Assert.Single(plainCmp.ExtraLocalEnabledMods, "pakchunk99-ExtraLocal_P.pak");
    }

    [Fact]
    public void AppController_SupportsQuickLaunchMode_RestoreIni_AndSyncCoopFromShare()
    {
        var (paths, install) = CreateFakeGameWorkspace();
        var storageDir = Path.Combine(_tempRoot, "Storage");
        var steamRoot = Path.Combine(_tempRoot, "Steam");
        var steamLibApps = Path.Combine(_tempRoot, "SteamLib", "steamapps");

        Directory.CreateDirectory(paths.ActiveModsDir);
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        Directory.CreateDirectory(steamLibApps);

        File.WriteAllText(Path.Combine(paths.ActiveModsDir, "pakchunk99-ModA.pak"), "mod_a");
        File.WriteAllText(Path.Combine(paths.ActiveModsDir, "pakchunk99-ModB.pak"), "mod_b");

        File.WriteAllText(install.ConfigIniPath,
            "[/Script/ReadyOrNot.ReadyOrNotGameUserSettings]\r\n" +
            "bServerSideChecksum=True\r\n" +
            "bJoinModdedServers=False\r\n" +
            "bAllowModdedPlayers=False\r\n");

        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), $$"""
        "libraryfolders" { "0" { "path" "{{Path.Combine(_tempRoot, "SteamLib").Replace("\\", "\\\\")}}" } }
        """);
        File.WriteAllText(Path.Combine(steamLibApps, "appmanifest_1144200.acf"), """
        "AppState" { "appid" "1144200" "installdir" "Ready Or Not" }
        """);

        var controller = new AppController(
            processService: new GameProcessService(_ => false),
            profileService: new ProfileService(storageDir),
            detectionEnv: new DetectionEnvironment
            {
                LocalAppData = Path.Combine(_tempRoot, "LocalAppData"),
                EpicManifestsDir = Path.Combine(_tempRoot, "NoEpic"),
                ExplicitSteamPaths = new[] { steamRoot },
                QueryWindowsRegistry = false
            },
            publicModIoRootOverride: Path.Combine(_tempRoot, "PublicModIo"));

        // Ensure first-run initialization
        var snap0 = controller.GetSnapshot();
        Assert.Equal(2, snap0.ModSummary!.ActivePakCount);

        // Quick launch mode toggle
        Assert.True(controller.SetLaunchModeQuick(LaunchMode.DirectDx12).Success);
        Assert.Equal(LaunchMode.DirectDx12, controller.GetSnapshot().Settings.LaunchMode);

        // Export share token & compare
        var exportRes = controller.ExportProfileShareText("solo");
        Assert.True(exportRes.Success);
        var shareText = Assert.IsType<string>(exportRes.Data);
        Assert.Contains("RONMODS1:", shareText, StringComparison.Ordinal);

        // Apply Co-Op via shared list containing only ModA -> disables ModB and turns Checksum off
        var syncRes = controller.ApplySharedToCoop("- pakchunk99-ModA.pak\n- pakchunk99-RemoteOnly.pak\n");
        Assert.True(syncRes.Success);

        var syncedSnap = controller.GetSnapshot();
        Assert.Equal("coop", syncedSnap.Settings.ActiveProfileId);
        Assert.Equal(1, syncedSnap.ModSummary!.ActivePakCount);
        Assert.Equal(1, syncedSnap.ModSummary.SingleDisabledCount);
        Assert.True(syncedSnap.Checksum!.IsCoopModFriendly);
        Assert.True(syncedSnap.Checksum.HasBackup);

        // Restore INI from backup -> restores original Checksum=True
        Assert.True(controller.RestoreIniBackup().Success);
        var restoredSnap = controller.GetSnapshot();
        Assert.True(restoredSnap.Checksum!.ServerSideChecksum);
    }
}
