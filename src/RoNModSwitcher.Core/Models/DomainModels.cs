using System.Text.Json.Serialization;

namespace RoNModSwitcher.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GamePlatform
{
    Unknown = 0,
    Steam = 1,
    Epic = 2,
    Custom = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LaunchMode
{
    PlatformUri = 0,
    DirectExe = 1,
    DirectDx11 = 2,
    DirectDx12 = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceMode
{
    Clean = 0,
    Modded = 1,
    Vanilla = 2,
    Mixed = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BuiltInProfileType
{
    Custom = 0,
    Coop = 1,
    Solo = 2,
    Vanilla = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModBucket
{
    ActivePaks = 0,
    VanillaStashed = 1,
    SingleDisabled = 2,
    ModIoPaksActive = 3,
    ModIoPaksStashed = 4,
    ModIoPublicActive = 5,
    ModIoPublicStashed = 6
}

public sealed record GameInstallation
{
    public GamePlatform Platform { get; init; }
    public string InstallPath { get; init; } = string.Empty;
    public string ContentPath { get; init; } = string.Empty;
    public string PaksPath { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }
    public string ConfigIniPath { get; init; } = string.Empty;
    public bool ConfigExists { get; init; }
    public string? LaunchUri { get; init; }
    public bool IsAutoDetected { get; init; }
    public string DisplayLabel { get; init; } = string.Empty;
}

public sealed record ModUnit
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string RawName { get; init; } = string.Empty;
    public ModBucket Bucket { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsCurrentlyActiveOnDisk { get; init; }
    public bool IsModIo { get; init; }
    public long TotalSizeBytes { get; init; }
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
    public bool HasIoStoreFiles { get; init; }
    public int? PakChunkOrder { get; init; }
    public string? PrimaryPath { get; init; }
    public bool HasHealthWarning { get; init; }
    public string? HealthWarningCode { get; init; }
}

public sealed record ChecksumSettings
{
    public bool ConfigFound { get; init; }
    public string ConfigPath { get; init; } = string.Empty;
    public bool ServerSideChecksum { get; init; } = true;
    public bool JoinModdedServers { get; init; }
    public bool AllowModdedPlayers { get; init; }
    public bool IsCoopModFriendly => !ServerSideChecksum && JoinModdedServers && AllowModdedPlayers;
    public bool HasBackup { get; init; }
    public string? BackupPath { get; init; }
}

public sealed class ModProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BuiltInProfileType BuiltInType { get; set; } = BuiltInProfileType.Custom;
    public string Description { get; set; } = string.Empty;
    public bool IsModdedMode { get; set; } = true;
    public bool EnableModIo { get; set; } = true;
    public bool DisableChecksum { get; set; } = true;
    public bool TrackNewModsAsEnabled { get; set; }
    public List<string> EnabledModIds { get; set; } = new();
    public List<string> DisabledModIds { get; set; } = new();
    public bool IsBuiltIn => BuiltInType != BuiltInProfileType.Custom;
}

public sealed class AppSettings
{
    public string Language { get; set; } = "auto";
    public GamePlatform PreferredPlatform { get; set; } = GamePlatform.Unknown;
    public string? CustomGamePath { get; set; }
    public LaunchMode LaunchMode { get; set; } = LaunchMode.PlatformUri;
    public bool MinimizeOnLaunch { get; set; }
    public bool IncludeModIoInSwitch { get; set; } = true;
    public string? ActiveProfileId { get; set; } = "solo";
}

public sealed record ModScanSummary
{
    public WorkspaceMode WorkspaceMode { get; init; }
    public IReadOnlyList<ModUnit> Mods { get; init; } = Array.Empty<ModUnit>();
    public IReadOnlyList<ModUnit> ModIoEntries { get; init; } = Array.Empty<ModUnit>();
    public int ActivePakCount { get; init; }
    public int StashedPakCount { get; init; }
    public int SingleDisabledCount { get; init; }
    public int ModIoActiveCount { get; init; }
    public int ModIoStashedCount { get; init; }
    public long TotalActiveSizeBytes { get; init; }
    public bool HasLegacyDirsDetected { get; init; }
    public string ActiveModsPath { get; init; } = string.Empty;
    public string DisabledModsPath { get; init; } = string.Empty;
}

public sealed record OperationResult
{
    public bool Success { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Details { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public object? Data { get; init; }

    public static OperationResult Ok(
        string code,
        string message,
        IReadOnlyList<string>? warnings = null,
        string? details = null,
        object? data = null)
        => new()
        {
            Success = true,
            Code = code,
            Message = message,
            Warnings = warnings ?? Array.Empty<string>(),
            Details = details,
            Data = data
        };

    public static OperationResult Fail(string code, string message, string? details = null)
        => new()
        {
            Success = false,
            Code = code,
            Message = message,
            Details = details
        };
}

public sealed record ProfileCompareResult
{
    public string ProfileName { get; init; } = string.Empty;
    public bool DisableChecksum { get; init; }
    public bool EnableModIo { get; init; }
    public IReadOnlyList<string> MatchingMods { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingLocalMods { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExtraLocalEnabledMods { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MatchedLocalModIds { get; init; } = Array.Empty<string>();
}

public sealed record WorkspaceSnapshot
{
    public string AppName { get; init; } = "RoN Mod Switcher";
    public string AppVersion { get; init; } = "1.0.0";
    public string StudioName { get; init; } = "Vosien Studio";
    public string DiscordUrl { get; init; } = "https://discord.gg/29bndFuds3";
    public string ResolvedLanguage { get; init; } = "en";
    public bool GameRunning { get; init; }
    public GameInstallation? ActiveInstallation { get; init; }
    public IReadOnlyList<GameInstallation> DetectedInstallations { get; init; } = Array.Empty<GameInstallation>();
    public ModScanSummary? ModSummary { get; init; }
    public ChecksumSettings? Checksum { get; init; }
    public IReadOnlyList<ModProfile> Profiles { get; init; } = Array.Empty<ModProfile>();
    public AppSettings Settings { get; init; } = new();
    public string? LegacyMigrationNotice { get; init; }
}
