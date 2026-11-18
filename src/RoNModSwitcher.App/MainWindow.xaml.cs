using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using RoNModSwitcher.App.Bridge;
using RoNModSwitcher.Core.Services;

namespace RoNModSwitcher.App;

public partial class MainWindow : Window
{
    private const string VirtualOrigin = "https://app.ronmodswitcher.local";
    private const string ResourcePrefix = "RoNModSwitcher.App.wwwroot.";
    private const string DiscordInviteUrl = "https://discord.gg/29bndFuds3";

    private readonly AppController _controller;
    private readonly AppBridge _bridge;
    private readonly EventWaitHandle? _bringToFrontEvent;
    private readonly CancellationTokenSource _shutdownCts = new();
    private string? _ephemeralUserDataFolder;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(EventWaitHandle? bringToFrontEvent)
    {
        InitializeComponent();
        _bringToFrontEvent = bringToFrontEvent;

        _controller = new AppController();
        _bridge = new AppBridge(
            _controller,
            pickGameFolder: PickGameFolder,
            pickModFiles: PickModFiles,
            minimizeWindow: () => Dispatcher.Invoke(() => WindowState = WindowState.Minimized),
            copyToClipboard: text => Dispatcher.Invoke(() => Clipboard.SetText(text)));

        SourceInitialized += (_, _) => EnableDarkTitleBar();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;

        StartBringToFrontListener();
    }

    private void StartBringToFrontListener()
    {
        if (_bringToFrontEvent is null) return;

        var handles = new WaitHandle[] { _bringToFrontEvent, _shutdownCts.Token.WaitHandle };
        Task.Run(() =>
        {
            while (!_shutdownCts.IsCancellationRequested)
            {
                int signaled = WaitHandle.WaitAny(handles);
                if (signaled != 0) break;

                Dispatcher.Invoke(() =>
                {
                    if (WindowState == WindowState.Minimized)
                    {
                        WindowState = WindowState.Normal;
                    }
                    Show();
                    Activate();
                    Topmost = true;
                    Topmost = false;
                    Focus();
                });
            }
        });
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VosienStudio",
                "RoNModSwitcher");

            var primaryFolder = Path.Combine(baseAppData, "WebView2");
            await InitializeWebViewWithFallbackAsync(primaryFolder, baseAppData);

            var core = WebView.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#if DEBUG
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
#else
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
#endif

            core.AddWebResourceRequestedFilter($"{VirtualOrigin}/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += Core_WebResourceRequested;
            core.NavigationStarting += Core_NavigationStarting;
            core.WebMessageReceived += Core_WebMessageReceived;

            core.Navigate($"{VirtualOrigin}/index.html");
        }
        catch (Exception ex)
        {
            WebView.Visibility = Visibility.Collapsed;
            FallbackPanel.Visibility = Visibility.Visible;
            FallbackReasonText.Text =
                $"Could not initialize Microsoft Edge WebView2 Runtime.\n\nDetails: {ex.Message}";
        }
    }

    private async Task InitializeWebViewWithFallbackAsync(string primaryFolder, string baseAppData)
    {
        Directory.CreateDirectory(primaryFolder);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, primaryFolder);
                await WebView.EnsureCoreWebView2Async(env);
                return;
            }
            catch (COMException ex) when (attempt == 0 && (ex.HResult == unchecked((int)0x800700AA) || ex.HResult == unchecked((int)0x80070020)))
            {
                await Task.Delay(350);
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x800700AA) || ex.HResult == unchecked((int)0x80070020))
            {
                // Primary WebView2 user data folder is locked by a lingering process; use isolated fallback folder
                _ephemeralUserDataFolder = Path.Combine(baseAppData, $"WebView2_Session_{Environment.ProcessId}");
                Directory.CreateDirectory(_ephemeralUserDataFolder);
                var env = await CoreWebView2Environment.CreateAsync(null, _ephemeralUserDataFolder);
                await WebView.EnsureCoreWebView2Async(env);
                return;
            }
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _shutdownCts.Cancel();
        try { WebView?.Dispose(); } catch { }
        _shutdownCts.Dispose();

        if (!string.IsNullOrWhiteSpace(_ephemeralUserDataFolder))
        {
            try { Directory.Delete(_ephemeralUserDataFolder, recursive: true); } catch { }
        }
    }

    private void Core_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (!uri.GetLeftPart(UriPartial.Authority).Equals(VirtualOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relativePath = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            relativePath = "index.html";
        }

        var resourceName = ResourcePrefix + relativePath.Replace('/', '.');
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(
                Stream.Null, 404, "Not Found", "Content-Type: text/plain");
            return;
        }

        var contentType = GetMimeType(relativePath);
        e.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(
            stream,
            200,
            "OK",
            $"Content-Type: {contentType}\r\nAccess-Control-Allow-Origin: *\r\nCache-Control: no-cache");
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith(VirtualOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true });
        }
    }

    private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var rawJson = e.WebMessageAsJson;
        var responseJson = _bridge.HandleMessage(rawJson, e);
        WebView.CoreWebView2.PostWebMessageAsJson(responseJson);
    }

    private string? PickGameFolder()
    {
        string? result = null;
        Dispatcher.Invoke(() =>
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Ready or Not Installation Folder",
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == true)
            {
                result = dialog.FolderName;
            }
        });
        return result;
    }

    private string[] PickModFiles()
    {
        string[] result = Array.Empty<string>();
        Dispatcher.Invoke(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Ready or Not Mod Files (.pak / .ucas / .utoc / .zip)",
                Filter = "Mod Packages (*.pak;*.ucas;*.utoc;*.sig;*.zip)|*.pak;*.ucas;*.utoc;*.sig;*.zip|All files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                result = dialog.FileNames;
            }
        });
        return result;
    }

    private void DownloadWebView2_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://go.microsoft.com/fwlink/p/?LinkId=2124703",
            UseShellExecute = true
        });
    }

    private void OpenDiscordSupport_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = DiscordInviteUrl,
            UseShellExecute = true
        });
    }

    private void EnableDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int useDark = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, 19, ref useDark, sizeof(int));
            }

            // Win11 Build 22000+: Set DWMWA_CAPTION_COLOR (35) to #090807 (COLORREF 0x00070809)
            int captionColor = 0x00070809;
            DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int));
        }
        catch
        {
            // Ignore on unsupported OS builds
        }
    }

    private static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".json" => "application/json; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
