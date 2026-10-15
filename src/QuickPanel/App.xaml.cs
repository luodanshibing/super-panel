using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using QuickPanel;

namespace QuickPanel;

public partial class App : Application
{
    private const string MutexName = "Local\\QuickPanel.SingleInstance";

    private Mutex? _mutex;
    private TaskbarIcon? _tray;
    private HotkeyManager? _hotkey;
    private PanelWindow? _panel;
    private AppConfig _config = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("快捷面板已在运行。", "QuickPanel");
            Shutdown();
            return;
        }

        base.OnStartup(e);

        Logger.Init(Path.Combine(AppContext.BaseDirectory, "logs"));
        Logger.Log("---- session start ----");

        _config = AppConfig.LoadOrCreate(Path.Combine(AppContext.BaseDirectory, "config"));
        Logger.Log($"config: mode={_config.Theme.Mode} accent={_config.Theme.AccentColor} hotkey={_config.Hotkey} columns={_config.GridColumns}");

        ThemeApplier.Apply(_config.Theme);

        _panel = new PanelWindow(_config);
        _panel.Hide();

        _tray = CreateTray();

        _hotkey = new HotkeyManager();
        var ok = _hotkey.Register(_config.Hotkey);
        if (ok)
        {
            Logger.Log($"hotkey registered OK: {_config.Hotkey}");
        }
        else
        {
            Logger.Log($"hotkey register FAILED: {_config.Hotkey}（可能被其他程序占用）");
            _tray.ShowBalloonTip("快捷面板", $"热键 {_config.Hotkey} 注册失败，可能被其他程序占用。可编辑 config\\config.json 换热键后重启。", BalloonIcon.Warning);
        }
        _hotkey.Pressed += () => Dispatcher.BeginInvoke(() => _panel?.ToggleShow());
    }

    private TaskbarIcon CreateTray()
    {
        var icon = new TaskbarIcon
        {
            ToolTipText = "快捷面板 QuickPanel (P0)",
            IconSource = LoadIconSource(),
            Visibility = Visibility.Visible,
        };

        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打开面板" };
        open.Click += (_, _) => _panel?.ToggleShow();
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Shutdown();
        menu.Items.Add(open);
        menu.Items.Add(exit);
        icon.ContextMenu = menu;

        icon.TrayLeftMouseUp += (_, _) => _panel?.ToggleShow();
        return icon;
    }

    private static ImageSource? LoadIconSource()
    {
        try
        {
            return BitmapFrame.Create(new Uri("pack://application:,,,/assets/icon.png", UriKind.Absolute));
        }
        catch
        {
            return null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _hotkey?.Dispose(); } catch { /* ignore */ }
        try { _tray?.Dispose(); } catch { /* ignore */ }
        try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        try { _mutex?.Dispose(); } catch { /* ignore */ }
        base.OnExit(e);
    }
}
