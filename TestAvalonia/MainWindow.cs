using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Diga.WebView2.Avalonia;

namespace TestAvalonia;

/// <summary>
/// 多标签示例：工具栏 + 标签条 + WebViewTabHost。
/// 演示：新建/关闭/切换标签、地址导航、缩放、DevTools、下载对话框。
/// 环境变量 DEMO_AUTOSWITCH=1 时，启动 6 秒后自动切到第 2 个标签（便于自动化验证）。
/// </summary>
internal sealed class MainWindow : Window
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TestAvalonia.run.log");

    private const string LocalHtml = """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>本地页面</title></head>
        <body style="margin:0;font-family:'Microsoft YaHei UI',sans-serif;background:#0f2b46;color:#fff">
          <div style="padding:34px">
            <div style="font-size:30px;font-weight:700;color:#8fd0ff">Diga.WebView2.Avalonia.AOT</div>
            <div style="font-size:19px;margin-top:12px">多标签 + SimdSkia + Native AOT</div>
            <ul style="font-size:16px;line-height:1.9;margin-top:14px">
              <li>F5 刷新 / F12 开发者工具 / Ctrl+± 缩放 / Ctrl+0 复位</li>
              <li>target=_blank 默认在当前标签内打开（可关闭该策略）</li>
            </ul>
            <div id="t" style="margin-top:20px;font-size:14px;color:#9fe6b0"></div>
          </div>
          <script>
            document.getElementById('t').textContent = 'UA: ' + navigator.userAgent;
          </script>
        </body></html>
        """;

    private readonly WebViewTabHost _tabs = new();
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly TextBlock _status = new() { Text = "就绪", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _address = new() { Width = 340, Text = "https://example.com", VerticalAlignment = VerticalAlignment.Center };

    public MainWindow()
    {
        AvaloniaWebView.Trace = message => TryLog("[trace] " + message);
        TryLog("=== start ===");
        Title = "Diga.WebView2.Avalonia — TestAvalonia (tabs + SimdSkia + AOT)";
        Width = 1240;
        Height = 840;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(10, 6)
        };

        toolbar.Children.Add(MakeButton("新建标签", () => OpenTab(_address.Text)));
        toolbar.Children.Add(MakeButton("关闭标签", () => _tabs.CloseSelectedTab()));
        toolbar.Children.Add(MakeButton("←", () => _tabs.SelectedTab?.GoBack()));
        toolbar.Children.Add(MakeButton("→", () => _tabs.SelectedTab?.GoForward()));
        toolbar.Children.Add(MakeButton("刷新", () => _tabs.SelectedTab?.Reload()));
        toolbar.Children.Add(_address);
        toolbar.Children.Add(MakeButton("转到", () => _tabs.SelectedTab?.Navigate(_address.Text ?? "about:blank")));
        toolbar.Children.Add(MakeButton("−", () => _tabs.SelectedTab?.ZoomOut()));
        toolbar.Children.Add(MakeButton("+", () => _tabs.SelectedTab?.ZoomIn()));
        toolbar.Children.Add(MakeButton("DevTools", () => _tabs.SelectedTab?.OpenDevTools()));
        toolbar.Children.Add(MakeButton("下载", () => _tabs.SelectedTab?.OpenDefaultDownloadDialog()));
        toolbar.Children.Add(new Border { Width = 10 });
        toolbar.Children.Add(_status);

        var tabBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x31, 0x3E)),
            Padding = new Thickness(8, 4),
            Child = _tabStrip
        };

        var top = new StackPanel();
        top.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x30)),
            Child = toolbar
        });
        top.Children.Add(tabBar);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(_tabs);
        Content = root;

        _tabs.SelectionChanged += (_, _) => RebuildTabStrip();

        // 标签 1：本地 HTML（默认选中）
        var first = OpenTab(null, "local");
        first.NavigateToString(LocalHtml);

        // 标签 2：远程页面（不抢焦点）。可用 DEMO_URL 环境变量覆盖，便于内网/离线验证。
        var demoUrl = Environment.GetEnvironmentVariable("DEMO_URL");
        if (string.IsNullOrWhiteSpace(demoUrl))
            demoUrl = "https://example.com";
        var second = OpenTab(demoUrl, "remote");
        _tabs.SelectTab(first);

        if (string.Equals(Environment.GetEnvironmentVariable("DEMO_AUTOSWITCH"), "1", StringComparison.Ordinal))
        {
            DispatcherTimer.RunOnce(() =>
            {
                TryLog("autoswitch -> tab 2");
                _tabs.SelectTab(second);
            }, TimeSpan.FromSeconds(6));
        }
    }

    private AvaloniaWebView OpenTab(string? url, string tag = "tab")
    {
        var view = _tabs.AddTab(url);
        TryLog($"add tab [{tag}] url={url ?? "(html)"}");

        view.WebViewCreated += (_, _) => TryLog($"created [{tag}]");
        view.NavigationCompleted += (_, e) => TryLog($"navigation-completed [{tag}] ok={e.IsSuccess} err={e.WebErrorStatus}");
        view.SourceChanged += (_, _) => TryLog($"source-changed [{tag}]");

        RebuildTabStrip();
        return view;
    }

    private void RebuildTabStrip()
    {
        _tabStrip.Children.Clear();
        var index = 0;
        foreach (var tab in _tabs.Tabs)
        {
            index++;
            var isSelected = ReferenceEquals(tab, _tabs.SelectedTab);
            var button = new Button
            {
                Content = isSelected ? $"● 标签 {index}" : $"标签 {index}",
                Padding = new Thickness(10, 3),
                Background = isSelected
                    ? new SolidColorBrush(Color.FromRgb(0x3A, 0x63, 0x9E))
                    : new SolidColorBrush(Color.FromRgb(0x33, 0x3B, 0x4A))
            };
            var captured = tab;
            button.Click += (_, _) => _tabs.SelectTab(captured);
            _tabStrip.Children.Add(button);
        }
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 4) };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static void TryLog(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响运行
        }
    }
}
