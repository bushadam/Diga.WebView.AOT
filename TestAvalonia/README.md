# TestAvalonia

Minimal Avalonia 12 app hosting `Diga.WebView2.Avalonia` with the **SimdSkia** renderer,
published with **Native AOT**.

```powershell
dotnet run -c Debug
dotnet publish -c Release -r win-x64 -o publish
```

Verified on Windows (WebView2 Runtime 158):

| Form | Result | Size |
|---|---|---|
| JIT (Debug) | window + toolbar + WebView2, local HTML and remote pages render | — |
| Native AOT single file | same behaviour, UA `Edg/158` | `TestAvalonia.exe` **19,317 KB** + `lib\Diga.WebView2.Native.dll` 1.7 MB |

No native Skia dependency is pulled in (`libSkiaSharp.dll` / `libHarfBuzzSharp.dll` are absent).

Do not remove `app.manifest`: Avalonia's `NativeControlHost` needs the `supportedOS`
declaration, and PerMonitorV2 DPI awareness keeps WebView2 aligned with Avalonia layout.
