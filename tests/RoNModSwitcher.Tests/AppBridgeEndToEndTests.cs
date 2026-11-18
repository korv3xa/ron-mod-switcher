using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using RoNModSwitcher.App.Bridge;
using RoNModSwitcher.Core.Models;
using RoNModSwitcher.Core.Services;

namespace RoNModSwitcher.Tests;

public sealed class AppBridgeEndToEndTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "RoNTests_Bridge_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static JsonDocument Send(AppBridge bridge, string action, object? payload = null, bool wrapAsWebViewString = true)
    {
        var reqObj = new
        {
            id = "req_test",
            action,
            payload = payload ?? new { }
        };
        var json = JsonSerializer.Serialize(reqObj);
        var wire = wrapAsWebViewString ? JsonSerializer.Serialize(json) : json;
        var respJson = bridge.HandleMessage(wire);
        return JsonDocument.Parse(respJson);
    }

    [Fact]
    public void EmbeddedResources_ContainAllWebUiAssets()
    {
        var asm = typeof(AppBridge).Assembly;
        var expected = new[]
        {
            "RoNModSwitcher.App.wwwroot.index.html",
            "RoNModSwitcher.App.wwwroot.app.js",
            "RoNModSwitcher.App.wwwroot.i18n.js",
            "RoNModSwitcher.App.wwwroot.styles.css",
            "RoNModSwitcher.App.wwwroot.logo.png"
        };

        foreach (var name in expected)
        {
            using var stream = asm.GetManifestResourceStream(name);
            Assert.NotNull(stream);
            Assert.True(stream.Length > 0, $"Embedded resource '{name}' should not be empty.");
        }
    }

    [Fact]
    public void FullBridgeProtocol_CoversAllActionsSafely_EndToEnd()
    {
        // Setup dual Steam + Epic fake installations + Custom install
        var localAppData = Path.Combine(_tempRoot, "LocalAppData");
        var storageDir = Path.Combine(_tempRoot, "Storage");
        var publicModIo = Path.Combine(_tempRoot, "PublicModIo");

        var steamRoot = Path.Combine(_tempRoot, "Steam");
        var steamLib = Path.Combine(_tempRoot, "SteamLib");
        var steamGame = Path.Combine(steamLib, "steamapps", "common", "Ready Or Not");
        var steamMods = Path.Combine(steamGame, "ReadyOrNot", "Content", "Paks", "~mods");
        var steamExe = Path.Combine(steamGame, "ReadyOrNot.exe");

        var epicManifests = Path.Combine(_tempRoot, "EpicManifests");
        var epicGame = Path.Combine(_tempRoot, "EpicGame");
        var epicMods = Path.Combine(epicGame, "ReadyOrNot", "Content", "Paks", "~mods");

        var customGame = Path.Combine(_tempRoot, "CustomGame");
        Directory.CreateDirectory(Path.Combine(customGame, "ReadyOrNot", "Content", "Paks"));

        Directory.CreateDirectory(steamMods);
        Directory.CreateDirectory(epicMods);
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steamLib, "steamapps"));
        Directory.CreateDirectory(epicManifests);
        File.WriteAllText(steamExe, "exe_stub");

        // Legacy folder on Epic (initial default installation when both Epic and Steam are detected)
        var legacyDir = Path.Combine(epicGame, "ReadyOrNot", "Content", "_Mods_DevreDisi", "~mods");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "pakchunk99-LegacyMod.pak"), "legacy");

        // Active IoStore mod + health warning mod + regular mod
        File.WriteAllText(Path.Combine(steamMods, "pakchunk99-NoCrack_P.pak"), "pak");
        File.WriteAllText(Path.Combine(steamMods, "pakchunk99-NoCrack_P.ucas"), "ucas");
        File.WriteAllText(Path.Combine(steamMods, "pakchunk99-NoCrack_P.utoc"), "utoc");
        File.WriteAllText(Path.Combine(steamMods, "pakchunk99-NoCrack_P.sig"), "sig");
        File.WriteAllBytes(Path.Combine(steamMods, "pakchunk99-BrokenZero.pak"), Array.Empty<byte>());

        // INI config
        var iniPath = Path.Combine(localAppData, "ReadyOrNot", "Saved", "Config", "Windows", "GameUserSettings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
        File.WriteAllText(iniPath,
            "[/Script/ReadyOrNot.ReadyOrNotGameUserSettings]\r\n" +
            "bServerSideChecksum=True\r\n" +
            "bJoinModdedServers=False\r\n" +
            "bAllowModdedPlayers=False\r\n");

        // Steam & Epic manifests
        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), $$"""
        "libraryfolders" { "0" { "path" "{{steamLib.Replace("\\", "\\\\")}}" } }
        """);
        File.WriteAllText(Path.Combine(steamLib, "steamapps", "appmanifest_1144200.acf"), """
        "AppState" { "appid" "1144200" "installdir" "Ready Or Not" }
        """);
        File.WriteAllText(Path.Combine(epicManifests, "ron.item"), $$"""
        { "DisplayName": "Ready or Not", "CatalogNamespace": "ns1", "CatalogItemId": "2696c009c89f4fa8a5ff4985a822f0a5", "AppName": "ReadyOrNot", "InstallLocation": "{{epicGame.Replace("\\", "\\\\")}}", "LaunchExecutable": "ReadyOrNotEGS.exe" }
        """);
        File.WriteAllText(Path.Combine(epicGame, "ReadyOrNotEGS.exe"), "egs_stub");

        // Zip mod package for browseAndInstallMods
        var uploadZip = Path.Combine(_tempRoot, "NewMod.zip");
        using (var archive = ZipFile.Open(uploadZip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("~mods/pakchunk98-NewZipMod_P.pak");
            using var w = new StreamWriter(entry.Open());
            w.Write("zip_pak_payload");
        }

        // Track host UI callbacks
        string? pickedFolder = null;
        string[] pickedModFiles = Array.Empty<string>();
        int minimizedCount = 0;
        string? copiedClipboard = null;
        var openedFolders = new List<string>();
        var openedUrls = new List<string>();
        var launchedProcesses = new List<ProcessStartInfo>();
        bool simulateGameRunning = false;

        var controller = new AppController(
            processService: new GameProcessService(_ => simulateGameRunning, psi => launchedProcesses.Add(psi)),
            profileService: new ProfileService(storageDir),
            detectionEnv: new DetectionEnvironment
            {
                LocalAppData = localAppData,
                EpicManifestsDir = epicManifests,
                ExplicitSteamPaths = new[] { steamRoot },
                QueryWindowsRegistry = false
            },
            publicModIoRootOverride: publicModIo);

        var bridge = new AppBridge(
            controller,
            pickGameFolder: () => pickedFolder,
            pickModFiles: () => pickedModFiles,
            minimizeWindow: () => minimizedCount++,
            copyToClipboard: text => copiedClipboard = text,
            openFolderOverride: p => openedFolders.Add(p),
            openExternalUrlOverride: u => openedUrls.Add(u));

        // 1. getState (initially Epic) & migration notice + dismissNotice
        using (var doc = Send(bridge, "getState"))
        {
            var root = doc.RootElement;
            Assert.True(root.GetProperty("result").GetProperty("success").GetBoolean());
            var snap = root.GetProperty("snapshot");
            Assert.NotEqual(JsonValueKind.Null, snap.GetProperty("legacyMigrationNotice").ValueKind);
            Assert.Equal(2, snap.GetProperty("detectedInstallations").GetArrayLength());
        }
        using (var doc = Send(bridge, "dismissNotice"))
        {
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("snapshot").GetProperty("legacyMigrationNotice").ValueKind);
        }

        // 2. setLanguage
        using (var doc = Send(bridge, "setLanguage", new { language = "tr" }))
        {
            Assert.Equal("tr", doc.RootElement.GetProperty("snapshot").GetProperty("resolvedLanguage").GetString());
        }

        // 3. selectPlatform (switch to Steam and check EMPTY_FILE warning) + invalid platform error + browseGameFolder
        using (var doc = Send(bridge, "selectPlatform", new { platform = "steam" }))
        {
            var snap = doc.RootElement.GetProperty("snapshot");
            Assert.Equal("steam", snap.GetProperty("activeInstallation").GetProperty("platform").GetString());
            Assert.Contains(snap.GetProperty("modSummary").GetProperty("mods").EnumerateArray(),
                m => m.GetProperty("healthWarningCode").GetString() == "EMPTY_FILE");
        }
        using (var doc = Send(bridge, "selectPlatform", new { platform = "epic" }))
        {
            Assert.Equal("epic", doc.RootElement.GetProperty("snapshot").GetProperty("activeInstallation").GetProperty("platform").GetString());
        }
        using (var doc = Send(bridge, "selectPlatform", new { platform = "invalid" }))
        {
            Assert.False(doc.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
            Assert.Equal("INVALID_PLATFORM", doc.RootElement.GetProperty("result").GetProperty("code").GetString());
        }
        using (var doc = Send(bridge, "browseGameFolder")) // cancelled
        {
            Assert.Equal("CANCELLED", doc.RootElement.GetProperty("result").GetProperty("code").GetString());
        }
        pickedFolder = customGame;
        using (var doc = Send(bridge, "browseGameFolder"))
        {
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
            Assert.Equal("custom", doc.RootElement.GetProperty("snapshot").GetProperty("activeInstallation").GetProperty("platform").GetString());
        }
        // Switch back to Steam for remaining checks
        Assert.True(Send(bridge, "selectPlatform", new { platform = "steam" }).RootElement.GetProperty("result").GetProperty("success").GetBoolean());

        // 4. browseAndInstallMods (cancelled then .zip)
        using (var doc = Send(bridge, "browseAndInstallMods"))
        {
            Assert.Equal("CANCELLED", doc.RootElement.GetProperty("result").GetProperty("code").GetString());
        }
        pickedModFiles = new[] { uploadZip };
        using (var doc = Send(bridge, "browseAndInstallMods"))
        {
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
            Assert.Contains(doc.RootElement.GetProperty("snapshot").GetProperty("modSummary").GetProperty("mods").EnumerateArray(),
                m => m.GetProperty("rawName").GetString() == "pakchunk98-NewZipMod_P.pak");
        }

        // 5. toggleMod & setBulkMods
        using (var doc = Send(bridge, "toggleMod", new { modId = "pak:pakchunk99-nocrack_p", enable = false }))
        {
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
        }
        using (var doc = Send(bridge, "setBulkMods", new { modIds = new[] { "pak:pakchunk99-nocrack_p", "pak:pakchunk98-newzipmod_p" }, enable = false }))
        {
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
            Assert.Equal(2, doc.RootElement.GetProperty("snapshot").GetProperty("modSummary").GetProperty("singleDisabledCount").GetInt32());
        }
        using (var doc = Send(bridge, "setBulkMods", new { modIds = new[] { "pak:pakchunk99-nocrack_p", "pak:pakchunk98-newzipmod_p" }, enable = true }))
        {
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
            Assert.Equal(0, doc.RootElement.GetProperty("snapshot").GetProperty("modSummary").GetProperty("singleDisabledCount").GetInt32());
        }

        // 6. createProfile, saveProfileSelection, applyProfile, deleteProfile
        string customProfileId;
        using (var doc = Send(bridge, "createProfile", new { name = "Squad Tactical", description = "Test preset", disableChecksum = true, enableModIo = false }))
        {
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
            var profiles = doc.RootElement.GetProperty("snapshot").GetProperty("profiles").EnumerateArray().ToList();
            customProfileId = profiles.Single(p => p.GetProperty("name").GetString() == "Squad Tactical").GetProperty("id").GetString()!;
        }
        Assert.True(Send(bridge, "saveProfileSelection", new { profileId = customProfileId }).RootElement.GetProperty("result").GetProperty("success").GetBoolean());
        using (var doc = Send(bridge, "deleteProfile", new { profileId = "coop" }))
        {
            Assert.Equal("CANNOT_DELETE_BUILTIN", doc.RootElement.GetProperty("result").GetProperty("code").GetString());
        }
        Assert.True(Send(bridge, "deleteProfile", new { profileId = customProfileId }).RootElement.GetProperty("result").GetProperty("success").GetBoolean());

        // 7. toggleWorkspace (Vanilla -> Modded) & setChecksumDisabled + restoreIniBackup
        using (var doc = Send(bridge, "toggleWorkspace", new { enableModded = false }))
        {
            Assert.Equal("vanilla", doc.RootElement.GetProperty("snapshot").GetProperty("modSummary").GetProperty("workspaceMode").GetString());
        }
        using (var doc = Send(bridge, "toggleWorkspace", new { enableModded = true }))
        {
            Assert.Equal("modded", doc.RootElement.GetProperty("snapshot").GetProperty("modSummary").GetProperty("workspaceMode").GetString());
        }
        using (var doc = Send(bridge, "setChecksumDisabled", new { disable = true }))
        {
            var cs = doc.RootElement.GetProperty("snapshot").GetProperty("checksum");
            Assert.True(cs.GetProperty("isCoopModFriendly").GetBoolean());
            Assert.True(cs.GetProperty("hasBackup").GetBoolean());
        }
        using (var doc = Send(bridge, "restoreIniBackup"))
        {
            var cs = doc.RootElement.GetProperty("snapshot").GetProperty("checksum");
            Assert.True(cs.GetProperty("serverSideChecksum").GetBoolean());
        }

        // 8. exportProfile (copy=true sets COPIED & clipboard), compareSharedProfile, applySharedToCoop, copyToClipboard
        string exportedShare;
        using (var doc = Send(bridge, "exportProfile", new { profileId = "solo", copy = true }))
        {
            Assert.Equal("COPIED", doc.RootElement.GetProperty("result").GetProperty("code").GetString());
            exportedShare = doc.RootElement.GetProperty("result").GetProperty("data").GetString()!;
            Assert.Equal(exportedShare, copiedClipboard);
            Assert.Contains("RONMODS1:", exportedShare);
        }
        using (var doc = Send(bridge, "compareSharedProfile", new { rawText = "- pakchunk99-NoCrack_P.pak\n- pakchunk99-FriendMissing.pak\n" }))
        {
            var cmp = doc.RootElement.GetProperty("result").GetProperty("data");
            Assert.Equal(1, cmp.GetProperty("matchingMods").GetArrayLength());
            Assert.Equal(1, cmp.GetProperty("missingLocalMods").GetArrayLength());
            Assert.True(cmp.GetProperty("extraLocalEnabledMods").GetArrayLength() >= 1);
        }
        using (var doc = Send(bridge, "applySharedToCoop", new { rawText = "- pakchunk99-NoCrack_P.pak\n" }))
        {
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
            Assert.Equal("coop", doc.RootElement.GetProperty("snapshot").GetProperty("settings").GetProperty("activeProfileId").GetString());
        }
        using (var doc = Send(bridge, "copyToClipboard", new { text = "" }))
        {
            Assert.Equal("EMPTY_TEXT", doc.RootElement.GetProperty("result").GetProperty("code").GetString());
        }
        using (var doc = Send(bridge, "copyToClipboard", new { text = "hello-clipboard" }))
        {
            Assert.Equal("COPIED", doc.RootElement.GetProperty("result").GetProperty("code").GetString());
            Assert.Equal("hello-clipboard", copiedClipboard);
        }

        // 9. setLaunchModeQuick, updatePreferences, applyProfile(launchAfter: true), launchGame cooldown, and gameRunning lock
        Assert.False(Send(bridge, "setLaunchModeQuick", new { launchMode = "dx99" }).RootElement.GetProperty("result").GetProperty("success").GetBoolean());
        using (var doc = Send(bridge, "updatePreferences", new { launchMode = "directDx11", minimizeOnLaunch = true, includeModIoInSwitch = true }))
        {
            var s = doc.RootElement.GetProperty("snapshot").GetProperty("settings");
            Assert.Equal("directDx11", s.GetProperty("launchMode").GetString());
            Assert.True(s.GetProperty("minimizeOnLaunch").GetBoolean());
        }
        Assert.True(Send(bridge, "setLaunchModeQuick", new { launchMode = "directDx12" }).RootElement.GetProperty("result").GetProperty("success").GetBoolean());

        // Launch via applyProfile(launchAfter: true) -> should invoke starter with -dx12 and minimize window
        using (var doc = Send(bridge, "applyProfile", new { profileId = "vanilla", launchAfter = true }))
        {
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
            Assert.Single(launchedProcesses);
            Assert.Equal("-dx12", launchedProcesses[0].Arguments);
            Assert.Equal(1, minimizedCount);
        }

        // Immediate second launchGame must hit 6s LAUNCH_COOLDOWN
        using (var doc = Send(bridge, "launchGame"))
        {
            Assert.Equal("LAUNCH_COOLDOWN", doc.RootElement.GetProperty("result").GetProperty("code").GetString());
        }

        // Simulated Game Running lock blocks mod mutations
        simulateGameRunning = true;
        using (var doc = Send(bridge, "toggleWorkspace", new { enableModded = true }))
        {
            Assert.Equal("GAME_RUNNING", doc.RootElement.GetProperty("result").GetProperty("code").GetString());
        }
        simulateGameRunning = false;

        // 10. Folder & URL actions + unknown action fallback
        Assert.True(Send(bridge, "openModsFolder").RootElement.GetProperty("result").GetProperty("success").GetBoolean());
        Assert.True(Send(bridge, "openDisabledFolder").RootElement.GetProperty("result").GetProperty("success").GetBoolean());
        Assert.True(Send(bridge, "openIniFolder").RootElement.GetProperty("result").GetProperty("success").GetBoolean());
        Assert.Equal(3, openedFolders.Count);

        Assert.False(Send(bridge, "openExternalUrl", new { url = "http://insecure.example" }).RootElement.GetProperty("result").GetProperty("success").GetBoolean());
        Assert.True(Send(bridge, "openExternalUrl", new { url = "https://discord.gg/29bndFuds3" }).RootElement.GetProperty("result").GetProperty("success").GetBoolean());
        Assert.Single(openedUrls);

        using (var doc = Send(bridge, "nonExistentAction"))
        {
            Assert.Equal("UNKNOWN_ACTION", doc.RootElement.GetProperty("result").GetProperty("code").GetString());
        }
    }
}
