using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace RoNModSwitcher.App;

public partial class App : Application
{
    public const string SingleInstanceMutexName = @"Local\VosienStudio.RoNModSwitcher.SingleInstance";
    public const string BringToFrontEventName = @"Local\VosienStudio.RoNModSwitcher.BringToFront";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _bringToFrontEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            if (HasVisibleExistingWindow())
            {
                TrySignalExistingInstance();
                _instanceMutex.Dispose();
                _instanceMutex = null;
                Shutdown(0);
                return;
            }
            // Existing process has no visible window (e.g. non-interactive or lingering); continue launching with WebView2 fallback
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        _bringToFrontEvent = new EventWaitHandle(false, EventResetMode.AutoReset, BringToFrontEventName);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogFatal("AppDomain.UnhandledException", args.ExceptionObject as Exception);

        DispatcherUnhandledException += (_, args) =>
        {
            LogFatal("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
            MessageBox.Show(
                $"Unexpected error:\n{args.Exception.Message}\n\nLog: {GetLogPath()}",
                "RoN Mod Switcher — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
            LogFatal("UnobservedTaskException", args.Exception);

        base.OnStartup(e);

        try
        {
            var window = new MainWindow(_bringToFrontEvent);
            window.Show();
        }
        catch (Exception ex)
        {
            LogFatal("MainWindow.OnStartup", ex);
            MessageBox.Show(
                $"Startup error:\n{ex.Message}",
                "RoN Mod Switcher — Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _bringToFrontEvent?.Dispose(); } catch { }
        try
        {
            if (_instanceMutex is not null)
            {
                _instanceMutex.ReleaseMutex();
                _instanceMutex.Dispose();
            }
        }
        catch { }

        base.OnExit(e);
    }

    private static bool HasVisibleExistingWindow()
    {
        try
        {
            var currentPid = Environment.ProcessId;
            foreach (var proc in Process.GetProcessesByName("RoNModSwitcher"))
            {
                try
                {
                    if (proc.Id != currentPid &&
                        (proc.MainWindowHandle != IntPtr.Zero || (DateTime.Now - proc.StartTime).TotalSeconds < 5))
                    {
                        return true;
                    }
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch { }
        return false;
    }

    private static void TrySignalExistingInstance()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(BringToFrontEventName);
            ev.Set();
        }
        catch
        {
            // Existing instance might still be starting up
        }
    }

    private static string GetLogPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VosienStudio",
            "RoNModSwitcher");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "error.log");
    }

    private static void LogFatal(string source, Exception? ex)
    {
        try
        {
            File.AppendAllText(
                GetLogPath(),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\r\n{ex}\r\n\r\n");
        }
        catch { }
    }
}
