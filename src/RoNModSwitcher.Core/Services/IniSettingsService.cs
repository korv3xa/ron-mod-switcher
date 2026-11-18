using System.Text;
using RoNModSwitcher.Core.Models;

namespace RoNModSwitcher.Core.Services;

public sealed class IniSettingsService
{
    public const string TargetSection = "[/Script/ReadyOrNot.ReadyOrNotGameUserSettings]";
    private const string KeyChecksum = "bServerSideChecksum";
    private const string KeyJoinModded = "bJoinModdedServers";
    private const string KeyAllowModded = "bAllowModdedPlayers";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public ChecksumSettings ReadChecksumSettings(string iniPath)
    {
        if (string.IsNullOrWhiteSpace(iniPath) || !File.Exists(iniPath))
        {
            return new ChecksumSettings
            {
                ConfigFound = false,
                ConfigPath = iniPath ?? string.Empty
            };
        }

        var lines = File.ReadAllLines(iniPath, Encoding.UTF8);
        var (startIdx, endIdx) = FindSectionRange(lines, TargetSection);

        bool? checksum = null;
        bool? joinModded = null;
        bool? allowModded = null;

        if (startIdx >= 0)
        {
            ReadKeysInRange(lines, startIdx + 1, endIdx, ref checksum, ref joinModded, ref allowModded);
        }

        // Fallback: check whole file if any key was missing from target section
        if (checksum is null || joinModded is null || allowModded is null)
        {
            ReadKeysInRange(lines, 0, lines.Length, ref checksum, ref joinModded, ref allowModded);
        }

        var backupPath = iniPath + ".ronswitch.bak";
        return new ChecksumSettings
        {
            ConfigFound = true,
            ConfigPath = iniPath,
            ServerSideChecksum = checksum ?? true,
            JoinModdedServers = joinModded ?? false,
            AllowModdedPlayers = allowModded ?? false,
            HasBackup = File.Exists(backupPath),
            BackupPath = File.Exists(backupPath) ? backupPath : null
        };
    }

    public OperationResult RestoreBackup(string iniPath)
    {
        if (string.IsNullOrWhiteSpace(iniPath))
        {
            return OperationResult.Fail("INI_PATH_EMPTY", "GameUserSettings.ini path is empty.");
        }

        var backupPath = iniPath + ".ronswitch.bak";
        if (!File.Exists(backupPath))
        {
            return OperationResult.Fail("BACKUP_NOT_FOUND", "No GameUserSettings.ini backup file found.");
        }

        try
        {
            File.Copy(backupPath, iniPath, overwrite: true);
            return OperationResult.Ok("INI_BACKUP_RESTORED", "GameUserSettings.ini restored from backup.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("INI_RESTORE_FAILED", ex.Message, backupPath);
        }
    }

    public OperationResult WriteChecksumSettings(string iniPath, bool disableChecksum)
    {
        if (string.IsNullOrWhiteSpace(iniPath))
        {
            return OperationResult.Fail("INI_PATH_EMPTY", "GameUserSettings.ini path is empty.");
        }

        if (!File.Exists(iniPath))
        {
            return OperationResult.Fail("INI_NOT_FOUND", "GameUserSettings.ini was not found. Launch the game once so it generates the config file.", iniPath);
        }

        try
        {
            var raw = File.ReadAllText(iniPath, Encoding.UTF8);
            var newline = raw.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var hadTrailingNewline = raw.EndsWith('\n');

            var backupPath = iniPath + ".ronswitch.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(iniPath, backupPath, overwrite: false);
            }

            var lines = SplitLines(raw);

            var targetValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [KeyChecksum] = disableChecksum ? "False" : "True",
                [KeyJoinModded] = disableChecksum ? "True" : "False",
                [KeyAllowModded] = disableChecksum ? "True" : "False"
            };

            var updatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var (startIdx, endIdx) = FindSectionRange(lines, TargetSection);

            if (startIdx >= 0)
            {
                for (int i = startIdx + 1; i < endIdx; i++)
                {
                    if (TryParseKeyValue(lines[i], out var key, out _))
                    {
                        if (targetValues.TryGetValue(key, out var newVal))
                        {
                            lines[i] = $"{key}={newVal}";
                            updatedKeys.Add(key);
                        }
                    }
                }

                // Insert missing keys right after the last non-empty line in the section
                var missing = targetValues.Keys.Where(k => !updatedKeys.Contains(k)).ToList();
                if (missing.Count > 0)
                {
                    int insertAt = endIdx;
                    while (insertAt > startIdx + 1 && string.IsNullOrWhiteSpace(lines[insertAt - 1]))
                    {
                        insertAt--;
                    }

                    foreach (var key in missing)
                    {
                        lines.Insert(insertAt, $"{key}={targetValues[key]}");
                        insertAt++;
                    }
                }
            }
            else
            {
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add(TargetSection);
                foreach (var kv in targetValues)
                {
                    lines.Add($"{kv.Key}={kv.Value}");
                }
            }

            var output = string.Join(newline, lines);
            if (hadTrailingNewline)
            {
                output += newline;
            }

            File.WriteAllText(iniPath, output, Utf8NoBom);
            return OperationResult.Ok("INI_UPDATED", disableChecksum ? "Checksum disabled for modded co-op." : "Checksum enabled (vanilla).");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("INI_WRITE_FAILED", ex.Message, iniPath);
        }
    }

    private static (int StartIndex, int EndIndex) FindSectionRange(IReadOnlyList<string> lines, string sectionHeader)
    {
        int start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Equals(sectionHeader, StringComparison.OrdinalIgnoreCase))
            {
                start = i;
                continue;
            }

            if (start >= 0 && trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                return (start, i);
            }
        }

        return start >= 0 ? (start, lines.Count) : (-1, -1);
    }

    private static void ReadKeysInRange(
        string[] lines,
        int start,
        int end,
        ref bool? checksum,
        ref bool? joinModded,
        ref bool? allowModded)
    {
        for (int i = start; i < end; i++)
        {
            if (!TryParseKeyValue(lines[i], out var key, out var val))
            {
                continue;
            }

            if (checksum is null && key.Equals(KeyChecksum, StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(val, out var b)) checksum = b;
            }
            else if (joinModded is null && key.Equals(KeyJoinModded, StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(val, out var b)) joinModded = b;
            }
            else if (allowModded is null && key.Equals(KeyAllowModded, StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(val, out var b)) allowModded = b;
            }
        }
    }

    private static bool TryParseKeyValue(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith('#') || trimmed.StartsWith('['))
        {
            return false;
        }

        var eq = trimmed.IndexOf('=');
        if (eq <= 0)
        {
            return false;
        }

        key = trimmed[..eq].Trim();
        value = trimmed[(eq + 1)..].Trim();
        return true;
    }

    private static List<string> SplitLines(string raw)
    {
        var normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n");
        var arr = normalized.Split('\n').ToList();
        if (raw.EndsWith('\n') && arr.Count > 0 && arr[^1] == string.Empty)
        {
            arr.RemoveAt(arr.Count - 1);
        }
        return arr;
    }
}
