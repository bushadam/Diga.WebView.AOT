# Diga.WebView2.Avalonia

Avalonia 12 host control for `Diga.WebView2.Wrapper` (AOT version of Diga.WebView2).

- Pure managed host: no WinForms, no WPF.
- Works with **Native AOT** (`IsAotCompatible=true`).
- Works with the **SimdSkia** Avalonia renderer (pure managed Skia, no native `libSkiaSharp`).

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
    UserDataFolder = @"D:\Data\WebView2"   // optional
};

webView.WebViewCreated += (_, _) => { /* ready */ };
webView.NavigationStarting += (_, e) => Console.WriteLine(e.Uri);
webView.NewWindowRequested += (_, e) => { e.Handled = true; /* open in new tab */ };

// full Diga API is always available:
var core = webView.CoreView;                 // WebView2View (Settings / CookieManager / ExecuteScriptAsync)
await webView.ExecuteScriptAsync("document.title");
webView.NavigateToString("<h1>hello</h1>");
webView.ZoomFactor = 1.25;
```

## Requirements (important)

1. **app.manifest with supportedOS** — without it `NativeControlHost` throws
   `Unable to create child window for native control host`.
   See `../TestAvalonia/app.manifest`.
2. **PerMonitorV2 DPI awareness** — otherwise WebView2 and Avalonia coordinates drift apart.
3. **`lib\Diga.WebView2.Native.dll`** must sit next to the executable
   (the wrapper loads it from the relative path `lib\Diga.WebView2.Native.dll`).

## Known limitations

- **Airspace**: WebView2 is a separate HWND; Avalonia popups (Flyout / Menu / ToolTip / modal overlay)
  cannot be drawn on top of the web area. Use a separate window or hide the webview while showing overlays.
- **Focus**: keyboard focus is forwarded on pointer press (`FocusWebView()`); Tab traversal between
  Avalonia controls and the web content has to be wired by the host application.
- One `AvaloniaWebView` = one `CoreWebView2Controller` = one browser process group.
