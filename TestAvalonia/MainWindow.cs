using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Diga.WebView2.Avalonia;

namespace TestAvalonia;

internal sealed class MainWindow : Window
{
    private const string LocalHtml = """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>Diga.WebView2.Avalonia</title></head>
        <body style="margin:0;font-family:'Microsoft YaHei UI',sans-serif;background:#0f2b46;color:#fff">
          <div style="padding:36px">
            <div style="font-size:32px;font-weight:700;color:#8fd0ff">Diga.WebView2.Avalonia.AOT</div>
            <div style="font-size:20px;margin-top:14px">Avalonia 12 + SimdSkia 承载 WebView2（Native AOT）</div>
            <ul style="font-size:17px;line-height:1.9;margin-top:16px">
              <li>控件：<code>AvaloniaWebView</code>（NativeControlHost + WebView2Control）</li>
              <li>渲染：SimdSkia（纯托管 C# Skia）</li>
            </ul>
            <div id="t" style="margin-top:22px;font-size:15px;color:#9fe6b0"></div>
          </div>
          <script>
            document.getElementById('t').textContent = 'UA: ' + navigator.userAgent;
          </script>
        </body></html>
        """;

    private readonly AvaloniaWebView _webView = new();
    private readonly TextBlock _status = new() { Text = "WebView2: 未创建", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _address = new() { Width = 420, Text = "https://www.bing.com", VerticalAlignment = VerticalAlignment.Center };

    public MainWindow()
    {
        Title = "Diga.WebView2.Avalonia — TestAvalonia (SimdSkia + AOT)";
        Width = 1200;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(10, 8)
        };

        toolbar.Children.Add(MakeButton("本地 HTML", () => _webView.NavigateToString(LocalHtml)));
        toolbar.Children.Add(MakeButton("←", () => _webView.GoBack()));
        toolbar.Children.Add(MakeButton("→", () => _webView.GoForward()));
        toolbar.Children.Add(MakeButton("刷新", () => _webView.Reload()));
        toolbar.Children.Add(_address);
        toolbar.Children.Add(MakeButton("转到", () =>
        {
            if (!string.IsNullOrWhiteSpace(_address.Text))
                _webView.Navigate(_address.Text!);
        }));
        toolbar.Children.Add(new Border { Width = 12 });
        toolbar.Children.Add(_status);

        var toolbarBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x30)),
            Child = toolbar
        };

        var root = new DockPanel();
        DockPanel.SetDock(toolbarBorder, Dock.Top);
        root.Children.Add(toolbarBorder);
        root.Children.Add(_webView);
        Content = root;

        _webView.WebViewCreated += (_, _) =>
        {
            _status.Text = "WebView2: 已就绪";
            _status.Foreground = Brushes.LightGreen;
        };

        _webView.NavigationCompleted += (_, e) =>
        {
            _status.Text = "WebView2: 已就绪";
        };

        _webView.ProcessFailed += (_, e) =>
        {
            _status.Text = "WebView2 进程失败";
            _status.Foreground = Brushes.OrangeRed;
        };

        Opened += (_, _) =>
        {
            _status.Text = "WebView2: " + (_webView.IsWebViewReady ? "已就绪" : "创建中");
            _webView.NavigateToString(LocalHtml);
        };
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 4) };
        button.Click += (_, _) => onClick();
        return button;
    }
}
