using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RoNModSwitcher.Core.Models;

namespace RoNModSwitcher.Core.Services;

public sealed class ProfileShareService
{
    private const string Prefix = "RONMODS1:";
    private static readonly Regex ShareTokenRegex = new(@"RONMODS1:([A-Za-z0-9\-_]+)", RegexOptions.Compiled);

    private sealed class SharePayload
    {
        public int V { get; set; } = 1;
        public string Name { get; set; } = string.Empty;
        public bool DisableChecksum { get; set; } = true;
        public bool EnableModIo { get; set; } = true;
        public List<string> Mods { get; set; } = new();
    }

    public string ExportShareText(ModProfile profile, IReadOnlyList<ModUnit> allLocalMods)
    {
        var enabledSet = new HashSet<string>(profile.EnabledModIds, StringComparer.OrdinalIgnoreCase);
        var disabledSet = new HashSet<string>(profile.DisabledModIds, StringComparer.OrdinalIgnoreCase);

        var includedMods = new List<ModUnit>();
        if (profile.IsModdedMode)
        {
            foreach (var mod in allLocalMods)
            {
                bool isIncluded = profile.TrackNewModsAsEnabled
                    ? !disabledSet.Contains(mod.Id)
                    : enabledSet.Contains(mod.Id);

                if (isIncluded)
                {
                    includedMods.Add(mod);
                }
            }
        }

        var payload = new SharePayload
        {
            V = 1,
            Name = profile.Name,
            DisableChecksum = profile.DisableChecksum,
            EnableModIo = profile.EnableModIo,
            Mods = includedMods.Select(m => m.RawName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
        };

        var json = JsonSerializer.Serialize(payload);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var sb = new StringBuilder();
        sb.AppendLine($"[RoN Mod Switcher Loadout — {profile.Name}]");
        sb.AppendLine($"Mods: {payload.Mods.Count} | Checksum: {(profile.DisableChecksum ? "Off" : "On")} | mod.io: {(profile.EnableModIo ? "On" : "Off")}");
        if (payload.Mods.Count > 0)
        {
            foreach (var m in payload.Mods)
            {
                sb.AppendLine($"- {m}");
            }
        }
        sb.Append($"{Prefix}{b64}");
        return sb.ToString();
    }

    public OperationResult CompareSharedText(string rawInput, IReadOnlyList<ModUnit> allLocalMods)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return OperationResult.Fail("SHARE_INPUT_EMPTY", "Paste a shared loadout text or code first.");
        }

        SharePayload? payload = TryDecodeToken(rawInput) ?? TryParsePlainList(rawInput);
        if (payload is null)
        {
            return OperationResult.Fail("SHARE_INPUT_INVALID", "Could not recognize a valid RoN Mod Switcher share code or .pak list.");
        }

        var localByRaw = new Dictionary<string, ModUnit>(StringComparer.OrdinalIgnoreCase);
        var localByStem = new Dictionary<string, ModUnit>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in allLocalMods)
        {
            localByRaw.TryAdd(mod.RawName, mod);
            localByStem.TryAdd(Path.GetFileNameWithoutExtension(mod.RawName), mod);
            localByStem.TryAdd(mod.DisplayName, mod);
        }

        var matching = new List<string>();
        var missing = new List<string>();
        var matchedIds = new List<string>();
        var matchedIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var remoteName in payload.Mods.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var trimmed = remoteName.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            ModUnit? localMatch = null;
            if (!localByRaw.TryGetValue(trimmed, out localMatch))
            {
                var stem = Path.GetFileNameWithoutExtension(trimmed);
                localByStem.TryGetValue(stem, out localMatch);
            }

            if (localMatch is not null)
            {
                matching.Add(localMatch.RawName);
                if (matchedIdSet.Add(localMatch.Id))
                {
                    matchedIds.Add(localMatch.Id);
                }
            }
            else
            {
                missing.Add(trimmed);
            }
        }

        var extra = allLocalMods
            .Where(m => m.IsEnabled && !matchedIdSet.Contains(m.Id))
            .Select(m => m.RawName)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new ProfileCompareResult
        {
            ProfileName = string.IsNullOrWhiteSpace(payload.Name) ? "Shared Co-Op" : payload.Name,
            DisableChecksum = payload.DisableChecksum,
            EnableModIo = payload.EnableModIo,
            MatchingMods = matching,
            MissingLocalMods = missing,
            ExtraLocalEnabledMods = extra,
            MatchedLocalModIds = matchedIds
        };

        return OperationResult.Ok("SHARE_COMPARED", "Loadout compared.", data: result);
    }

    private static SharePayload? TryDecodeToken(string rawInput)
    {
        var match = ShareTokenRegex.Match(rawInput);
        if (!match.Success) return null;

        try
        {
            var token = match.Groups[1].Value.Replace('-', '+').Replace('_', '/');
            int pad = (4 - token.Length % 4) % 4;
            token += new string('=', pad);

            var bytes = Convert.FromBase64String(token);
            var json = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<SharePayload>(json);
        }
        catch
        {
            return null;
        }
    }

    private static SharePayload? TryParsePlainList(string rawInput)
    {
        var lines = rawInput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().TrimStart('-', '*', '•', ' '))
            .Where(l => l.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) ||
                        l.EndsWith(".ucas", StringComparison.OrdinalIgnoreCase) ||
                        l.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (lines.Count == 0) return null;

        return new SharePayload
        {
            Name = "Imported List",
            DisableChecksum = true,
            EnableModIo = true,
            Mods = lines
        };
    }
}
