using System.IO.Compression;
using RoNModSwitcher.Core.Models;
using RoNModSwitcher.Core.Services;

namespace RoNModSwitcher.Tests;

public sealed class ModEngineTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "RoNTests_Mod_" + Guid.NewGuid().ToString("N"));
    private readonly GameInstallation _install;
    private readonly string _publicModIoRoot;
    private readonly ModWorkspacePaths _paths;

    public ModEngineTests()
    {
        var gameRoot = Path.Combine(_tempRoot, "Game");
        var paks = Path.Combine(gameRoot, "ReadyOrNot", "Content", "Paks");
        var content = Path.Combine(gameRoot, "ReadyOrNot", "Content");
        Directory.CreateDirectory(paks);

        _publicModIoRoot = Path.Combine(_tempRoot, "PublicModIo");
        Directory.CreateDirectory(_publicModIoRoot);

        _install = new GameInstallation
        {
            Platform = GamePlatform.Steam,
            InstallPath = gameRoot,
            ContentPath = content,
            PaksPath = paks,
            ConfigIniPath = Path.Combine(_tempRoot, "GameUserSettings.ini"),
            DisplayLabel = "Steam"
        };

        _paths = ModWorkspacePaths.FromInstallation(_install, _publicModIoRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Scan_GroupsPakUcasUtocSigAsSingleModUnit_AndTogglesTogether()
    {
        Directory.CreateDirectory(_paths.ActiveModsDir);
        File.WriteAllText(Path.Combine(_paths.ActiveModsDir, "pakchunk99-Mods_No_Crack_AI_P.pak"), "pak");
        File.WriteAllText(Path.Combine(_paths.ActiveModsDir, "pakchunk99-Mods_No_Crack_AI_P.ucas"), "ucas");
        File.WriteAllText(Path.Combine(_paths.ActiveModsDir, "pakchunk99-Mods_No_Crack_AI_P.utoc"), "utoc");
        File.WriteAllText(Path.Combine(_paths.ActiveModsDir, "pakchunk99-Mods_No_Crack_AI_P.sig"), "sig");

        var engine = new ModEngine();
        var scan = engine.Scan(_paths);

        var mod = Assert.Single(scan.Mods);
        Assert.Equal("pak:pakchunk99-mods_no_crack_ai_p", mod.Id);
        Assert.True(mod.HasIoStoreFiles);
        Assert.Equal(4, mod.Files.Count);
        Assert.True(mod.IsEnabled);

        // Disable the single mod unit -> all 4 files move together
        var disableRes = engine.SetModEnabled(_paths, mod.Id, enable: false);
        Assert.True(disableRes.Success);
        Assert.Empty(Directory.GetFiles(_paths.ActiveModsDir));
        Assert.Equal(4, Directory.GetFiles(_paths.SingleDisabledDir).Length);
    }

    [Fact]
    public void SetWorkspaceModded_MergesPublicModIo_EvenWhenGameRecreatedEmpty8254()
    {
        Directory.CreateDirectory(_paths.ActiveModsDir);
        File.WriteAllText(Path.Combine(_paths.ActiveModsDir, "pakchunk99-Homeland.pak"), "homeland");

        var modIoCacheFile = Path.Combine(_paths.PublicModIoDir, "cache", "mods", "12345", "map.pak");
        Directory.CreateDirectory(Path.GetDirectoryName(modIoCacheFile)!);
        File.WriteAllText(modIoCacheFile, "modio_data");

        var engine = new ModEngine();

        // Switch to Vanilla
        var toVanilla = engine.SetWorkspaceModded(_paths, enableModded: false, includeModIo: true);
        Assert.True(toVanilla.Success);
        Assert.False(File.Exists(modIoCacheFile));
        Assert.True(File.Exists(Path.Combine(_paths.DisabledPublicModIoDir, "cache", "mods", "12345", "map.pak")));

        // Simulate Ready or Not recreating an empty/log 8254 folder while playing Vanilla
        Directory.CreateDirectory(Path.Combine(_paths.PublicModIoDir, "logs"));
        File.WriteAllText(Path.Combine(_paths.PublicModIoDir, "logs", "modio.log"), "vanilla session");

        // Switch back to Modded -> must merge and restore map.pak!
        var toModded = engine.SetWorkspaceModded(_paths, enableModded: true, includeModIo: true);
        Assert.True(toModded.Success);
        Assert.True(File.Exists(modIoCacheFile));
        Assert.True(File.Exists(Path.Combine(_paths.ActiveModsDir, "pakchunk99-Homeland.pak")));
    }

    [Fact]
    public void DetectAndMigrateLegacy_MigratesScriptFoldersWithoutTouchingCustomBackups()
    {
        Directory.CreateDirectory(_paths.LegacyDisabledModsDir);
        Directory.CreateDirectory(_paths.LegacySingleDisabledDir);
        Directory.CreateDirectory(_paths.LegacyDisabledPublicModIoDir);
        var customBackupDir = Path.Combine(_paths.LegacyDisabledRoot, "Eski_ReadyOrRealism_Yedek");
        Directory.CreateDirectory(customBackupDir);

        File.WriteAllText(Path.Combine(_paths.LegacyDisabledModsDir, "pakchunk99-TestMod.pak"), "data");
        File.WriteAllText(Path.Combine(_paths.LegacySingleDisabledDir, "pakchunk99-Passive.pak"), "passive");
        File.WriteAllText(Path.Combine(customBackupDir, "Difficulty.Hard"), "keep_me");
        File.WriteAllText(Path.Combine(_paths.LegacyDisabledPublicModIoDir, "test.bin"), "pub");

        var engine = new ModEngine();
        var report = engine.DetectAndMigrateLegacy(_paths);

        Assert.True(report.Migrated);
        Assert.True(File.Exists(Path.Combine(_paths.VanillaStashModsDir, "pakchunk99-TestMod.pak")));
        Assert.True(File.Exists(Path.Combine(_paths.SingleDisabledDir, "pakchunk99-Passive.pak")));
        Assert.True(File.Exists(Path.Combine(_paths.DisabledPublicModIoDir, "test.bin")));
        // Custom backup MUST remain untouched!
        Assert.True(File.Exists(Path.Combine(customBackupDir, "Difficulty.Hard")));
    }

    [Fact]
    public void InstallModFiles_ExtractsZipStrippingModsPrefix_AndBlocksZipSlip()
    {
        var goodZip = Path.Combine(_tempRoot, "good.zip");
        using (var zip = ZipFile.Open(goodZip, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("ReadyOrNot/Content/Paks/~mods/pakchunk99-CoolMod_P.pak");
            using var w = new StreamWriter(e.Open());
            w.Write("cool");
        }

        var engine = new ModEngine();
        var (res, added) = engine.InstallModFiles(_paths, new[] { goodZip });
        Assert.True(res.Success);
        Assert.Single(added);
        Assert.True(File.Exists(Path.Combine(_paths.ActiveModsDir, "pakchunk99-CoolMod_P.pak")));

        var badZip = Path.Combine(_tempRoot, "evil.zip");
        using (var zip = ZipFile.Open(badZip, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("../../outside.pak");
            using var w = new StreamWriter(e.Open());
            w.Write("evil");
        }

        var (badRes, _) = engine.InstallModFiles(_paths, new[] { badZip });
        Assert.False(badRes.Success);
    }
}
