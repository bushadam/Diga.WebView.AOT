# TestAvalonia

Minimal Avalonia 12 app hosting `Diga.WebView2.Avalonia` with the **SimdSkia** renderer,
published with **Native AOT**.

```powershell
dotnet run -c Debug
dotnet publish -c Release -r win-x64 -o publish
```

## Demo content

- toolbar: new / close tab, back, forward, reload, address bar, zoom −/+, DevTools, download dialog
- tab strip: switch tabs, selected tab highlighted
- tab 1: local HTML (Chinese text, UA display)
- tab 2: remote page (default `https://example.com`)

### Environment variables (useful for automated verification)

| Variable | Effect |
| --- | --- |
| `DEMO_URL=<url>` | override tab 2 URL, e.g. `http://127.0.0.1:18080/` for an offline test server |
| `DEMO_AUTOSWITCH=1` | switch to tab 2 automatically after 6 s |

Runtime log: `%LOCALAPPDATA%\TestAvalonia.run.log` (navigation results + `AvaloniaWebView.Trace` messages).

## Verified on Windows (WebView2 Runtime 158)

| Form | Result | Size |
|---|---|---|
| JIT (Debug/Release) | window + toolbar + tabs; local HTML and HTTP pages render; tab switching works | — |
| Native AOT single file | same behaviour, UA `Edg/158` | `TestAvalonia.exe` **19,341 KB** + `lib\Diga.WebView2.Native.dll` 1.7 MB |

No native Skia dependency is pulled in (`libSkiaSharp.dll` / `libHarfBuzzSharp.dll` are absent).

Do not remove `app.manifest`: Avalonia's `NativeControlHost` needs the `supportedOS`
declaration, and PerMonitorV2 DPI awareness keeps WebView2 aligned with Avalonia layout.
