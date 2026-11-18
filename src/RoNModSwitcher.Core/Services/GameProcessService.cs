using System.Diagnostics;
using RoNModSwitcher.Core.Models;

namespace RoNModSwitcher.Core.Services;

public sealed class GameProcessService
{
    private readonly object _launchLock = new();
    private readonly Func<string?, bool>? _isGameRunningOverride;
    private readonly Action<ProcessStartInfo>? _processStarterOverride;
    private DateTimeOffset _lastLaunchUtc = DateTimeOffset.MinValue;

    public GameProcessService(
        Func<string?, bool>? isGameRunningOverride = null,
        Action<ProcessStartInfo>? processStarterOverride = null)
    {
        _isGameRunningOverride = isGameRunningOverride;
        _processStarterOverride = processStarterOverride;
    }

    private void StartProcess(ProcessStartInfo psi)
    {
        if (_processStarterOverride is not null)
        {
            _processStarterOverride(psi);
            return;
        }
        Process.Start(psi);
    }

    private static readonly string[] KnownProcessNames =
    {
        "ReadyOrNot",
        "ReadyOrNot-Win64-Shipping",
        "ReadyOrNotSteam-Win64-Shipping",
        "ReadyOrNotEGS",
        "ReadyOrNotEGS-Win64-Shipping"
    };

    public bool IsGameRunning(string? installPath = null)
    {
        if (_isGameRunningOverride is not null)
        {
            return _isGameRunningOverride(installPath);
        }

        foreach (var procName in KnownProcessNames)
        {
            try
            {
                var procs = Process.GetProcessesByName(procName);
                try
                {
                    if (procs.Length > 0)
                    {
                        return true;
                    }
                }
                finally
                {
                    foreach (var p in procs) p.Dispose();
                }
            }
            catch
            {
                // Ignore restricted process inspection
            }
        }

        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.ProcessName.StartsWith("ReadyOrNot", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore inaccessible processes
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    public OperationResult LaunchGame(GameInstallation installation, LaunchMode launchMode)
    {
        if (IsGameRunning(installation.InstallPath))
        {
            return OperationResult.Fail("GAME_ALREADY_RUNNING", "Ready or Not is already running.");
        }

        lock (_launchLock)
        {
            if ((DateTimeOffset.UtcNow - _lastLaunchUtc).TotalSeconds < 6)
            {
                return OperationResult.Fail("LAUNCH_COOLDOWN", "Game launch is already in progress. Please wait a few seconds.");
            }
            _lastLaunchUtc = DateTimeOffset.UtcNow;
        }

        try
        {
            if (launchMode == LaunchMode.PlatformUri && !string.IsNullOrWhiteSpace(installation.LaunchUri))
            {
                StartProcess(new ProcessStartInfo
                {
                    FileName = installation.LaunchUri,
                    UseShellExecute = true
                });
                return OperationResult.Ok("LAUNCHED_URI", $"Launching via {installation.DisplayLabel}...");
            }

            if (!string.IsNullOrWhiteSpace(installation.ExecutablePath) && File.Exists(installation.ExecutablePath))
            {
                var args = launchMode switch
                {
                    LaunchMode.DirectDx11 => "-dx11",
                    LaunchMode.DirectDx12 => "-dx12",
                    _ => string.Empty
                };

                StartProcess(new ProcessStartInfo
                {
                    FileName = installation.ExecutablePath,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(installation.ExecutablePath) ?? installation.InstallPath,
                    UseShellExecute = true
                });
                return OperationResult.Ok("LAUNCHED_EXE", "Launching Ready or Not executable...");
            }

            if (!string.IsNullOrWhiteSpace(installation.LaunchUri))
            {
                StartProcess(new ProcessStartInfo
                {
                    FileName = installation.LaunchUri,
                    UseShellExecute = true
                });
                return OperationResult.Ok("LAUNCHED_URI", $"Launching via {installation.DisplayLabel}...");
            }

            return OperationResult.Fail("LAUNCH_TARGET_MISSING", "No valid launch executable or platform URI found.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("LAUNCH_FAILED", ex.Message);
        }
    }
}
