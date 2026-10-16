using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace QuickPanel;

public partial class SettingsWindow : Window
{
    private static readonly (string Hex, string Name)[] Presets =
    {
        ("#4A9EFF", "蓝"),
        ("#7C8FA6", "灰蓝"),
        ("#4CAF7D", "绿"),
        ("#B5654A", "砖红"),
        ("#8E7CC3", "紫"),
        ("#D4A017", "金"),
        ("#E06C75", "红"),
        ("#5E6673", "灰"),
    };

    private readonly AppConfig _config;
    private readonly string _tilesPath;
    private readonly string _iconsDir;
    private bool _loading;

    public SettingsWindow(AppConfig config, string tilesPath, string iconsDir)
    {
        InitializeComponent();
        _config = config;
        _tilesPath = tilesPath;
        _iconsDir = iconsDir;
        _loading = true;

        // 外观
        var mode = (config.Theme.Mode ?? "dark").ToLowerInvariant();
        ModeBox.SelectedIndex = mode switch { "light" => 1, "auto" => 2, _ => 0 };
        AccentBox.Text = config.Theme.AccentColor;
        BuildSwatches();
        ColumnsBox.Text = config.GridColumns.ToString();

        // 热键 / 行为
        HotkeyBox.Text = config.Hotkey;
        AutostartBox.IsChecked = GetAutostart();
        EverythingBox.IsChecked = config.Search.EverythingEnabled;
        MaxResultsBox.Text = config.Search.MaxResults.ToString();

        // 数据
        DataPathText.Text = $"当前数据目录：{Path.GetDirectoryName(_tilesPath)}";

        _loading = false;
    }

    private void BuildSwatches()
    {
        foreach (var (hex, name) in Presets)
        {
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Margin = new Thickness(0, 0, 8, 6),
                ToolTip = $"{name} {hex}",
                Cursor = Cursors.Hand,
                Tag = hex,
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                AccentBox.Text = hex; // 触发 OnAccentTextChanged 统一走应用逻辑
            };
            Swatches.Children.Add(swatch);
        }
    }

    // ---------- 外观 ----------

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModeBox.SelectedItem is not ComboBoxItem item || item.Tag is not string mode)
        {
            return;
        }
        _config.Theme.Mode = mode;
        ThemeApplier.Apply(_config.Theme);
        _config.Save();
    }

    private void OnAccentTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        try
        {
            // 合法才应用
            _ = (Color)ColorConverter.ConvertFromString(AccentBox.Text.Trim());
        }
        catch
        {
            return;
        }
        _config.Theme.AccentColor = AccentBox.Text.Trim();
        ThemeApplier.Apply(_config.Theme);
        _config.Save();
    }

    private void OnColumnsLostFocus(object sender, RoutedEventArgs e) => ApplyColumns();
    private void OnColumnsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyColumns();
        }
    }

    private void ApplyColumns()
    {
        if (_loading || !int.TryParse(ColumnsBox.Text.Trim(), out var columns))
        {
            return;
        }
        columns = Math.Clamp(columns, 2, 16);
        ColumnsBox.Text = columns.ToString();
        if (_config.GridColumns != columns)
        {
            _config.GridColumns = columns;
            _config.Save();
            ((App)Application.Current).ApplyGridColumns(columns);
        }
    }

    // ---------- 热键 ----------

    private void OnApplyHotkeyClick(object sender, RoutedEventArgs e)
    {
        var combo = HotkeyBox.Text.Trim();
        var ok = ((App)Application.Current).ChangeHotkey(combo);
        HotkeyStatus.Text = ok ? "✓ 已生效" : "✗ 注册失败（可能被占用）或格式错误，已回退";
        if (ok)
        {
            HotkeyBox.Text = combo;
        }
    }

    // ---------- 行为 ----------

    private static bool GetAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("QuickPanel") is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? "";
            key.SetValue("QuickPanel", $"\"{exe}\"");
            Logger.Log($"autostart ON: {exe}");
        }
        else
        {
            key.DeleteValue("QuickPanel", false);
            Logger.Log("autostart OFF");
        }
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        try
        {
            SetAutostart(AutostartBox.IsChecked == true);
        }
        catch (Exception ex)
        {
            Logger.Log($"autostart set FAILED: {ex.Message}");
            MessageBox.Show(this, $"写入开机启动失败：{ex.Message}", "快捷面板");
            _loading = true;
            AutostartBox.IsChecked = GetAutostart(); // 回读真实状态
            _loading = false;
        }
    }

    private void OnEverythingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        _config.Search.EverythingEnabled = EverythingBox.IsChecked == true;
        _config.Save();
    }

    private void OnMaxResultsLostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || !int.TryParse(MaxResultsBox.Text.Trim(), out var max))
        {
            return;
        }
        max = Math.Clamp(max, 5, 100);
        MaxResultsBox.Text = max.ToString();
        if (_config.Search.MaxResults != max)
        {
            _config.Search.MaxResults = max;
            _config.Save();
        }
    }

    // ---------- 数据目录 ----------

    private void OnChangeDataDirClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择数据目录（建议：群晖同步目录下的专用文件夹）" };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            var newDir = dlg.FolderName;
            Directory.CreateDirectory(newDir);
            Directory.CreateDirectory(Path.Combine(newDir, "icons"));

            if (File.Exists(_tilesPath))
            {
                File.Copy(_tilesPath, Path.Combine(newDir, "tiles.json"), true);
            }
            if (Directory.Exists(_iconsDir))
            {
                foreach (var file in Directory.EnumerateFiles(_iconsDir))
                {
                    File.Copy(file, Path.Combine(newDir, "icons", Path.GetFileName(file)), true);
                }
            }

            _config.DataDir = newDir;
            _config.Save();
            DataPathText.Text = $"当前数据目录：{newDir}（重启程序后生效）";
            Logger.Log($"dataDir changed: {newDir}");
        }
        catch (Exception ex)
        {
            Logger.Log($"dataDir change FAILED: {ex.Message}");
            MessageBox.Show(this, $"迁移数据失败：{ex.Message}", "快捷面板");
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
