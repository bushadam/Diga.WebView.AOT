using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Diga.WebView2.Interop;
using Diga.WebView2.Wrapper;
using Diga.WebView2.Wrapper.EventArguments;

namespace Diga.WebView2.Avalonia;

/// <summary>
/// Avalonia 12 host control: creates a native Win32 child window inside a
/// <see cref="NativeControlHost"/> and hands it to <see cref="WebView2Control"/>.
///
/// Requirements:
/// 1. The application must ship an app.manifest declaring supportedOS (Win10/11);
///    otherwise NativeControlHost throws "Unable to create child window for native control host".
/// 2. Declare PerMonitorV2 DPI awareness, otherwise WebView2 and Avalonia coordinates drift apart.
/// 3. lib\Diga.WebView2.Native.dll must be deployed next to the executable.
///
/// Known limitation: WebView2 is a separate HWND, so Avalonia popups
/// (Flyout / Menu / ToolTip) cannot be drawn on top of the web area.
/// </summary>
public class AvaloniaWebView : NativeControlHost, IDisposable
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;

    private const int VK_CONTROL = 0x11;
    private const uint VK_F5 = 0x74;
    private const uint VK_F12 = 0x7B;
    private const uint VK_R = 0x52;
    private const uint VK_0 = 0x30;
    private const uint VK_OEM_PLUS = 0xBB;
    private const uint VK_OEM_MINUS = 0xBD;

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(int exStyle, string className, string? windowName, int style,
        int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int nCmdShow);

    private static readonly nint HWND_TOP = IntPtr.Zero;
    private static readonly nint HWND_BOTTOM = (IntPtr)1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<AvaloniaWebView, string?>(nameof(Source));

    public static readonly StyledProperty<string?> UserDataFolderProperty =
        AvaloniaProperty.Register<AvaloniaWebView, string?>(nameof(UserDataFolder));

    public static readonly StyledProperty<string?> BrowserExecutableFolderProperty =
        AvaloniaProperty.Register<AvaloniaWebView, string?>(nameof(BrowserExecutableFolder));

    public static readonly StyledProperty<string?> AdditionalBrowserArgumentsProperty =
        AvaloniaProperty.Register<AvaloniaWebView, string?>(nameof(AdditionalBrowserArguments));

    public static readonly StyledProperty<double> ZoomFactorProperty =
        AvaloniaProperty.Register<AvaloniaWebView, double>(nameof(ZoomFactor), 1.0);

    private nint _childHwnd;
    private string? _pendingSource;
    private string? _pendingHtml;

    static AvaloniaWebView()
    {
        SourceProperty.Changed.AddClassHandler<AvaloniaWebView>((view, args) => view.OnSourceChanged(args));
        ZoomFactorProperty.Changed.AddClassHandler<AvaloniaWebView>((view, args) => view.OnZoomFactorChanged(args));
    }

    /// <summary>Optional diagnostics hook; receives host/tab lifecycle messages.</summary>
    public static Action<string>? Trace { get; set; }

    /// <summary>Underlying Diga control; null until the native host is created.</summary>
    public WebView2Control? WebViewControl { get; private set; }

    /// <summary>CoreWebView2 view (script, settings, cookies...).</summary>
    public WebView2View? CoreView => WebViewControl?.WebView;

    /// <summary>WebView2 settings; null until ready.</summary>
    public WebView2Settings? Settings => CoreView?.Settings;

    /// <summary>Cookie manager; null until ready.</summary>
    public CookieManager? CookieManager => WebViewControl?.GetCookieManager;

    public bool IsWebViewReady => WebViewControl != null;

    // ---- Host configuration ----

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string? UserDataFolder
    {
        get => GetValue(UserDataFolderProperty);
        set => SetValue(UserDataFolderProperty, value);
    }

    public string? BrowserExecutableFolder
    {
        get => GetValue(BrowserExecutableFolderProperty);
        set => SetValue(BrowserExecutableFolderProperty, value);
    }

    public string? AdditionalBrowserArguments
    {
        get => GetValue(AdditionalBrowserArgumentsProperty);
        set => SetValue(AdditionalBrowserArgumentsProperty, value);
    }

    public double ZoomFactor
    {
        get => GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    /// <summary>Enable DevTools (default true).</summary>
    public bool AreDevToolsEnabled { get; set; } = true;

    /// <summary>Enable the default WebView2 context menu (default true).</summary>
    public bool AreDefaultContextMenusEnabled { get; set; } = true;

    /// <summary>Automatically accept server certificate errors (common for intranet self-signed hosts).</summary>
    public bool IgnoreCertificateErrors { get; set; }

    /// <summary>Reload automatically when the render process fails (default true).</summary>
    public bool ReloadOnProcessFailed { get; set; } = true;

    /// <summary>Handle window.open / target=_blank by navigating the current view (default true).</summary>
    public bool OpenNewWindowInSameView { get; set; } = true;

    /// <summary>Handle F5 / Ctrl+R / F12 / Ctrl+±0 shortcuts inside the control (default true).</summary>
    public bool HandleAcceleratorKeys { get; set; } = true;

    // ---- WebView2 events ----
    public event EventHandler? WebViewCreated;
    public event EventHandler<NavigationStartingEventArgs>? NavigationStarting;
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<SourceChangedEventArgs>? SourceChanged;
    public event EventHandler<WebView2EventArgs>? DocumentTitleChanged;
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
    public event EventHandler<DownloadStartingEventArgs>? DownloadStarting;
    public event EventHandler<ProcessFailedEventArgs>? ProcessFailed;
    public event EventHandler<WebView2EventArgs>? ContainsFullScreenElementChanged;
    public event EventHandler<WebView2EventArgs>? WindowCloseRequested;
    public event EventHandler<AcceleratorKeyPressedEventArgs>? AcceleratorKeyPressed;
    public event EventHandler<ServerCertificateErrorDetectedEventArgs>? ServerCertificateErrorDetected;
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;
    public event EventHandler<PrintToPdfCompleteEventArgs>? PrintToPdfCompleted;

    public AvaloniaWebView()
    {
        Focusable = true;
    }

    // ---- Navigation ----
    public void Navigate(string url)
    {
        if (WebViewControl == null)
        {
            _pendingHtml = null;
            _pendingSource = url;
            return;
        }

        WebViewControl.Navigate(url);
    }

    /// <summary>Load an HTML string; queued until the control is ready.</summary>
    public void NavigateToString(string html)
    {
        if (WebViewControl == null)
        {
            _pendingHtml = html;
            return;
        }

        WebViewControl.NavigateToString(html);
    }

    public void Reload() => WebViewControl?.Reload();

    public void Stop()
    {
        try
        {
            CoreView?.Stop();
        }
        catch
        {
            // not ready
        }
    }

    public void GoBack() => WebViewControl?.GoBack();

    public void GoForward() => WebViewControl?.GoForward();

    public void OpenDefaultDownloadDialog() => WebViewControl?.OpenDefaultDownloadDialog();

    // ---- Script / messages ----
    public async Task<string?> ExecuteScriptAsync(string javaScript)
    {
        if (CoreView == null)
            return null;

        return await CoreView.ExecuteScriptAsync(javaScript).ConfigureAwait(true);
    }

    public void PostWebMessageAsString(string message) => WebViewControl?.PostWebMessageAsString(message);

    // ---- Zoom / DevTools / printing ----
    public void ZoomIn() => ZoomFactor = Math.Min(5.0, Math.Round(ZoomFactor + 0.1, 2));

    public void ZoomOut() => ZoomFactor = Math.Max(0.25, Math.Round(ZoomFactor - 0.1, 2));

    public void ResetZoom() => ZoomFactor = 1.0;

    public void OpenDevTools()
    {
        try
        {
            CoreView?.OpenDevToolsWindow();
        }
        catch
        {
            // not ready
        }
    }

    /// <summary>Print the current page to PDF; raises <see cref="PrintToPdfCompleted"/> when done.</summary>
    public void PrintToPdf(string path) => CoreView?.PrintToPdf(path, null!);

    /// <summary>Hand keyboard focus to WebView2 (called automatically on pointer press).</summary>
    public void FocusWebView()
    {
        if (_childHwnd != IntPtr.Zero)
            SetFocus(_childHwnd);
        WebViewControl?.MoveFocusToWebView();
    }

    /// <summary>
    /// Show/hide the native child window. Used by <see cref="WebViewTabHost"/>:
    /// Avalonia visibility alone does not always hide the HWND.
    /// </summary>
    /// <summary>
    /// 切换本视图是否显示（多标签用）。使用 WebView2 控制器的 IsVisible，
    /// 而不是 ShowWindow/隐藏父窗口 —— 后者会让渲染控件进入隐藏状态，重新显示时白屏。
    /// </summary>
    internal void SetNativeVisible(bool visible)
    {
        try
        {
            if (WebViewControl != null)
                WebViewControl.IsVisible = visible;
        }
        catch
        {
            // 控制器尚未就绪
        }

        Trace?.Invoke($"SetNativeVisible({visible}) hwnd=0x{_childHwnd.ToInt64():X} active={IsTabActive}");
    }

    /// <summary>Re-align the WebView2 controller bounds with the current parent client area.</summary>
    public void RefreshNativeBounds()
    {
        // 切换标签时 Arrange 未必会再次触发，这里显式同步一次可见性与 Bounds。
        SetNativeVisible(IsTabActive);
        if (IsTabActive)
            DockToParentSafe();
    }

    // ---- Lifetime ----
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _childHwnd = CreateWindowEx(0, "STATIC", null,
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, 200, 200, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_childHwnd == IntPtr.Zero)
            throw new InvalidOperationException("CreateWindowEx failed, error " + Marshal.GetLastWin32Error());

        var options = new WebView2ControlCreateParams
        {
            UserDataFolder = ResolveUserDataFolder(),
            BrowserExecutableFolder = BrowserExecutableFolder ?? string.Empty,
            AdditionalBrowserArguments = AdditionalBrowserArguments ?? string.Empty
        };

        var control = new WebView2Control(_childHwnd, options);
        WebViewControl = control;
        WireEvents(control);

        return new PlatformHandle(_childHwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        DisposeWebView();
        base.DestroyNativeControlCore(control);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);

        // Avalonia 自身会在布局时把原生子窗口 Show 出来，所以“藏起来”的标签要在布局之后
        // 再隐藏一次，否则多个标签的 HWND 会叠在一起（后加的在上）。
        if (IsTabActive)
        {
            SetNativeVisible(true);
            DockToParentSafe();
        }
        else
        {
            SetNativeVisible(false);
        }

        Trace?.Invoke($"Arrange({finalSize.Width}x{finalSize.Height}) active={IsTabActive} hwnd=0x{_childHwnd.ToInt64():X}");

        return size;
    }

    /// <summary>
    /// 是否是当前显示的标签。由 <see cref="WebViewTabHost"/> 维护：
    /// 非活动标签在每次布局后都会被重新隐藏（Avalonia 布局会把原生窗口重新显示出来）。
    /// </summary>
    internal bool IsTabActive { get; set; } = true;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        FocusWebView();
    }

    /// <summary>Release WebView2 and the native child window (used when a tab is closed).</summary>
    public void Dispose()
    {
        DisposeWebView();
        GC.SuppressFinalize(this);
    }

    private void DisposeWebView()
    {
        try
        {
            if (WebViewControl != null)
            {
                UnwireEvents(WebViewControl);
                WebViewControl.Dispose();
            }
        }
        catch
        {
            // shutdown path
        }

        WebViewControl = null;
        if (_childHwnd != IntPtr.Zero)
        {
            DestroyWindow(_childHwnd);
            _childHwnd = IntPtr.Zero;
        }
    }

    private void OnSourceChanged(AvaloniaPropertyChangedEventArgs args)
    {
        var url = args.NewValue as string;
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (WebViewControl == null)
        {
            _pendingHtml = null;
            _pendingSource = url;
            return;
        }

        WebViewControl.Navigate(url);
    }

    private void OnZoomFactorChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (WebViewControl == null)
            return;

        if (args.NewValue is double zoom && zoom > 0)
            WebViewControl.ZoomFactor = zoom;
    }

    private string ResolveUserDataFolder()
    {
        var folder = UserDataFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Diga.WebView2.Avalonia",
                "WebView2Data");
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    private void WireEvents(WebView2Control control)
    {
        control.Created += OnCreated;
        control.NavigateStart += (_, e) => NavigationStarting?.Invoke(this, e);
        control.NavigationCompleted += OnNavigationCompleted;
        control.SourceChanged += (_, e) => SourceChanged?.Invoke(this, e);
        control.DocumentTitleChanged += (_, e) => DocumentTitleChanged?.Invoke(this, e);
        control.NewWindowRequested += OnNewWindowRequested;
        control.WebMessageReceived += (_, e) => WebMessageReceived?.Invoke(this, e);
        control.DownloadStarting += (_, e) => DownloadStarting?.Invoke(this, e);
        control.ProcessFailed += OnProcessFailed;
        control.ContainsFullScreenElementChanged += (_, e) => ContainsFullScreenElementChanged?.Invoke(this, e);
        control.WindowCloseRequested += (_, e) => WindowCloseRequested?.Invoke(this, e);
        control.AcceleratorKeyPressed += OnAcceleratorKeyPressed;
        control.ServerCertificateErrorDetected += OnServerCertificateErrorDetected;
        control.ContextMenuRequested += (_, e) => ContextMenuRequested?.Invoke(this, e);
        control.PrintToPdfCompleted += (_, e) => PrintToPdfCompleted?.Invoke(this, e);
    }

    private void UnwireEvents(WebView2Control control)
    {
        control.Created -= OnCreated;
        control.NavigationCompleted -= OnNavigationCompleted;
        control.NewWindowRequested -= OnNewWindowRequested;
        control.ProcessFailed -= OnProcessFailed;
        control.AcceleratorKeyPressed -= OnAcceleratorKeyPressed;
        control.ServerCertificateErrorDetected -= OnServerCertificateErrorDetected;
    }

    private void OnCreated(object? sender, EventArgs e)
    {
        try
        {
            ApplySettings();

            if (ZoomFactor > 0)
                WebViewControl!.ZoomFactor = ZoomFactor;

            var html = _pendingHtml;
            _pendingHtml = null;

            if (!string.IsNullOrEmpty(html))
            {
                WebViewControl!.NavigateToString(html);
            }
            else
            {
                var target = _pendingSource;
                if (string.IsNullOrWhiteSpace(target))
                    target = Source;

                if (!string.IsNullOrWhiteSpace(target))
                    WebViewControl!.Navigate(target);
            }

            _pendingSource = null;
            DockToParentSafe();
            WebViewCreated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[AvaloniaWebView] Created callback failed: " + ex);
        }
    }

    private void ApplySettings()
    {
        var settings = Settings;
        if (settings == null)
            return;

        settings.AreDevToolsEnabled = AreDevToolsEnabled;
        settings.AreDefaultContextMenusEnabled = AreDefaultContextMenusEnabled;
    }

    private void OnNavigationCompleted(object? sender, NavigationCompletedEventArgs e)
    {
        NavigationCompleted?.Invoke(this, e);
    }

    private void OnNewWindowRequested(object? sender, NewWindowRequestedEventArgs e)
    {
        try
        {
            NewWindowRequested?.Invoke(this, e);

            if (OpenNewWindowInSameView && !e.Handled)
            {
                e.Handled = true;
                var uri = e.Uri;
                if (!string.IsNullOrWhiteSpace(uri))
                    Navigate(uri);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[AvaloniaWebView] NewWindowRequested failed: " + ex);
        }
    }

    private void OnProcessFailed(object? sender, ProcessFailedEventArgs e)
    {
        ProcessFailed?.Invoke(this, e);

        if (ReloadOnProcessFailed && IsWebViewReady)
        {
            try
            {
                Reload();
            }
            catch
            {
                // ignore
            }
        }
    }

    private void OnServerCertificateErrorDetected(object? sender, ServerCertificateErrorDetectedEventArgs e)
    {
        ServerCertificateErrorDetected?.Invoke(this, e);

        if (!IgnoreCertificateErrors)
            return;

        try
        {
            e.Action = COREWEBVIEW2_SERVER_CERTIFICATE_ERROR_ACTION
                .COREWEBVIEW2_SERVER_CERTIFICATE_ERROR_ACTION_ALWAYS_ALLOW;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[AvaloniaWebView] certificate error handling failed: " + ex);
        }
    }

    private void OnAcceleratorKeyPressed(object? sender, AcceleratorKeyPressedEventArgs e)
    {
        AcceleratorKeyPressed?.Invoke(this, e);

        if (!HandleAcceleratorKeys || e.Handled)
            return;

        if (e.KeyEventKind != COREWEBVIEW2_KEY_EVENT_KIND.COREWEBVIEW2_KEY_EVENT_KIND_KEY_DOWN &&
            e.KeyEventKind != COREWEBVIEW2_KEY_EVENT_KIND.COREWEBVIEW2_KEY_EVENT_KIND_SYSTEM_KEY_DOWN)
            return;

        var ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;

        switch (e.VirtualKey)
        {
            case VK_F12:
                e.Handled = true;
                OpenDevTools();
                break;

            case VK_F5:
                e.Handled = true;
                Reload();
                break;

            case VK_R when ctrl:
                e.Handled = true;
                Reload();
                break;

            case VK_0 when ctrl:
                e.Handled = true;
                ResetZoom();
                break;

            case VK_OEM_PLUS when ctrl:
                e.Handled = true;
                ZoomIn();
                break;

            case VK_OEM_MINUS when ctrl:
                e.Handled = true;
                ZoomOut();
                break;
        }
    }

    private void DockToParentSafe()
    {
        try
        {
            WebViewControl?.DockToParent();
        }
        catch
        {
            // not ready
        }
    }
}
