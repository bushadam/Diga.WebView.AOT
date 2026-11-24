using System;
using Avalonia;
using SimdSkia.Avalonia;

namespace TestAvalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<App>()
            .UseSimdSkiaDesktop()
            .StartWithClassicDesktopLifetime(args);
}
