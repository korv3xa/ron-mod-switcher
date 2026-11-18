using RoNModSwitcher.Core.Models;
using RoNModSwitcher.Core.Services;

namespace RoNModSwitcher.Tests;

public sealed class PlatformDetectorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "RoNTests_Detect_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void DetectAll_FindsBothSteamAndEpicInstallations()
    {
        var localAppData = Path.Combine(_tempRoot, "LocalAppData");
        var epicManifests = Path.Combine(_tempRoot, "EpicManifests");
        var epicGameRoot = Path.Combine(_tempRoot, "EpicGames", "ReadyOrNot");
        var steamRoot = Path.Combine(_tempRoot, "Steam");
        var steamLib2 = Path.Combine(_tempRoot, "SteamLib2");
        var steamGameRoot = Path.Combine(steamLib2, "steamapps", "common", "Ready Or Not");

        Directory.CreateDirectory(epicManifests);
        Directory.CreateDirectory(Path.Combine(epicGameRoot, "ReadyOrNot", "Content", "Paks"));
        File.WriteAllText(Path.Combine(epicGameRoot, "ReadyOrNotEGS.exe"), "");

        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steamLib2, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steamGameRoot, "ReadyOrNot", "Content", "Paks"));
        Directory.CreateDirectory(Path.Combine(steamGameRoot, "ReadyOrNot", "Binaries", "Win64"));
        File.WriteAllText(Path.Combine(steamGameRoot, "ReadyOrNot", "Binaries", "Win64", "ReadyOrNotSteam-Win64-Shipping.exe"), "");

        var epicIni = Path.Combine(localAppData, "ReadyOrNotEGS", "Saved", "Config", "Windows", "GameUserSettings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(epicIni)!);
        File.WriteAllText(epicIni, "[/Script/ReadyOrNot.ReadyOrNotGameUserSettings]\r\nbServerSideChecksum=True\r\n");

        File.WriteAllText(Path.Combine(epicManifests, "ron.item"), $$"""
        {
          "DisplayName": "Ready or Not",
          "InstallLocation": "{{epicGameRoot.Replace("\\", "\\\\")}}",
          "CatalogNamespace": "ns1",
          "CatalogItemId": "item2",
          "AppName": "app3",
          "LaunchExecutable": "ReadyOrNotEGS.exe"
        }
        """);

        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), $$"""
        "libraryfolders"
        {
          "0" { "path" "{{steamRoot.Replace("\\", "\\\\")}}" }
          "1" { "path" "{{steamLib2.Replace("\\", "\\\\")}}" }
        }
        """);

        File.WriteAllText(Path.Combine(steamLib2, "steamapps", "appmanifest_1144200.acf"), """
        "AppState"
        {
          "appid" "1144200"
          "installdir" "Ready Or Not"
        }
        """);

        var detector = new PlatformDetector();
        var env = new DetectionEnvironment
        {
            LocalAppData = localAppData,
            EpicManifestsDir = epicManifests,
            ExplicitSteamPaths = new[] { steamRoot },
            QueryWindowsRegistry = false
        };

        var detected = detector.DetectAll(env);

        Assert.Equal(2, detected.Count);

        var epic = Assert.Single(detected, d => d.Platform == GamePlatform.Epic);
        Assert.Equal(Path.GetFullPath(epicGameRoot), epic.InstallPath);
        Assert.Equal("com.epicgames.launcher://apps/ns1%3Aitem2%3Aapp3?action=launch&silent=true", epic.LaunchUri);
        Assert.True(epic.ConfigExists);

        var steam = Assert.Single(detected, d => d.Platform == GamePlatform.Steam);
        Assert.Equal(Path.GetFullPath(steamGameRoot), steam.InstallPath);
        Assert.Equal("steam://rungameid/1144200", steam.LaunchUri);
        Assert.False(steam.ConfigExists);
    }

    [Fact]
    public void ResolveCustomInstallation_NormalizesNestedPaksOrModsPath()
    {
        var gameRoot = Path.Combine(_tempRoot, "CustomRoN");
        var modsDir = Path.Combine(gameRoot, "ReadyOrNot", "Content", "Paks", "~mods");
        Directory.CreateDirectory(modsDir);

        var detector = new PlatformDetector();
        var env = new DetectionEnvironment { LocalAppData = _tempRoot, QueryWindowsRegistry = false };

        var resolved = detector.ResolveCustomInstallation(modsDir, env: env);

        Assert.NotNull(resolved);
        Assert.Equal(Path.GetFullPath(gameRoot), resolved!.InstallPath);
        Assert.Equal(Path.GetFullPath(Path.Combine(gameRoot, "ReadyOrNot", "Content", "Paks")), resolved.PaksPath);
    }
}
