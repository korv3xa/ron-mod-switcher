using RoNModSwitcher.Core.Services;

namespace RoNModSwitcher.Tests;

public sealed class IniSettingsServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "RoNTests_Ini_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void WriteChecksumSettings_UpdatesExistingAndCreatesBackup()
    {
        Directory.CreateDirectory(_tempRoot);
        var iniPath = Path.Combine(_tempRoot, "GameUserSettings.ini");
        File.WriteAllText(iniPath,
            "[ScalabilityGroups]\r\n" +
            "sg.ResolutionQuality=100\r\n\r\n" +
            "[/Script/ReadyOrNot.ReadyOrNotGameUserSettings]\r\n" +
            "bFlashlightShadowsEnabled=True\r\n" +
            "bServerSideChecksum=True\r\n" +
            "bJoinModdedServers=False\r\n" +
            "bAllowModdedPlayers=False\r\n\r\n" +
            "[/Script/Engine.GameUserSettings]\r\n" +
            "bUseVSync=False\r\n");

        var svc = new IniSettingsService();
        var res = svc.WriteChecksumSettings(iniPath, disableChecksum: true);

        Assert.True(res.Success);
        Assert.True(File.Exists(iniPath + ".ronswitch.bak"));

        var read = svc.ReadChecksumSettings(iniPath);
        Assert.True(read.ConfigFound);
        Assert.False(read.ServerSideChecksum);
        Assert.True(read.JoinModdedServers);
        Assert.True(read.AllowModdedPlayers);
        Assert.True(read.IsCoopModFriendly);

        // Toggle back to vanilla checksum enabled
        var res2 = svc.WriteChecksumSettings(iniPath, disableChecksum: false);
        Assert.True(res2.Success);

        var read2 = svc.ReadChecksumSettings(iniPath);
        Assert.True(read2.ServerSideChecksum);
        Assert.False(read2.JoinModdedServers);
        Assert.False(read2.AllowModdedPlayers);
        Assert.False(read2.IsCoopModFriendly);
    }

    [Fact]
    public void WriteChecksumSettings_InsertsMissingKeysInsideTargetSection()
    {
        Directory.CreateDirectory(_tempRoot);
        var iniPath = Path.Combine(_tempRoot, "GameUserSettings.ini");
        File.WriteAllText(iniPath,
            "[/Script/ReadyOrNot.ReadyOrNotGameUserSettings]\n" +
            "bFlashlightShadowsEnabled=True\n\n" +
            "[/Script/Engine.GameUserSettings]\n" +
            "bUseVSync=False\n");

        var svc = new IniSettingsService();
        var res = svc.WriteChecksumSettings(iniPath, disableChecksum: true);

        Assert.True(res.Success);
        var read = svc.ReadChecksumSettings(iniPath);
        Assert.False(read.ServerSideChecksum);
        Assert.True(read.JoinModdedServers);
        Assert.True(read.AllowModdedPlayers);

        // Verify keys were inserted before [/Script/Engine.GameUserSettings]
        var content = File.ReadAllText(iniPath);
        Assert.True(content.IndexOf("bServerSideChecksum=False", StringComparison.Ordinal) <
                    content.IndexOf("[/Script/Engine.GameUserSettings]", StringComparison.Ordinal));
    }
}
