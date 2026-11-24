using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Diga.WebView2.Wrapper;
using Diga.WebView2.Wrapper.EventArguments;

namespace Diga.WebView2.Avalonia;

/// <summary>
/// Avalonia 12 宿主控件：在 <see cref="NativeControlHost"/> 里创建 Win32 子窗口，
/// 交给 <see cref="WebView2Control"/> 承载 WebView2。
///
/// 使用要求（重要）：
/// 1. 程序必须带 app.manifest 并声明 supportedOS（Win10/11），否则 NativeControlHost 会抛
///    "Unable to create child window for native control host"；
/// 2. 建议声明 PerMonitorV2 DPI 感知，否则 WebView2 与 Avalonia 坐标会错位；
/// 3. 输出目录必须包含 lib\Diga.WebView2.Native.dll。
///
/// 已知限制：WebView2 是独立 HWND，Avalonia 的弹层（Flyout/Menu/ToolTip）无法覆盖在网页区域上。
/// </summary>
public class AvaloniaWebView : NativeControlHost
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(int exStyle, string className, string? windowName, int style,
        int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hwnd);

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

    static AvaloniaWebView()
    {
        SourceProperty.Changed.AddClassHandler<AvaloniaWebView>((view, args) => view.OnSourceChanged(args));
        ZoomFactorProperty.Changed.AddClassHandler<AvaloniaWebView>((view, args) => view.OnZoomFactorChanged(args));
    }

    /// <summary>底层的 Diga WebView2 控制对象；未创建前为 null，可用于访问完整 API。</summary>
    public WebView2Control? WebViewControl { get; private set; }

    /// <summary>WebView2 的 CoreWebView2 视图（脚本、设置、Cookie 等）。</summary>
    public WebView2View? CoreView => WebViewControl?.WebView;

    public bool IsWebViewReady => WebViewControl != null;

    /// <summary>要加载的 URL；控件就绪前后赋值都可以。</summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>WebView2 用户数据目录；留空则使用 %LOCALAPPDATA%\Diga.WebView2.Avalonia\WebView2Data。</summary>
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

    // ---- WebView2 事件（参数类型直接复用 Diga.WebView2.Wrapper.EventArguments）----
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

    public AvaloniaWebView()
    {
        Focusable = true;
    }

    // ---- 导航 / 脚本 / 消息 ----
    public void Navigate(string url)
    {
        if (WebViewControl == null)
        {
            _pendingSource = url;
            return;
        }

        WebViewControl.Navigate(url);
    }

    public void NavigateToString(string html)
    {
        if (WebViewControl == null)
        {
            return; // 未就绪时忽略，调用方可挂在 WebViewCreated 上
        }

        WebViewControl.NavigateToString(html);
    }

    public void Reload() => WebViewControl?.Reload();

    public void GoBack() => WebViewControl?.GoBack();

    public void GoForward() => WebViewControl?.GoForward();

    public async Task<string?> ExecuteScriptAsync(string javaScript)
    {
        if (CoreView == null)
            return null;

        var result = await CoreView.ExecuteScriptAsync(javaScript).ConfigureAwait(true);
        return result;
    }

    public void PostWebMessageAsString(string message) => WebViewControl?.PostWebMessageAsString(message);

    /// <summary>把键盘焦点交给 WebView2（宿主被点击时自动调用）。</summary>
    public void FocusWebView()
    {
        if (_childHwnd != IntPtr.Zero)
            SetFocus(_childHwnd);
        WebViewControl?.MoveFocusToWebView();
    }

    // ---- 生命周期 ----
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _childHwnd = CreateWindowEx(0, "STATIC", null,
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, 200, 200, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_childHwnd == IntPtr.Zero)
            throw new InvalidOperationException("CreateWindowEx 失败，错误码 " + Marshal.GetLastWin32Error());

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
            // 关闭阶段忽略
        }

        WebViewControl = null;
        if (_childHwnd != IntPtr.Zero)
        {
            DestroyWindow(_childHwnd);
            _childHwnd = IntPtr.Zero;
        }

        base.DestroyNativeControlCore(control);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        try
        {
            WebViewControl?.DockToParent();
        }
        catch
        {
            // 未就绪时忽略
        }

        return size;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        FocusWebView();
    }

    private void OnSourceChanged(AvaloniaPropertyChangedEventArgs args)
    {
        var url = args.NewValue as string;
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (WebViewControl == null)
        {
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
        control.NavigationCompleted += (_, e) => NavigationCompleted?.Invoke(this, e);
        control.SourceChanged += (_, e) => SourceChanged?.Invoke(this, e);
        control.DocumentTitleChanged += (_, e) => DocumentTitleChanged?.Invoke(this, e);
        control.NewWindowRequested += (_, e) => NewWindowRequested?.Invoke(this, e);
        control.WebMessageReceived += (_, e) => WebMessageReceived?.Invoke(this, e);
        control.DownloadStarting += (_, e) => DownloadStarting?.Invoke(this, e);
        control.ProcessFailed += (_, e) => ProcessFailed?.Invoke(this, e);
        control.ContainsFullScreenElementChanged += (_, e) => ContainsFullScreenElementChanged?.Invoke(this, e);
        control.WindowCloseRequested += (_, e) => WindowCloseRequested?.Invoke(this, e);
        control.AcceleratorKeyPressed += (_, e) => AcceleratorKeyPressed?.Invoke(this, e);
    }

    private void UnwireEvents(WebView2Control control)
    {
        control.Created -= OnCreated;
    }

    private void OnCreated(object? sender, EventArgs e)
    {
        try
        {
            if (ZoomFactor > 0)
                WebViewControl!.ZoomFactor = ZoomFactor;

            var target = _pendingSource;
            if (string.IsNullOrWhiteSpace(target))
                target = Source;

            if (!string.IsNullOrWhiteSpace(target))
                WebViewControl!.Navigate(target);

            _pendingSource = null;
            DockToParentSafe();
            WebViewCreated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[AvaloniaWebView] Created 回调异常: " + ex);
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
            // 忽略
        }
    }
}
