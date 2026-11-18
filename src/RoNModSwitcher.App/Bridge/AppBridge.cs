using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using RoNModSwitcher.Core.Models;
using RoNModSwitcher.Core.Services;

namespace RoNModSwitcher.App.Bridge;

public sealed class AppBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly AppController _controller;
    private readonly Func<string?> _pickGameFolder;
    private readonly Func<string[]> _pickModFiles;
    private readonly Action _minimizeWindow;
    private readonly Action<string> _copyToClipboard;
    private readonly Action<string>? _openFolderOverride;
    private readonly Action<string>? _openExternalUrlOverride;

    public AppBridge(
        AppController controller,
        Func<string?> pickGameFolder,
        Func<string[]> pickModFiles,
        Action minimizeWindow,
        Action<string> copyToClipboard,
        Action<string>? openFolderOverride = null,
        Action<string>? openExternalUrlOverride = null)
    {
        _controller = controller;
        _pickGameFolder = pickGameFolder;
        _pickModFiles = pickModFiles;
        _minimizeWindow = minimizeWindow;
        _copyToClipboard = copyToClipboard;
        _openFolderOverride = openFolderOverride;
        _openExternalUrlOverride = openExternalUrlOverride;
    }

    public string HandleMessage(string rawJson, CoreWebView2WebMessageReceivedEventArgs? args = null)
    {
        string requestId = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // If JS passed a string via postMessage(JSON.stringify(...)), WebMessageAsJson wraps it as a JSON string
            JsonDocument? innerDoc = null;
            if (root.ValueKind == JsonValueKind.String)
            {
                var innerJson = root.GetString();
                if (!string.IsNullOrWhiteSpace(innerJson))
                {
                    innerDoc = JsonDocument.Parse(innerJson);
                    root = innerDoc.RootElement;
                }
            }

            using (innerDoc)
            {
                requestId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString() ?? string.Empty
                    : string.Empty;

                var action = root.TryGetProperty("action", out var actEl) && actEl.ValueKind == JsonValueKind.String
                    ? actEl.GetString() ?? string.Empty
                    : string.Empty;

                var payload = root.TryGetProperty("payload", out var payEl) ? payEl : default;

                var (opResult, includeSnapshot) = ExecuteAction(action, payload, args);
                var snapshot = includeSnapshot ? _controller.GetSnapshot() : null;

                return JsonSerializer.Serialize(new
                {
                    id = requestId,
                    action,
                    result = opResult,
                    snapshot
                }, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                id = requestId,
                action = "error",
                result = OperationResult.Fail("BRIDGE_ERROR", ex.Message),
                snapshot = (WorkspaceSnapshot?)null
            }, JsonOptions);
        }
    }

    private (OperationResult Result, bool IncludeSnapshot) ExecuteAction(
        string action,
        JsonElement payload,
        CoreWebView2WebMessageReceivedEventArgs? args)
    {
        switch (action)
        {
            case "getState":
                return (OperationResult.Ok("STATE_READY", "OK"), true);

            case "dismissNotice":
                return (_controller.DismissMigrationNotice(), true);

            case "setLanguage":
            {
                var lang = GetString(payload, "language") ?? "auto";
                return (_controller.SetLanguage(lang), true);
            }

            case "selectPlatform":
            {
                var pStr = GetString(payload, "platform");
                if (!Enum.TryParse<GamePlatform>(pStr, ignoreCase: true, out var platform))
                {
                    return (OperationResult.Fail("INVALID_PLATFORM", "Invalid platform value."), false);
                }
                return (_controller.SelectPlatform(platform), true);
            }

            case "browseGameFolder":
            {
                var picked = _pickGameFolder();
                if (string.IsNullOrWhiteSpace(picked))
                {
                    return (OperationResult.Ok("CANCELLED", "Folder selection cancelled."), false);
                }
                return (_controller.SetCustomGamePath(picked), true);
            }

            case "applyProfile":
            {
                var profileId = GetString(payload, "profileId") ?? string.Empty;
                var launch = GetBool(payload, "launchAfter");
                var res = _controller.ApplyProfile(profileId, launch);
                if (res.Success && launch)
                {
                    MaybeMinimizeOnLaunch();
                }
                return (res, true);
            }

            case "createProfile":
            {
                var name = GetString(payload, "name") ?? string.Empty;
                var desc = GetString(payload, "description") ?? string.Empty;
                var disableChecksum = GetBool(payload, "disableChecksum", defaultValue: true);
                var enableModIo = GetBool(payload, "enableModIo", defaultValue: true);
                return (_controller.CreateCustomProfile(name, desc, disableChecksum, enableModIo), true);
            }

            case "deleteProfile":
            {
                var profileId = GetString(payload, "profileId") ?? string.Empty;
                return (_controller.DeleteProfile(profileId), true);
            }

            case "saveProfileSelection":
            {
                var profileId = GetString(payload, "profileId") ?? string.Empty;
                return (_controller.SaveCurrentSelectionToProfile(profileId), true);
            }

            case "toggleWorkspace":
            {
                var enableModded = GetBool(payload, "enableModded");
                return (_controller.ToggleWorkspaceModded(enableModded), true);
            }

            case "toggleMod":
            {
                var modId = GetString(payload, "modId") ?? string.Empty;
                var enable = GetBool(payload, "enable");
                return (_controller.ToggleMod(modId, enable), true);
            }

            case "setBulkMods":
            {
                var ids = new List<string>();
                if (payload.ValueKind == JsonValueKind.Object &&
                    payload.TryGetProperty("modIds", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        var val = item.GetString();
                        if (!string.IsNullOrWhiteSpace(val)) ids.Add(val);
                    }
                }
                var enable = GetBool(payload, "enable");
                return (_controller.SetBulkModsEnabled(ids, enable), true);
            }

            case "restoreIniBackup":
                return (_controller.RestoreIniBackup(), true);

            case "setLaunchModeQuick":
            {
                var modeStr = GetString(payload, "launchMode");
                if (!Enum.TryParse<LaunchMode>(modeStr, ignoreCase: true, out var mode))
                {
                    return (OperationResult.Fail("INVALID_LAUNCH_MODE", "Invalid launch mode."), false);
                }
                return (_controller.SetLaunchModeQuick(mode), true);
            }

            case "exportProfile":
            {
                var profileId = GetString(payload, "profileId") ?? string.Empty;
                var copy = GetBool(payload, "copy", defaultValue: false);
                var res = _controller.ExportProfileShareText(profileId);
                if (res.Success && copy && res.Data is string text)
                {
                    _copyToClipboard(text);
                    res = res with { Code = "COPIED" };
                }
                return (res, false);
            }

            case "compareSharedProfile":
            {
                var rawText = GetString(payload, "rawText") ?? string.Empty;
                return (_controller.CompareSharedProfile(rawText), false);
            }

            case "applySharedToCoop":
            {
                var rawText = GetString(payload, "rawText") ?? string.Empty;
                return (_controller.ApplySharedToCoop(rawText), true);
            }

            case "copyToClipboard":
            {
                var text = GetString(payload, "text") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return (OperationResult.Fail("EMPTY_TEXT", "Nothing to copy."), false);
                }
                _copyToClipboard(text);
                return (OperationResult.Ok("COPIED", "Copied to clipboard."), false);
            }

            case "setChecksumDisabled":
            {
                var disable = GetBool(payload, "disable");
                return (_controller.SetChecksumDisabled(disable), true);
            }

            case "deleteMod":
            {
                var modId = GetString(payload, "modId") ?? string.Empty;
                return (_controller.DeleteMod(modId, sendToRecycleBin: true), true);
            }

            case "browseAndInstallMods":
            {
                var files = _pickModFiles();
                if (files.Length == 0)
                {
                    return (OperationResult.Ok("CANCELLED", "No files selected."), false);
                }
                return (_controller.InstallModFiles(files), true);
            }

            case "filesDropped":
            {
                var droppedPaths = ExtractDroppedPaths(args);
                if (droppedPaths.Count == 0)
                {
                    return (OperationResult.Fail("NO_DROPPED_PATHS", "Could not read dropped file paths from Windows Explorer."), false);
                }
                return (_controller.InstallModFiles(droppedPaths), true);
            }

            case "updatePreferences":
            {
                var modeStr = GetString(payload, "launchMode");
                if (!Enum.TryParse<LaunchMode>(modeStr, ignoreCase: true, out var launchMode))
                {
                    launchMode = LaunchMode.PlatformUri;
                }
                var minimize = GetBool(payload, "minimizeOnLaunch");
                var includeModIo = GetBool(payload, "includeModIoInSwitch", defaultValue: true);
                return (_controller.UpdatePreferences(launchMode, minimize, includeModIo), true);
            }

            case "launchGame":
            {
                var res = _controller.LaunchGame();
                if (res.Success)
                {
                    MaybeMinimizeOnLaunch();
                }
                return (res, true);
            }

            case "openModsFolder":
            {
                var snap = _controller.GetSnapshot();
                if (snap.ModSummary is null || string.IsNullOrWhiteSpace(snap.ModSummary.ActiveModsPath))
                {
                    return (OperationResult.Fail("NO_INSTALLATION", "No active mods folder found."), false);
                }
                Directory.CreateDirectory(snap.ModSummary.ActiveModsPath);
                OpenFolder(snap.ModSummary.ActiveModsPath);
                return (OperationResult.Ok("OPENED", "Opened ~mods folder."), false);
            }

            case "openDisabledFolder":
            {
                var snap = _controller.GetSnapshot();
                if (snap.ModSummary is null || string.IsNullOrWhiteSpace(snap.ModSummary.DisabledModsPath))
                {
                    return (OperationResult.Fail("NO_INSTALLATION", "No disabled mods folder found."), false);
                }
                Directory.CreateDirectory(snap.ModSummary.DisabledModsPath);
                OpenFolder(snap.ModSummary.DisabledModsPath);
                return (OperationResult.Ok("OPENED", "Opened disabled mods folder."), false);
            }

            case "openIniFolder":
            {
                var snap = _controller.GetSnapshot();
                if (snap.ActiveInstallation is null)
                {
                    return (OperationResult.Fail("NO_INSTALLATION", "No active installation."), false);
                }
                var dir = Path.GetDirectoryName(snap.ActiveInstallation.ConfigIniPath);
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    return (OperationResult.Fail("INI_FOLDER_MISSING", "Config folder does not exist yet. Launch the game once."), false);
                }
                OpenFolder(dir);
                return (OperationResult.Ok("OPENED", "Opened config folder."), false);
            }

            case "openExternalUrl":
            {
                var url = GetString(payload, "url");
                if (string.IsNullOrWhiteSpace(url) ||
                    !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps)
                {
                    return (OperationResult.Fail("INVALID_URL", "Only HTTPS URLs can be opened."), false);
                }

                if (_openExternalUrlOverride is not null)
                {
                    _openExternalUrlOverride(uri.ToString());
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = uri.ToString(),
                        UseShellExecute = true
                    });
                }
                return (OperationResult.Ok("OPENED_URL", "Opened external link."), false);
            }

            default:
                return (OperationResult.Fail("UNKNOWN_ACTION", $"Unknown action '{action}'."), false);
        }
    }

    private void MaybeMinimizeOnLaunch()
    {
        var snap = _controller.GetSnapshot();
        if (snap.Settings.MinimizeOnLaunch)
        {
            _minimizeWindow();
        }
    }

    private static List<string> ExtractDroppedPaths(CoreWebView2WebMessageReceivedEventArgs? args)
    {
        var paths = new List<string>();
        if (args is null) return paths;
        try
        {
            var additional = args.AdditionalObjects;
            if (additional is null) return paths;

            foreach (var obj in additional)
            {
                if (obj is CoreWebView2File file && !string.IsNullOrWhiteSpace(file.Path))
                {
                    paths.Add(file.Path);
                }
            }
        }
        catch
        {
            // Ignore restricted additional objects
        }
        return paths;
    }

    private void OpenFolder(string path)
    {
        if (_openFolderOverride is not null)
        {
            _openFolderOverride(path);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        return el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static bool GetBool(JsonElement el, string name, bool defaultValue = false)
    {
        if (el.ValueKind != JsonValueKind.Object) return defaultValue;
        if (!el.TryGetProperty(name, out var prop)) return defaultValue;
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }
}
