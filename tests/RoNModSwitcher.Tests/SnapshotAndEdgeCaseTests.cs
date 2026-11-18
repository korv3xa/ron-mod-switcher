using System.Text.Json;
using System.Text.Json.Serialization;
using RoNModSwitcher.Core.Models;
using RoNModSwitcher.Core.Services;

namespace RoNModSwitcher.Tests;

public sealed class SnapshotAndEdgeCaseTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "RoNTests_Edge_" + Guid.NewGuid().ToString("N"));

    private static readonly JsonSerializerOptions BridgeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ToggleSingleMod_WhileInVanillaMode_MovesBetweenStashAndDisabledWithoutPollutingActiveMods()
    {
        var gameRoot = Path.Combine(_tempRoot, "Game");
        var paks = Path.Combine(gameRoot, "ReadyOrNot", "Content", "Paks");
        var mods = Path.Combine(paks, "~mods");
        Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(mods, "pakchunk99-ModOne.pak"), "one");

        var install = new GameInstallation
        {
            Platform = GamePlatform.Epic,
            InstallPath = gameRoot,
            ContentPath = Path.Combine(gameRoot, "ReadyOrNot", "Content"),
            PaksPath = paks,
            ConfigIniPath = Path.Combine(_tempRoot, "GameUserSettings.ini"),
            DisplayLabel = "Epic Games"
        };

        var paths = ModWorkspacePaths.FromInstallation(install, Path.Combine(_tempRoot, "PublicModIo"));
        var engine = new ModEngine();

        // Switch to Vanilla first
        Assert.True(engine.SetWorkspaceModded(paths, enableModded: false).Success);
        Assert.False(Directory.Exists(mods));

        // Disable ModOne while in Vanilla -> moves Stash -> Disabled
        Assert.True(engine.SetModEnabled(paths, "pak:pakchunk99-modone", enable: false).Success);
        Assert.False(Directory.Exists(mods));
        Assert.True(File.Exists(Path.Combine(paths.SingleDisabledDir, "pakchunk99-ModOne.pak")));

        // Re-enable ModOne while still in Vanilla -> moves Disabled -> Stash (NOT ~mods!)
        Assert.True(engine.SetModEnabled(paths, "pak:pakchunk99-modone", enable: true, preferVanillaStash: true).Success);
        Assert.False(Directory.Exists(mods));
        Assert.True(File.Exists(Path.Combine(paths.VanillaStashModsDir, "pakchunk99-ModOne.pak")));
    }

    [Fact]
    public void SnapshotSerialization_MatchesFrontendExpectedCamelCaseEnums()
    {
        var snap = new WorkspaceSnapshot
        {
            ResolvedLanguage = "tr",
            ModSummary = new ModScanSummary
            {
                WorkspaceMode = WorkspaceMode.Vanilla,
                Mods = new[]
                {
                    new ModUnit
                    {
                        Id = "pak:test",
                        DisplayName = "Test",
                        RawName = "pakchunk99-Test.pak",
                        Bucket = ModBucket.VanillaStashed,
                        IsEnabled = true
                    }
                }
            },
            Profiles = ProfileService.CreateDefaultProfiles()
        };

        var json = JsonSerializer.Serialize(snap, BridgeJsonOptions);

        Assert.Contains("\"workspaceMode\":\"vanilla\"", json);
        Assert.Contains("\"bucket\":\"vanillaStashed\"", json);
        Assert.Contains("\"builtInType\":\"coop\"", json);
        Assert.Contains("\"launchMode\":\"platformUri\"", json);
    }
}
