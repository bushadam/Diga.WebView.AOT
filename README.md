# Diga.WebView.AOT
AOT Version of [Diga.WebView2](https://github.com/ITAgnesmeyer/Diga.WebView2)

We have decided to separate this project from Diga.WebView2. The reasons for this are as follows:

1. **AOT Compilation**: Windows.Forms currently does not officially support Ahead-of-Time (AOT) compilation. It is unclear if or when this support will be implemented.

2. **Support by Other Frameworks**: Other projects, such as Avalonia, already support AOT compilation.

3. **Compatibility**: AOT compilation follows its own rules and is not backward compatible, which complicates integration into existing systems.

4. **Challenging Scope**: Achieving full coverage of the Diga.WebView2 project requires significant and challenging effort, making it a resource-intensive endeavor.

We plan to integrate as much of Diga.WebView2 into this project as possible. However, whether we will achieve complete coverage remains uncertain at this time.




## Projekte

- [TestWindow](TestWindow/README.md) => CoreWindowsWrapper basied WebBrowser APP with Diga.NativeControls.WebBrowser.AOT
- [MinimalWinForms](MinimalWinForms/README.md) => Minimal Windows Forms APP with Diga.WebView2.Wrapper.AOT
- [Diga.WebView2.Avalonia](Diga.WebView2.Avalonia/README.md) => Avalonia 12 host control (`AvaloniaWebView`), AOT + SimdSkia friendly
- [TestAvalonia](TestAvalonia/README.md) => Minimal Avalonia 12 APP (SimdSkia renderer) with Native AOT

## Avalonia host (fork addition)

This fork adds a framework-agnostic Avalonia host. It needs no WinForms/WPF:

```csharp
var webView = new AvaloniaWebView { Source = "https://www.bing.com" };
webView.WebViewCreated += (_, _) => { /* ready */ };
```

Requirements: `app.manifest` with `supportedOS` (otherwise `NativeControlHost` throws),
PerMonitorV2 DPI awareness, and `lib\Diga.WebView2.Native.dll` next to the executable.
Verified with `SimdSkia.Avalonia` (pure managed renderer) and Native AOT:
single-file exe ≈ 19 MB, no native Skia dependency.




