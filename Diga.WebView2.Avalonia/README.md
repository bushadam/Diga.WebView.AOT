# Diga.WebView2.Avalonia

Avalonia 12 host control for `Diga.WebView2.Wrapper` (AOT version of Diga.WebView2).

- Pure managed host: no WinForms, no WPF.
- Works with **Native AOT** (`IsAotCompatible=true`).
- Works with the **SimdSkia** Avalonia renderer (pure managed Skia, no native `libSkiaSharp`).
- Includes a lightweight multi-tab host (`WebViewTabHost`).

## Usage

```csharp
using Avalonia;
using Diga.WebView2.Avalonia;
using SimdSkia.Avalonia;

AppBuilder.Configure<App>()
    .UseSimdSkiaDesktop()      // or .UsePlatformDetect()
    .StartWithClassicDesktopLifetime(args);
```

```csharp
var webView = new AvaloniaWebView
{
    Source = "https://www.bing.com",
    UserDataFolder = @"D:\Data\WebView2",   // optional
    IgnoreCertificateErrors = true          // intranet self-signed hosts
};

webView.WebViewCreated += (_, _) => { /* ready */ };
webView.NavigationStarting += (_, e) => Console.WriteLine(e.Uri);
webView.NewWindowRequested += (_, e) => { e.Handled = true; /* open in new tab */ };

// full Diga API is always available:
var core = webView.CoreView;                 // WebView2View (Settings / CookieManager / ExecuteScriptAsync)
var cookies = webView.CookieManager;         // CookieManager
await webView.ExecuteScriptAsync("document.title");
webView.NavigateToString("<h1>hello</h1>");
webView.ZoomFactor = 1.25;
webView.OpenDevTools();
webView.PrintToPdf(@"D:\tmp\page.pdf");
```

## Features

| Area | Members |
| --- | --- |
| Navigation | `Source`, `Navigate`, `NavigateToString`, `Reload`, `Stop`, `GoBack`, `GoForward` |
| Script / messages | `ExecuteScriptAsync`, `PostWebMessageAsString`, `WebMessageReceived` |
| Zoom | `ZoomFactor`, `ZoomIn`, `ZoomOut`, `ResetZoom` |
| Debug / print | `OpenDevTools`, `AreDevToolsEnabled`, `PrintToPdf`, `PrintToPdfCompleted` |
| Downloads | `DownloadStarting`, `OpenDefaultDownloadDialog` |
| Windows | `NewWindowRequested`, `OpenNewWindowInSameView`, `WindowCloseRequested`, `ContainsFullScreenElementChanged` |
| Security | `IgnoreCertificateErrors`, `ServerCertificateErrorDetected`, `AreDefaultContextMenusEnabled` |
| Robustness | `ReloadOnProcessFailed`, `ProcessFailed`, `AcceleratorKeyPressed` (F5 / Ctrl+R / F12 / Ctrl+±0 handled by default) |
| Diagnostics | `Trace` (static hook), `RefreshNativeBounds`, `IsWebViewReady` |

## Multi-tab host

```csharp
var tabs = new WebViewTabHost();
var first = tabs.AddTab("https://example.com");
var second = tabs.AddTab();               // blank tab
second.NavigateToString("<h1>hello</h1>");

tabs.SelectTab(first);
tabs.SelectNext();                        // Ctrl+Tab behaviour
tabs.CloseSelectedTab();
```

Implementation notes:

- TabControl is **not** used: it detaches non-selected content, which would destroy and recreate the
  `CoreWebView2` (page state lost).
- All tabs stay attached and arranged; switching uses **`ICoreWebView2Controller.IsVisible`**
  (exposed as `WebView2Control.IsVisible`).
- Do **not** hide the parent HWND (`ShowWindow`) or move windows in the z-order: hiding the parent makes
  WebView2's render widget stop painting, and re-showing it leaves a blank page. This was verified
  during development — the controller-level visibility is the only reliable way.

## Requirements (important)

1. **app.manifest with supportedOS** — without it `NativeControlHost` throws
   `Unable to create child window for native control host`.
   See `../TestAvalonia/app.manifest`.
2. **PerMonitorV2 DPI awareness** — otherwise WebView2 and Avalonia coordinates drift apart.
3. **`lib\Diga.WebView2.Native.dll`** must sit next to the executable
   (the wrapper loads it from the relative path `lib\Diga.WebView2.Native.dll`).

## Known limitations

- **Airspace**: WebView2 is a separate HWND; Avalonia popups (Flyout / Menu / ToolTip / modal overlay)
  cannot be drawn on top of the web area. Use a separate window, or temporarily hide the web view
  (e.g. `Controller.IsVisible = false`) while showing overlays.
- **Focus**: keyboard focus is forwarded on pointer press (`FocusWebView()`); Tab traversal between
  Avalonia controls and the web content has to be wired by the host application.
- One `AvaloniaWebView` = one `CoreWebView2Controller` = one browser process group (memory cost per tab).
## Renderer caveats (SimdSkia)

- The **SimdSkia** backend cannot synthesize bold/italic faces and throws
  `System.NotSupportedException: Synthetic bold/italic font outlines are not implemented`
  when a `TextBlock` requests a weight the installed font family does not provide
  (e.g. `FontWeight="SemiBold"` on a family with only Regular/Bold).
  Use a font family that really ships the requested face, or avoid non-default weights.
- This concerns Avalonia-drawn chrome only; WebView2 renders itself and is unaffected.
