using RoNModSwitcher.Core.Models;
using RoNModSwitcher.Core.Services;

namespace RoNModSwitcher.Tests;

public sealed class AppControllerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "RoNTests_Ctrl_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ProfileSwitching_IsolatesCoopSoloVanillaSelection_AndUpdatesChecksum()
    {
        var localAppData = Path.Combine(_tempRoot, "LocalAppData");
        var storageDir = Path.Combine(_tempRoot, "Storage");
        var publicModIo = Path.Combine(_tempRoot, "PublicModIo");
        var gameRoot = Path.Combine(_tempRoot, "SteamLib", "steamapps", "common", "Ready Or Not");
        var modsDir = Path.Combine(gameRoot, "ReadyOrNot", "Content", "Paks", "~mods");
        var steamRoot = Path.Combine(_tempRoot, "Steam");

        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "SteamLib", "steamapps"));

        File.WriteAllText(Path.Combine(modsDir, "pakchunk99-ModA.pak"), "a");
        File.WriteAllText(Path.Combine(modsDir, "pakchunk99-ModB.pak"), "b");

        var iniPath = Path.Combine(localAppData, "ReadyOrNot", "Saved", "Config", "Windows", "GameUserSettings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(iniPath)!);
        File.WriteAllText(iniPath, "[/Script/ReadyOrNot.ReadyOrNotGameUserSettings]\r\nbServerSideChecksum=True\r\nbJoinModdedServers=False\r\nbAllowModdedPlayers=False\r\n");

        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), $$"""
        "libraryfolders" { "0" { "path" "{{Path.Combine(_tempRoot, "SteamLib").Replace("\\", "\\\\")}}" } }
        """);
        File.WriteAllText(Path.Combine(_tempRoot, "SteamLib", "steamapps", "appmanifest_1144200.acf"), """
        "AppState" { "appid" "1144200" "installdir" "Ready Or Not" }
        """);

        var controller = new AppController(
            processService: new GameProcessService(_ => false),
            profileService: new ProfileService(storageDir),
            detectionEnv: new DetectionEnvironment
            {
                LocalAppData = localAppData,
                EpicManifestsDir = Path.Combine(_tempRoot, "NoEpic"),
                ExplicitSteamPaths = new[] { steamRoot },
                QueryWindowsRegistry = false
            },
            publicModIoRootOverride: publicModIo);

        var initial = controller.GetSnapshot();
        Assert.NotNull(initial.ActiveInstallation);
        Assert.Equal(2, initial.ModSummary!.ActivePakCount);

        // Switch to Co-Op and disable ModB only in Co-Op
        Assert.True(controller.ApplyProfile("coop").Success);
        Assert.True(controller.ToggleMod("pak:pakchunk99-modb", enable: false).Success);

        var coopSnap = controller.GetSnapshot();
        Assert.Equal(1, coopSnap.ModSummary!.ActivePakCount);
        Assert.True(coopSnap.Checksum!.IsCoopModFriendly);

        // Switch to Solo -> both ModA and ModB should be active!
        Assert.True(controller.ApplyProfile("solo").Success);
        var soloSnap = controller.GetSnapshot();
        Assert.Equal(2, soloSnap.ModSummary!.ActivePakCount);

        // Switch to Vanilla -> 0 active, 2 stashed, Checksum restored to True!
        Assert.True(controller.ApplyProfile("vanilla").Success);
        var vanillaSnap = controller.GetSnapshot();
        Assert.Equal(WorkspaceMode.Vanilla, vanillaSnap.ModSummary!.WorkspaceMode);
        Assert.Equal(0, vanillaSnap.ModSummary.ActivePakCount);
        Assert.Equal(2, vanillaSnap.ModSummary.StashedPakCount);
        Assert.True(vanillaSnap.Checksum!.ServerSideChecksum);

        // Back to Co-Op -> only ModA active, ModB stays disabled!
        Assert.True(controller.ApplyProfile("coop").Success);
        var backCoopSnap = controller.GetSnapshot();
        Assert.Equal(1, backCoopSnap.ModSummary!.ActivePakCount);
        Assert.Equal(1, backCoopSnap.ModSummary.SingleDisabledCount);
    }

    [Fact]
    public void FirstRun_PreservesPreDisabledLegacyModsInsideSoloProfile()
    {
        var localAppData = Path.Combine(_tempRoot, "LocalAppData");
        var storageDir = Path.Combine(_tempRoot, "Storage");
        var publicModIo = Path.Combine(_tempRoot, "PublicModIo");
        var gameRoot = Path.Combine(_tempRoot, "SteamLib", "steamapps", "common", "Ready Or Not");
        var contentDir = Path.Combine(gameRoot, "ReadyOrNot", "Content");
        var modsDir = Path.Combine(contentDir, "Paks", "~mods");
        var legacyPassive = Path.Combine(contentDir, "_Mods_DevreDisi", "Tekil_Pasif");
        var steamRoot = Path.Combine(_tempRoot, "Steam");

        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(legacyPassive);
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "SteamLib", "steamapps"));

        File.WriteAllText(Path.Combine(modsDir, "pakchunk99-GoodMod.pak"), "good");
        File.WriteAllText(Path.Combine(legacyPassive, "pakchunk97-BrokenOldMod.pak"), "broken");

        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), $$"""
        "libraryfolders" { "0" { "path" "{{Path.Combine(_tempRoot, "SteamLib").Replace("\\", "\\\\")}}" } }
        """);
        File.WriteAllText(Path.Combine(_tempRoot, "SteamLib", "steamapps", "appmanifest_1144200.acf"), """
        "AppState" { "appid" "1144200" "installdir" "Ready Or Not" }
        """);

        var controller = new AppController(
            processService: new GameProcessService(_ => false),
            profileService: new ProfileService(storageDir),
            detectionEnv: new DetectionEnvironment
            {
                LocalAppData = localAppData,
                EpicManifestsDir = Path.Combine(_tempRoot, "NoEpic"),
                ExplicitSteamPaths = new[] { steamRoot },
                QueryWindowsRegistry = false
            },
            publicModIoRootOverride: publicModIo);

        var initial = controller.GetSnapshot();
        Assert.Equal(1, initial.ModSummary!.ActivePakCount);
        Assert.Equal(1, initial.ModSummary.SingleDisabledCount);

        // Switching to Solo must NOT re-enable the previously disabled broken mod!
        Assert.True(controller.ApplyProfile("solo").Success);
        var soloSnap = controller.GetSnapshot();
        Assert.Equal(1, soloSnap.ModSummary!.ActivePakCount);
        Assert.Equal(1, soloSnap.ModSummary.SingleDisabledCount);
        Assert.True(File.Exists(Path.Combine(modsDir, "pakchunk99-GoodMod.pak")));
        Assert.False(File.Exists(Path.Combine(modsDir, "pakchunk97-BrokenOldMod.pak")));
    }
}
