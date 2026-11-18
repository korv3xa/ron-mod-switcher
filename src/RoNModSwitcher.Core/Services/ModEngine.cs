using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using RoNModSwitcher.Core.Models;

namespace RoNModSwitcher.Core.Services;

public sealed record ModWorkspacePaths
{
    public string PaksDir { get; init; } = string.Empty;
    public string ActiveModsDir { get; init; } = string.Empty;
    public string ActiveModIoPaksDir { get; init; } = string.Empty;
    public string SwitchRoot { get; init; } = string.Empty;
    public string VanillaStashModsDir { get; init; } = string.Empty;
    public string VanillaStashModIoPaksDir { get; init; } = string.Empty;
    public string SingleDisabledDir { get; init; } = string.Empty;
    public string PublicModIoDir { get; init; } = string.Empty;
    public string DisabledPublicModIoDir { get; init; } = string.Empty;
    public string LegacyDisabledRoot { get; init; } = string.Empty;
    public string LegacyDisabledModsDir { get; init; } = string.Empty;
    public string LegacySingleDisabledDir { get; init; } = string.Empty;
    public string LegacyModIoPaksDir { get; init; } = string.Empty;
    public string LegacyDisabledPublicModIoDir { get; init; } = string.Empty;

    public static ModWorkspacePaths FromInstallation(GameInstallation installation, string? publicModIoRootOverride = null)
    {
        var paks = installation.PaksPath;
        var content = installation.ContentPath;
        var switchRoot = Path.Combine(content, "_RoNModSwitch");
        var legacyRoot = Path.Combine(content, "_Mods_DevreDisi");
        var publicRoot = string.IsNullOrWhiteSpace(publicModIoRootOverride)
            ? @"C:\Users\Public\mod.io"
            : publicModIoRootOverride;

        return new ModWorkspacePaths
        {
            PaksDir = paks,
            ActiveModsDir = Path.Combine(paks, "~mods"),
            ActiveModIoPaksDir = Path.Combine(paks, "mod.io"),
            SwitchRoot = switchRoot,
            VanillaStashModsDir = Path.Combine(switchRoot, "Stash", "~mods"),
            VanillaStashModIoPaksDir = Path.Combine(switchRoot, "Stash", "mod.io"),
            SingleDisabledDir = Path.Combine(switchRoot, "Disabled"),
            PublicModIoDir = Path.Combine(publicRoot, "8254"),
            DisabledPublicModIoDir = Path.Combine(publicRoot, "_8254_Disabled"),
            LegacyDisabledRoot = legacyRoot,
            LegacyDisabledModsDir = Path.Combine(legacyRoot, "~mods"),
            LegacySingleDisabledDir = Path.Combine(legacyRoot, "Tekil_Pasif"),
            LegacyModIoPaksDir = Path.Combine(legacyRoot, "modio_paks"),
            LegacyDisabledPublicModIoDir = Path.Combine(publicRoot, "_8254_DevreDisi")
        };
    }
}

public sealed record LegacyMigrationReport(bool Migrated, int MovedItems, IReadOnlyList<string> Warnings);

