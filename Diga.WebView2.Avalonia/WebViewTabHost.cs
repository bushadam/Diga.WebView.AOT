using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Diga.WebView2.Avalonia;

/// <summary>
/// 轻量多标签宿主：为每个标签创建一个 <see cref="AvaloniaWebView"/>，
/// 所有视图叠放在同一个 <see cref="Grid"/> 里，只显示当前选中的那个。
///
/// 为什么不用 <see cref="TabControl"/>：TabControl 只把选中页的 content 挂到可视化树上，
/// 切页会销毁/重建 <see cref="AvaloniaWebView"/>（连带重建 CoreWebView2），页面状态全部丢失。
/// 这里改为“全部保持挂载 + 原生窗口显示/隐藏”，切换标签不丢失页面。
///
/// 注意：不使用 IsVisible=false 来隐藏未选中的标签 —— 那样 Avalonia 会跳过布局，
/// 原生子窗口的位置会停留在旧值，重新显示时就会错位。这里用 SW_HIDE 隐藏 HWND，
/// 让 Avalonia 始终按正常布局摆放所有标签。
/// </summary>
public class WebViewTabHost : Grid
{
    private readonly List<AvaloniaWebView> _tabs = new();

    /// <summary>选中标签变化（参数为新的选中标签，可能为 null）。</summary>
    public event EventHandler<AvaloniaWebView?>? SelectionChanged;

    /// <summary>标签被关闭。</summary>
    public event EventHandler<AvaloniaWebView>? TabClosed;

    public IReadOnlyList<AvaloniaWebView> Tabs => _tabs;

    public AvaloniaWebView? SelectedTab { get; private set; }

    public int TabCount => _tabs.Count;

    /// <summary>新建标签；url 为空时打开空白页。返回新标签的视图。</summary>
    public AvaloniaWebView AddTab(string? url = null)
    {
        var view = new AvaloniaWebView
        {
            IsHitTestVisible = false,
            Source = string.IsNullOrWhiteSpace(url) ? null : url
        };

        _tabs.Add(view);
        Children.Add(view);
        SelectTab(view);
        return view;
    }

    /// <summary>选中指定标签。</summary>
    public void SelectTab(AvaloniaWebView view)
    {
        if (view == null || !_tabs.Contains(view))
            return;

        foreach (var tab in _tabs)
        {
            var active = ReferenceEquals(tab, view);
            tab.IsHitTestVisible = active;
            tab.IsTabActive = active;
            tab.SetNativeVisible(active);
            tab.InvalidateArrange();
        }

        SelectedTab = view;

        // 新标签在隐藏状态下可能还没拿到最终尺寸：等布局跑完再对齐一次 WebView2 的 Bounds。
        view.InvalidateArrange();
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(SelectedTab, view))
                return;

            view.RefreshNativeBounds();
            view.FocusWebView();
        }, DispatcherPriority.Loaded);

        SelectionChanged?.Invoke(this, view);
    }

    /// <summary>关闭标签；会释放 WebView2（结束其浏览器进程组）。</summary>
    public void CloseTab(AvaloniaWebView view)
    {
        if (view == null || !_tabs.Contains(view))
            return;

        var index = _tabs.IndexOf(view);
        _tabs.RemoveAt(index);
        Children.Remove(view);
        view.Dispose();

        TabClosed?.Invoke(this, view);

        if (ReferenceEquals(SelectedTab, view))
        {
            var next = _tabs.Count == 0 ? null : _tabs[Math.Min(index, _tabs.Count - 1)];
            if (next != null)
                SelectTab(next);
            else
            {
                SelectedTab = null;
                SelectionChanged?.Invoke(this, null);
            }
        }
    }

    /// <summary>关闭当前标签。</summary>
    public void CloseSelectedTab()
    {
        if (SelectedTab != null)
            CloseTab(SelectedTab);
    }

    /// <summary>当前标签内导航。</summary>
    public void NavigateSelected(string url) => SelectedTab?.Navigate(url);

    /// <summary>按标签顺序切换（用于 Ctrl+Tab）。</summary>
    public void SelectNext(bool forward = true)
    {
        if (_tabs.Count < 2 || SelectedTab == null)
            return;

        var index = _tabs.IndexOf(SelectedTab);
        var next = forward
            ? (index + 1) % _tabs.Count
            : (index - 1 + _tabs.Count) % _tabs.Count;
        SelectTab(_tabs[next]);
    }
}
