using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuickPanel;

public partial class PanelWindow : Window
{
    private readonly AppConfig _config;
    private readonly Stopwatch _showSw = new();
    private DateTime _lastAutoHide = DateTime.MinValue;

    public PanelWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        TileGrid.Columns = Math.Clamp(config.GridColumns, 2, 16);
        SourceInitialized += (_, _) => ApplyWin11WindowPolish();
        BuildSampleTiles();
    }

    public void ToggleShow()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        // 刚因失焦自动隐藏（如点了托盘），这次点击应理解为"关掉"而不是立刻又弹回
        if ((DateTime.Now - _lastAutoHide).TotalMilliseconds < 250)
        {
            return;
        }

        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 3;

        _showSw.Restart();
        Show();
        Activate();
        SearchBox.Focus();

        // 渲染一帧完成后记录耗时：热键→可见的实测值进日志
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _showSw.Stop();
            Logger.Log($"panel show = {_showSw.ElapsedMilliseconds}ms（含热键触发到首帧）");
        });
    }

    private void OnDeactivated(object sender, EventArgs e)
    {
        _lastAutoHide = DateTime.Now;
        Hide();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _lastAutoHide = DateTime.MinValue; // Esc 是主动关闭，不算失焦
            Hide();
            e.Handled = true;
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Opacity = string.IsNullOrEmpty(SearchBox.Text) ? 1 : 0;
    }

    // Win11：原生圆角 + 暗色边框（无透明开销）
    [DllImport("dwmapi.dll")]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void ApplyWin11WindowPolish()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref preference, 4); // DWMWA_WINDOW_CORNER_PREFERENCE
            int dark = ThemeApplier.IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref dark, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
        }
        catch
        {
            // 非 Win11 环境忽略，不影响功能
        }
    }

    // P0 示例磁贴：只验证等大网格 + 细边框 + 主题观感；P1 换成真实条目
    private void BuildSampleTiles()
    {
        var samples = new (string Glyph, string Label)[]
        {
            ("📁", "文件夹"),
            ("🌐", "Edge"),
            ("📝", "记事本"),
            ("📷", "Snipaste"),
            ("📚", "Obsidian"),
            ("⚙️", "设置"),
        };

        foreach (var (glyph, label) in samples)
        {
            var tile = new Border { Style = (Style)FindResource("TileBorderStyle") };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var iconBox = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(9),
                Background = (Brush)FindResource("AccentBrush"),
                Opacity = 0.92,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            iconBox.Child = new TextBlock
            {
                Text = glyph,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var text = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            stack.Children.Add(iconBox);
            stack.Children.Add(text);
            tile.Child = stack;
            TileGrid.Children.Add(tile);
        }
    }
}