public sealed class ModEngine
{
    private static readonly HashSet<string> PrimaryModExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pak", ".ucas", ".utoc"
    };

    private static readonly HashSet<string> CompanionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pak", ".ucas", ".utoc", ".sig"
    };

    private static readonly Regex PakChunkOrderRegex = new("^pakchunk(\\d+)[-_]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DisplayPrefixRegex = new("^pakchunk\\d+[-_](?:Mods[-_])?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LegacyMigrationReport DetectAndMigrateLegacy(ModWorkspacePaths paths)
    {
        var warnings = new List<string>();
        int moved = 0;

        bool hasAnyLegacy =
            Directory.Exists(paths.LegacyDisabledModsDir) ||
            Directory.Exists(paths.LegacySingleDisabledDir) ||
            Directory.Exists(paths.LegacyModIoPaksDir) ||
            Directory.Exists(paths.LegacyDisabledPublicModIoDir);

        if (!hasAnyLegacy)
        {
            return new LegacyMigrationReport(false, 0, warnings);
        }

        EnsureDirectories(paths);

        try
        {
            if (Directory.Exists(paths.LegacyDisabledModsDir))
            {
                moved += MoveDirectoryContents(paths.LegacyDisabledModsDir, paths.VanillaStashModsDir);
                TryDeleteEmptyDirectory(paths.LegacyDisabledModsDir);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Legacy ~mods migration warning: {ex.Message}");
        }

        try
        {
            if (Directory.Exists(paths.LegacySingleDisabledDir))
            {
                moved += MoveDirectoryContents(paths.LegacySingleDisabledDir, paths.SingleDisabledDir);
                TryDeleteEmptyDirectory(paths.LegacySingleDisabledDir);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Legacy single-disabled migration warning: {ex.Message}");
        }

        try
        {
            if (Directory.Exists(paths.LegacyModIoPaksDir))
            {
                moved += MoveDirectoryContents(paths.LegacyModIoPaksDir, paths.VanillaStashModIoPaksDir);
                TryDeleteEmptyDirectory(paths.LegacyModIoPaksDir);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Legacy mod.io paks migration warning: {ex.Message}");
        }

        try
        {
            if (Directory.Exists(paths.LegacyDisabledPublicModIoDir))
            {
                moved += MoveDirectoryContents(paths.LegacyDisabledPublicModIoDir, paths.DisabledPublicModIoDir);
                TryDeleteEmptyDirectory(paths.LegacyDisabledPublicModIoDir);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Legacy public mod.io migration warning: {ex.Message}");
        }

        // Only remove _Mods_DevreDisi if it is completely empty; leave user backups untouched!
        TryDeleteEmptyDirectory(paths.LegacyDisabledRoot);

        return new LegacyMigrationReport(moved > 0, moved, warnings);
    }

    public ModScanSummary Scan(ModWorkspacePaths paths)
    {
        var mods = new List<ModUnit>();
        mods.AddRange(ScanPool(paths.ActiveModsDir, ModBucket.ActivePaks, isEnabled: true, isCurrentlyActive: true));
        mods.AddRange(ScanPool(paths.VanillaStashModsDir, ModBucket.VanillaStashed, isEnabled: true, isCurrentlyActive: false));
        mods.AddRange(ScanPool(paths.SingleDisabledDir, ModBucket.SingleDisabled, isEnabled: false, isCurrentlyActive: false));

        mods = mods
            .OrderByDescending(m => m.IsEnabled)
            .ThenBy(m => m.PakChunkOrder ?? int.MaxValue)
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var modIoEntries = ScanModIoEntries(paths);

        int activePakCount = mods.Count(m => m.Bucket == ModBucket.ActivePaks);
        int stashedPakCount = mods.Count(m => m.Bucket == ModBucket.VanillaStashed);
        int singleDisabledCount = mods.Count(m => m.Bucket == ModBucket.SingleDisabled);
        int modIoActiveCount = modIoEntries.Count(m => m.IsCurrentlyActiveOnDisk);
        int modIoStashedCount = modIoEntries.Count(m => !m.IsCurrentlyActiveOnDisk);

        bool anyActive = activePakCount > 0 || modIoActiveCount > 0;
        bool anyStashed = stashedPakCount > 0 || modIoStashedCount > 0;

        var workspaceMode = (anyActive, anyStashed) switch
        {
            (true, false) => WorkspaceMode.Modded,
            (false, true) => WorkspaceMode.Vanilla,
            (true, true) => WorkspaceMode.Mixed,
            (false, false) => WorkspaceMode.Clean
        };

        long totalActiveSize = mods.Where(m => m.IsCurrentlyActiveOnDisk).Sum(m => m.TotalSizeBytes)
                               + modIoEntries.Where(m => m.IsCurrentlyActiveOnDisk).Sum(m => m.TotalSizeBytes);

        bool legacyDetected =
            Directory.Exists(paths.LegacyDisabledModsDir) ||
            Directory.Exists(paths.LegacySingleDisabledDir) ||
            Directory.Exists(paths.LegacyModIoPaksDir) ||
            Directory.Exists(paths.LegacyDisabledPublicModIoDir);

        return new ModScanSummary
        {
            WorkspaceMode = workspaceMode,
            Mods = mods,
            ModIoEntries = modIoEntries,
            ActivePakCount = activePakCount,
            StashedPakCount = stashedPakCount,
            SingleDisabledCount = singleDisabledCount,
            ModIoActiveCount = modIoActiveCount,
            ModIoStashedCount = modIoStashedCount,
            TotalActiveSizeBytes = totalActiveSize,
            HasLegacyDirsDetected = legacyDetected,
            ActiveModsPath = paths.ActiveModsDir,
            DisabledModsPath = paths.SingleDisabledDir
        };
    }

    public OperationResult SetWorkspaceModded(ModWorkspacePaths paths, bool enableModded, bool includeModIo = true)
    {
        EnsureDirectories(paths);
        var warnings = new List<string>();

        try
        {
            if (enableModded)
            {
                if (Directory.Exists(paths.VanillaStashModsDir))
                {
                    MoveDirectoryOrMerge(paths.VanillaStashModsDir, paths.ActiveModsDir);
                }

                if (includeModIo)
                {
                    RestoreModIo(paths, warnings);
                }

                var after = Scan(paths);
                return OperationResult.Ok(
                    "WORKSPACE_MODDED",
                    $"Modded mode enabled ({after.ActivePakCount} mod packages active).",
                    warnings);
            }
            else
            {
                if (Directory.Exists(paths.ActiveModsDir))
                {
                    MoveDirectoryOrMerge(paths.ActiveModsDir, paths.VanillaStashModsDir);
                }

                if (includeModIo)
                {
                    StashModIo(paths, warnings);
                }

                var after = Scan(paths);
                return OperationResult.Ok(
                    "WORKSPACE_VANILLA",
                    $"Vanilla / Online mode enabled ({after.StashedPakCount} mod packages safely stashed).",
                    warnings);
            }
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("WORKSPACE_SWITCH_FAILED", ex.Message);
        }
    }

    public OperationResult SetModEnabled(ModWorkspacePaths paths, string modId, bool enable, bool preferVanillaStash = false)
    {
        EnsureDirectories(paths);
        var scan = Scan(paths);
        var mod = scan.Mods.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase));
        if (mod is null)
        {
            return OperationResult.Fail("MOD_NOT_FOUND", $"Mod unit '{modId}' was not found.");
        }

        if (mod.IsEnabled == enable)
        {
            return OperationResult.Ok("MOD_UNCHANGED", $"Mod '{mod.DisplayName}' is already {(enable ? "enabled" : "disabled")}.");
        }

        var useStash = preferVanillaStash || scan.WorkspaceMode == WorkspaceMode.Vanilla;
        var enabledPoolDir = useStash
            ? paths.VanillaStashModsDir
            : paths.ActiveModsDir;

        Directory.CreateDirectory(enabledPoolDir);
        Directory.CreateDirectory(paths.SingleDisabledDir);

        var targetDir = enable ? enabledPoolDir : paths.SingleDisabledDir;

        try
        {
            MoveModUnit(mod, targetDir);
            return OperationResult.Ok(
                enable ? "MOD_ENABLED" : "MOD_DISABLED",
                $"{mod.DisplayName} {(enable ? "enabled" : "disabled")}.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("MOD_TOGGLE_FAILED", ex.Message);
        }
    }

    public OperationResult SetBulkModsEnabled(
        ModWorkspacePaths paths,
        IEnumerable<string> modIds,
        bool enable,
        bool preferVanillaStash = false)
    {
        EnsureDirectories(paths);
        var scan = Scan(paths);
        var idSet = new HashSet<string>(modIds, StringComparer.OrdinalIgnoreCase);
        var targets = scan.Mods.Where(m => idSet.Contains(m.Id) && m.IsEnabled != enable).ToList();

        if (targets.Count == 0)
        {
            return OperationResult.Ok("MODS_BULK_UNCHANGED", "No mods needed to change.");
        }

        var useStash = preferVanillaStash || scan.WorkspaceMode == WorkspaceMode.Vanilla;
        var enabledPoolDir = useStash ? paths.VanillaStashModsDir : paths.ActiveModsDir;

        Directory.CreateDirectory(enabledPoolDir);
        Directory.CreateDirectory(paths.SingleDisabledDir);
        var targetDir = enable ? enabledPoolDir : paths.SingleDisabledDir;

        try
        {
            int movedCount = 0;
            foreach (var mod in targets)
            {
                MoveModUnit(mod, targetDir);
                movedCount++;
            }

            return OperationResult.Ok(
                enable ? "MODS_BULK_ENABLED" : "MODS_BULK_DISABLED",
                $"{movedCount} mod(s) {(enable ? "enabled" : "disabled")}.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("MODS_BULK_FAILED", ex.Message);
        }
    }

    public OperationResult ApplyProfile(ModWorkspacePaths paths, ModProfile profile, bool includeModIo = true)
    {
        EnsureDirectories(paths);
        var warnings = new List<string>();

        try
        {
            if (!profile.IsModdedMode)
            {
                return SetWorkspaceModded(paths, enableModded: false, includeModIo: includeModIo);
            }

            // First ensure stashed mods are brought into active workspace, then reconcile per-mod states
            if (Directory.Exists(paths.VanillaStashModsDir))
            {
                MoveDirectoryOrMerge(paths.VanillaStashModsDir, paths.ActiveModsDir);
            }

            Directory.CreateDirectory(paths.ActiveModsDir);
            Directory.CreateDirectory(paths.SingleDisabledDir);

            var currentMods = Scan(paths).Mods;
            var enabledSet = new HashSet<string>(profile.EnabledModIds, StringComparer.OrdinalIgnoreCase);
            var disabledSet = new HashSet<string>(profile.DisabledModIds, StringComparer.OrdinalIgnoreCase);

            foreach (var mod in currentMods)
            {
                bool shouldBeEnabled = profile.TrackNewModsAsEnabled
                    ? !disabledSet.Contains(mod.Id)
                    : enabledSet.Contains(mod.Id);

                if (shouldBeEnabled && mod.Bucket == ModBucket.SingleDisabled)
                {
                    MoveModUnit(mod, paths.ActiveModsDir);
                }
                else if (!shouldBeEnabled && mod.Bucket != ModBucket.SingleDisabled)
                {
                    MoveModUnit(mod, paths.SingleDisabledDir);
                }
            }

            if (includeModIo)
            {
                if (profile.EnableModIo)
                {
                    RestoreModIo(paths, warnings);
                }
                else
                {
                    StashModIo(paths, warnings);
                }
            }

            var finalScan = Scan(paths);
            return OperationResult.Ok(
                "PROFILE_APPLIED",
                $"Profile '{profile.Name}' applied ({finalScan.ActivePakCount} active).",
                warnings);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("PROFILE_APPLY_FAILED", ex.Message);
        }
    }

    public (OperationResult Result, IReadOnlyList<string> InstalledModIds) InstallModFiles(
        ModWorkspacePaths paths,
        IEnumerable<string> inputPaths,
        bool preferVanillaStash = false)
    {
        EnsureDirectories(paths);
        var scanBefore = Scan(paths);
        var targetDir = (preferVanillaStash || scanBefore.WorkspaceMode == WorkspaceMode.Vanilla)
            ? paths.VanillaStashModsDir
            : paths.ActiveModsDir;

        Directory.CreateDirectory(targetDir);

        int installedEntries = 0;
        var warnings = new List<string>();
        var beforeIds = new HashSet<string>(scanBefore.Mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var raw in inputPaths)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var full = Path.GetFullPath(raw.Trim('"'));

                if (Directory.Exists(full))
                {
                    var dirName = Path.GetFileName(full);
                    if (string.Equals(dirName, "~mods", StringComparison.OrdinalIgnoreCase))
                    {
                        installedEntries += CopyDirectoryContents(full, targetDir);
                    }
                    else
                    {
                        var destSub = Path.Combine(targetDir, dirName);
                        CopyDirectoryRecursive(full, destSub);
                        installedEntries++;
                    }
                }
                else if (File.Exists(full))
                {
                    var ext = Path.GetExtension(full);
                    if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        installedEntries += ExtractZipSafely(full, targetDir);
                    }
                    else if (CompanionExtensions.Contains(ext))
                    {
                        var destFile = Path.Combine(targetDir, Path.GetFileName(full));
                        File.Copy(full, destFile, overwrite: true);
                        installedEntries++;
                    }
                    else
                    {
                        warnings.Add($"Skipped unsupported file: {Path.GetFileName(full)}");
                    }
                }
            }

            if (installedEntries == 0)
            {
                return (OperationResult.Fail("NO_VALID_MODS", "No supported mod files (.pak, .ucas, .utoc, .sig, .zip, or folders) were found."), Array.Empty<string>());
            }

            var scanAfter = Scan(paths);
            var addedIds = scanAfter.Mods
                .Where(m => !beforeIds.Contains(m.Id))
                .Select(m => m.Id)
                .ToList();

            return (
                OperationResult.Ok("MODS_INSTALLED", $"Installed {installedEntries} item(s).", warnings),
                addedIds);
        }
        catch (Exception ex)
        {
            return (OperationResult.Fail("INSTALL_FAILED", ex.Message), Array.Empty<string>());
        }
    }

    public OperationResult DeleteMod(ModWorkspacePaths paths, string modId, bool sendToRecycleBin = true)
    {
        var scan = Scan(paths);
        var mod = scan.Mods.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase));
        if (mod is null)
        {
            return OperationResult.Fail("MOD_NOT_FOUND", $"Mod unit '{modId}' was not found.");
        }

        try
        {
            if (mod.IsDirectory && !string.IsNullOrWhiteSpace(mod.PrimaryPath) && Directory.Exists(mod.PrimaryPath))
            {
                DeletePath(mod.PrimaryPath, sendToRecycleBin);
            }
            else
            {
                foreach (var filePath in mod.Files)
                {
                    if (File.Exists(filePath))
                    {
                        DeletePath(filePath, sendToRecycleBin);
                    }
                }
            }

            return OperationResult.Ok("MOD_DELETED", $"Deleted '{mod.DisplayName}'.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("MOD_DELETE_FAILED", ex.Message);
        }
    }

    private void RestoreModIo(ModWorkspacePaths paths, List<string> warnings)
    {
        try
        {
            if (Directory.Exists(paths.VanillaStashModIoPaksDir))
            {
                MoveDirectoryOrMerge(paths.VanillaStashModIoPaksDir, paths.ActiveModIoPaksDir);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not restore Paks\\mod.io: {ex.Message}");
        }

        try
        {
            if (Directory.Exists(paths.DisabledPublicModIoDir))
            {
                MoveDirectoryOrMerge(paths.DisabledPublicModIoDir, paths.PublicModIoDir);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not restore Public mod.io (8254): {ex.Message}");
        }
    }

    private void StashModIo(ModWorkspacePaths paths, List<string> warnings)
    {
        try
        {
            if (Directory.Exists(paths.ActiveModIoPaksDir))
            {
                MoveDirectoryOrMerge(paths.ActiveModIoPaksDir, paths.VanillaStashModIoPaksDir);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not stash Paks\\mod.io: {ex.Message}");
        }

        try
        {
            var cacheMods = Path.Combine(paths.PublicModIoDir, "cache", "mods");
            bool hasPublicMods = Directory.Exists(cacheMods) &&
                                 Directory.EnumerateFiles(cacheMods, "*", SearchOption.AllDirectories).Any();
            if (hasPublicMods || (Directory.Exists(paths.PublicModIoDir) && Directory.Exists(paths.DisabledPublicModIoDir)))
            {
                MoveDirectoryOrMerge(paths.PublicModIoDir, paths.DisabledPublicModIoDir);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not stash Public mod.io (8254): {ex.Message}");
        }
    }

    private static List<ModUnit> ScanPool(string dir, ModBucket bucket, bool isEnabled, bool isCurrentlyActive)
    {
        var list = new List<ModUnit>();
        if (!Directory.Exists(dir))
        {
            return list;
        }

        // 1) Subdirectories inside the pool represent folder-based mod units
        foreach (var subDir in Directory.EnumerateDirectories(dir))
        {
            var info = new DirectoryInfo(subDir);
            var files = Directory.EnumerateFiles(subDir, "*", SearchOption.AllDirectories).ToList();
            bool hasMods = files.Any(f => PrimaryModExtensions.Contains(Path.GetExtension(f)));
            if (!hasMods && files.Count == 0)
            {
                continue;
            }

            long size = 0;
            bool hasIoStore = false;
            bool hasEmptyFile = false;
            foreach (var f in files)
            {
                try
                {
                    var fi = new FileInfo(f);
                    size += fi.Length;
                    if (fi.Length == 0 && PrimaryModExtensions.Contains(fi.Extension))
                    {
                        hasEmptyFile = true;
                    }
                    if (fi.Extension.Equals(".ucas", StringComparison.OrdinalIgnoreCase) ||
                        fi.Extension.Equals(".utoc", StringComparison.OrdinalIgnoreCase))
                    {
                        hasIoStore = true;
                    }
                }
                catch { }
            }

            string? dirWarning = null;
            if (!hasMods) dirWarning = "EMPTY_FOLDER";
            else if (hasEmptyFile) dirWarning = "EMPTY_FILE";

            list.Add(new ModUnit
            {
                Id = $"dir:{info.Name.ToLowerInvariant()}",
                RawName = info.Name,
                DisplayName = FormatDisplayName(info.Name),
                Bucket = bucket,
                IsDirectory = true,
                IsEnabled = isEnabled,
                IsCurrentlyActiveOnDisk = isCurrentlyActive,
                IsModIo = false,
                TotalSizeBytes = size,
                Files = files,
                HasIoStoreFiles = hasIoStore,
                PakChunkOrder = ExtractPakChunkOrder(info.Name),
                PrimaryPath = info.FullName,
                HasHealthWarning = dirWarning is not null,
                HealthWarningCode = dirWarning
            });
        }

        // 2) Top-level mod files grouped by stem (.pak / .ucas / .utoc / .sig)
        var groups = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
        {
            var fi = new FileInfo(filePath);
            if (!CompanionExtensions.Contains(fi.Extension))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(fi.Name);
            if (!groups.TryGetValue(stem, out var group))
            {
                group = new List<FileInfo>();
                groups[stem] = group;
            }
            group.Add(fi);
        }

        foreach (var (stem, groupFiles) in groups)
        {
            // Skip orphan .sig files without a .pak/.ucas/.utoc
            if (!groupFiles.Any(f => PrimaryModExtensions.Contains(f.Extension)))
            {
                continue;
            }

            long size = groupFiles.Sum(f => f.Length);
            bool hasUcas = groupFiles.Any(f => f.Extension.Equals(".ucas", StringComparison.OrdinalIgnoreCase));
            bool hasUtoc = groupFiles.Any(f => f.Extension.Equals(".utoc", StringComparison.OrdinalIgnoreCase));
            bool hasEmpty = groupFiles.Any(f => f.Length == 0 && PrimaryModExtensions.Contains(f.Extension));
            bool hasIoStore = hasUcas || hasUtoc;

            string? healthCode = null;
            if (hasEmpty) healthCode = "EMPTY_FILE";
            else if (hasUcas && !hasUtoc) healthCode = "MISSING_UTOC";
            else if (hasUtoc && !hasUcas) healthCode = "MISSING_UCAS";

            var primary = groupFiles.FirstOrDefault(f => f.Extension.Equals(".pak", StringComparison.OrdinalIgnoreCase))
                          ?? groupFiles[0];

            list.Add(new ModUnit
            {
                Id = $"pak:{stem.ToLowerInvariant()}",
                RawName = primary.Name,
                DisplayName = FormatDisplayName(stem),
                Bucket = bucket,
                IsDirectory = false,
                IsEnabled = isEnabled,
                IsCurrentlyActiveOnDisk = isCurrentlyActive,
                IsModIo = false,
                TotalSizeBytes = size,
                Files = groupFiles.Select(f => f.FullName).ToList(),
                HasIoStoreFiles = hasIoStore,
                PakChunkOrder = ExtractPakChunkOrder(stem),
                PrimaryPath = primary.FullName,
                HasHealthWarning = healthCode is not null,
                HealthWarningCode = healthCode
            });
        }

        return list;
    }

    private static List<ModUnit> ScanModIoEntries(ModWorkspacePaths paths)
    {
        var entries = new List<ModUnit>();

        ScanModIoPaksFolder(entries, paths.ActiveModIoPaksDir, ModBucket.ModIoPaksActive, isActive: true);
        ScanModIoPaksFolder(entries, paths.VanillaStashModIoPaksDir, ModBucket.ModIoPaksStashed, isActive: false);

        ScanPublicModIoRoot(entries, paths.PublicModIoDir, ModBucket.ModIoPublicActive, isActive: true);
        ScanPublicModIoRoot(entries, paths.DisabledPublicModIoDir, ModBucket.ModIoPublicStashed, isActive: false);

        return entries;
    }

    private static void ScanModIoPaksFolder(List<ModUnit> entries, string dirPath, ModBucket bucket, bool isActive)
    {
        if (!Directory.Exists(dirPath)) return;

        foreach (var subDir in Directory.EnumerateDirectories(dirPath))
        {
            var info = new DirectoryInfo(subDir);
            AddDirSummaryIfNonEmpty(
                entries,
                subDir,
                $"modiopak:dir:{info.Name}",
                $"mod.io · {FormatDisplayName(info.Name)}",
                bucket,
                isActive);
        }

        foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.TopDirectoryOnly))
        {
            var fi = new FileInfo(file);
            if (!PrimaryModExtensions.Contains(fi.Extension)) continue;
            entries.Add(new ModUnit
            {
                Id = $"modiopak:file:{fi.Name.ToLowerInvariant()}",
                RawName = fi.Name,
                DisplayName = $"mod.io · {FormatDisplayName(Path.GetFileNameWithoutExtension(fi.Name))}",
                Bucket = bucket,
                IsDirectory = false,
                IsEnabled = isActive,
                IsCurrentlyActiveOnDisk = isActive,
                IsModIo = true,
                TotalSizeBytes = fi.Length,
                Files = new[] { fi.FullName },
                PrimaryPath = fi.FullName
            });
        }
    }

    private static void ScanPublicModIoRoot(List<ModUnit> entries, string rootDir, ModBucket bucket, bool isActive)
    {
        if (!Directory.Exists(rootDir)) return;

        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Parse metadata/state.json for subscribed mod.io mods
        var stateJsonPath = Path.Combine(rootDir, "metadata", "state.json");
        if (File.Exists(stateJsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(stateJsonPath));
                if (doc.RootElement.TryGetProperty("Mods", out var modsEl) && modsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in modsEl.EnumerateArray())
                    {
                        string? modId = null;
                        if (item.TryGetProperty("ID", out var idEl))
                        {
                            modId = idEl.ValueKind == JsonValueKind.Number ? idEl.GetRawText() : idEl.GetString();
                        }

                        string? name = null;
                        if (item.TryGetProperty("Profile", out var profEl) &&
                            profEl.ValueKind == JsonValueKind.Object &&
                            profEl.TryGetProperty("Name", out var nameEl))
                        {
                            name = nameEl.GetString();
                        }
                        if (string.IsNullOrWhiteSpace(name) && item.TryGetProperty("Name", out var directNameEl))
                        {
                            name = directNameEl.GetString();
                        }

                        long size = 0;
                        if (!string.IsNullOrWhiteSpace(modId))
                        {
                            knownIds.Add(modId);
                            var subCache = Path.Combine(rootDir, "cache", "mods", modId);
                            if (Directory.Exists(subCache))
                            {
                                size += GetDirectorySize(subCache);
                            }
                            var modsSub = Path.Combine(rootDir, "mods", modId);
                            if (Directory.Exists(modsSub))
                            {
                                size += GetDirectorySize(modsSub);
                            }
                        }

                        var display = !string.IsNullOrWhiteSpace(name)
                            ? name!
                            : (!string.IsNullOrWhiteSpace(modId) ? $"mod.io #{modId}" : "mod.io Mod");

                        entries.Add(new ModUnit
                        {
                            Id = $"modio:sub:{modId ?? display}",
                            RawName = modId ?? display,
                            DisplayName = display,
                            Bucket = bucket,
                            IsDirectory = true,
                            IsEnabled = isActive,
                            IsCurrentlyActiveOnDisk = isActive,
                            IsModIo = true,
                            TotalSizeBytes = size,
                            PrimaryPath = stateJsonPath
                        });
                    }
                }
            }
            catch
            {
                // Tolerate partially written state.json
            }
        }

        // 2) Installed mod.io mod directories under <root>/mods
        var modsFolder = Path.Combine(rootDir, "mods");
        if (Directory.Exists(modsFolder))
        {
            foreach (var sub in Directory.EnumerateDirectories(modsFolder))
            {
                var name = Path.GetFileName(sub);
                if (knownIds.Contains(name)) continue;
                AddDirSummaryIfNonEmpty(entries, sub, $"modio:folder:{name}", $"mod.io #{name}", bucket, isActive);
            }
        }

        // 3) Browser cache / logos summary under cache/mods
        var cacheMods = Path.Combine(rootDir, "cache", "mods");
        if (Directory.Exists(cacheMods))
        {
            var cacheSubDirs = Directory.EnumerateDirectories(cacheMods).ToList();
            var cacheFiles = Directory.EnumerateFiles(cacheMods, "*", SearchOption.AllDirectories).ToList();
            if (cacheFiles.Count > 0)
            {
                long size = 0;
                foreach (var f in cacheFiles)
                {
                    try { size += new FileInfo(f).Length; } catch { }
                }

                entries.Add(new ModUnit
                {
                    Id = $"modio:cache:{(isActive ? "active" : "stashed")}",
                    RawName = $"cache/mods ({cacheSubDirs.Count} items)",
                    DisplayName = $"mod.io Cache ({cacheSubDirs.Count})",
                    Bucket = bucket,
                    IsDirectory = true,
                    IsEnabled = isActive,
                    IsCurrentlyActiveOnDisk = isActive,
                    IsModIo = true,
                    TotalSizeBytes = size,
                    Files = cacheFiles,
                    PrimaryPath = cacheMods
                });
            }
        }
    }

    private static long GetDirectorySize(string dir)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return total;
    }

    private static void AddDirSummaryIfNonEmpty(
        List<ModUnit> entries,
        string dirPath,
        string id,
        string displayName,
        ModBucket bucket,
        bool isActive)
    {
        if (!Directory.Exists(dirPath)) return;
        var files = Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories).ToList();
        if (files.Count == 0) return;

        long totalSize = 0;
        foreach (var f in files)
        {
            try { totalSize += new FileInfo(f).Length; } catch { }
        }

        entries.Add(new ModUnit
        {
            Id = id,
            RawName = Path.GetFileName(dirPath),
            DisplayName = displayName,
            Bucket = bucket,
            IsDirectory = true,
            IsEnabled = isActive,
            IsCurrentlyActiveOnDisk = isActive,
            IsModIo = true,
            TotalSizeBytes = totalSize,
            Files = files,
            PrimaryPath = dirPath
        });
    }

    private static void MoveModUnit(ModUnit mod, string targetPoolDir)
    {
        Directory.CreateDirectory(targetPoolDir);
        if (mod.IsDirectory && !string.IsNullOrWhiteSpace(mod.PrimaryPath) && Directory.Exists(mod.PrimaryPath))
        {
            var destDir = Path.Combine(targetPoolDir, mod.RawName);
            MoveDirectoryOrMerge(mod.PrimaryPath, destDir);
            return;
        }

        foreach (var srcFile in mod.Files)
        {
            if (!File.Exists(srcFile)) continue;
            var destFile = Path.Combine(targetPoolDir, Path.GetFileName(srcFile));
            MoveFileOverwrite(srcFile, destFile);
        }
    }

    private static void EnsureDirectories(ModWorkspacePaths paths)
    {
        Directory.CreateDirectory(paths.PaksDir);
        Directory.CreateDirectory(paths.SwitchRoot);
        Directory.CreateDirectory(Path.Combine(paths.SwitchRoot, "Stash"));
        Directory.CreateDirectory(paths.SingleDisabledDir);
    }

    private static void MoveDirectoryOrMerge(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;

        bool hasEntries = Directory.EnumerateFileSystemEntries(sourceDir).Any();
        if (!hasEntries)
        {
            TryDeleteEmptyDirectory(sourceDir);
            return;
        }

        if (!Directory.Exists(destDir))
        {
            var parent = Path.GetDirectoryName(destDir);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

            try
            {
                Directory.Move(sourceDir, destDir);
                return;
            }
            catch (IOException)
            {
                // Cross-volume or destination locked: fall back to recursive merge
            }
        }

        Directory.CreateDirectory(destDir);
        MoveDirectoryContents(sourceDir, destDir);
        TryDeleteEmptyDirectory(sourceDir);
    }

    private static int MoveDirectoryContents(string sourceDir, string destDir)
    {
        int count = 0;
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            MoveFileOverwrite(file, destFile);
            count++;
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var destSub = Path.Combine(destDir, Path.GetFileName(subDir));
            if (!Directory.Exists(destSub))
            {
                try
                {
                    Directory.Move(subDir, destSub);
                    count++;
                    continue;
                }
                catch (IOException) { }
            }

            count += MoveDirectoryContents(subDir, destSub);
            TryDeleteEmptyDirectory(subDir);
        }

        return count;
    }

    private static void MoveFileOverwrite(string sourceFile, string destFile)
    {
        var destParent = Path.GetDirectoryName(destFile);
        if (!string.IsNullOrWhiteSpace(destParent))
        {
            Directory.CreateDirectory(destParent);
        }

        File.Move(sourceFile, destFile, overwrite: true);
    }

    private static int CopyDirectoryContents(string sourceDir, string destDir)
    {
        int count = 0;
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var ext = Path.GetExtension(file);
            if (!CompanionExtensions.Contains(ext)) continue;
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            count++;
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            CopyDirectoryRecursive(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
            count++;
        }

        return count;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var sub in Directory.EnumerateDirectories(sourceDir))
        {
            CopyDirectoryRecursive(sub, Path.Combine(destDir, Path.GetFileName(sub)));
        }
    }

    private static int ExtractZipSafely(string zipPath, string targetModsDir)
    {
        int count = 0;
        var fullTargetRoot = Path.GetFullPath(targetModsDir);
        if (!fullTargetRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            fullTargetRoot += Path.DirectorySeparatorChar;
        }

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // folder entry

            var normalizedRel = entry.FullName.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            // Strip common archive wrappers like ReadyOrNot/Content/Paks/~mods/ or ~mods/
            normalizedRel = StripArchiveModPrefix(normalizedRel);
            if (string.IsNullOrWhiteSpace(normalizedRel)) continue;

            var ext = Path.GetExtension(entry.Name);
            // Allow companion mod files or files inside mod subfolders
            bool isTopLevel = !normalizedRel.Contains(Path.DirectorySeparatorChar);
            if (isTopLevel && !CompanionExtensions.Contains(ext))
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(targetModsDir, normalizedRel));
            if (!destinationPath.StartsWith(fullTargetRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsafe zip entry path detected: {entry.FullName}");
            }

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

            entry.ExtractToFile(destinationPath, overwrite: true);
            if (PrimaryModExtensions.Contains(ext)) count++;
        }

        return count;
    }

    private static string StripArchiveModPrefix(string relativePath)
    {
        var markers = new[]
        {
            @"ReadyOrNot\Content\Paks\~mods\",
            @"Content\Paks\~mods\",
            @"Paks\~mods\",
            @"~mods\"
        };

        foreach (var marker in markers)
        {
            var idx = relativePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                return relativePath[(idx + marker.Length)..];
            }
        }

        return relativePath;
    }

    private static void TryDeleteEmptyDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir, recursive: false);
            }
        }
        catch { }
    }

    public static string FormatDisplayName(string rawName)
    {
        var cleaned = DisplayPrefixRegex.Replace(rawName, string.Empty);
        if (cleaned.EndsWith("_BP_P", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^5] + " BP";
        else if (cleaned.EndsWith("_P", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^2];

        cleaned = cleaned.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? rawName : cleaned;
    }

    public static int? ExtractPakChunkOrder(string name)
    {
        var m = PakChunkOrderRegex.Match(name);
        return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : null;
    }

    private static void DeletePath(string path, bool sendToRecycleBin)
    {
        if (!sendToRecycleBin)
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
            return;
        }

        if (SendToRecycleBin(path)) return;

        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }

    private static bool SendToRecycleBin(string path)
    {
        var shf = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
        };
        return SHFileOperation(ref shf) == 0;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
