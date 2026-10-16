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
    private SettingsWindow? _settings;
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

        // 全局异常网：托盘常驻应用绝不能因单次异常静默死亡（2026-10-16 实撞：
        // SettingsWindow 对关闭中窗口重复 Close 抛异常，整进程消失，用户以为"卡死"）。
        // UI 线程异常记日志后吞掉保活；非 UI / Task 异常吞不住，但必须留下法医记录。
        DispatcherUnhandledException += (_, e) =>
        {
            Logger.Log($"UI UNHANDLED: {e.Exception.GetType().Name}: {e.Exception.Message}\n{e.Exception.StackTrace}");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Log($"NON-UI UNHANDLED: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Log($"UNOBSERVED TASK: {e.Exception}");
            e.SetObserved();
        };

        _config = AppConfig.LoadOrCreate(Path.Combine(AppContext.BaseDirectory, "config"));
        Logger.Log($"config: mode={_config.Theme.Mode} accent={_config.Theme.AccentColor} hotkey={_config.Hotkey} columns={_config.GridColumns}");

        ThemeApplier.Apply(_config.Theme);

        _panel = new PanelWindow(_config);
        _panel.Hide();

        _tray = CreateTray();

        TryRegisterHotkey(_config.Hotkey);
        if (_hotkey is null || !_hotkey.IsRegistered)
        {
            Logger.Log($"hotkey register FAILED: {_config.Hotkey}（可能被其他程序占用）");
            _tray.ShowBalloonTip("快捷面板", $"热键 {_config.Hotkey} 注册失败，可能被其他程序占用。可编辑 config\\config.json 换热键后重启。", BalloonIcon.Warning);
        }

        // 自检入口：QuickPanel.exe --show 启动后自动呼出一次（用于定位/性能的数值验证）
        if (e.Args.Any(a => string.Equals(a, "--show", StringComparison.OrdinalIgnoreCase)))
        {
            Dispatcher.BeginInvoke(() => _panel?.ToggleShow());
        }
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
        var settings = new MenuItem { Header = "设置…" };
        settings.Click += (_, _) => OpenSettings();
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Shutdown();
        menu.Items.Add(open);
        menu.Items.Add(settings);
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

    internal void OpenSettings()
    {
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }
        var configDir = Path.Combine(AppContext.BaseDirectory, "config");
        var dataDir = string.IsNullOrWhiteSpace(_config.DataDir) ? configDir : _config.DataDir;
        _settings = new SettingsWindow(_config, Path.Combine(dataDir, "tiles.json"), Path.Combine(dataDir, "icons"));
        _settings.Closed += (_, _) => _panel?.EndSuppress();
        _panel?.BeginSuppress(); // 设置窗打开期间面板不因失焦隐藏（2026-10-16：点⚙面板消失且设置窗被盖住的观感问题）
        Logger.Log("settings opened");
        _settings.Show();
    }

    /// <summary>注册热键并接好响应链（Press → 面板显隐）。切换热键必须走这里，否则新热键"注册了却没人响应"。</summary>
    private bool TryRegisterHotkey(string combo)
    {
        try
        {
            HotkeyManager.ParseCombo(combo);
        }
        catch
        {
            Logger.Log($"hotkey combo invalid: {combo}");
            return false;
        }
        _hotkey?.Dispose();
        _hotkey = new HotkeyManager();
        if (!_hotkey.Register(combo))
        {
            Logger.Log($"hotkey register FAILED: {combo}");
            return false;
        }
        _hotkey.Pressed += () => Dispatcher.BeginInvoke(() => _panel?.ToggleShow());
        Logger.Log($"hotkey registered OK: {combo}");
        return true;
    }

    internal bool ChangeHotkey(string combo)
    {
        try
        {
            HotkeyManager.ParseCombo(combo);
        }
        catch
        {
            return false; // 格式不合法
        }
        if (TryRegisterHotkey(combo))
        {
            _config.Hotkey = combo;
            _config.Save();
            Logger.Log($"hotkey changed to: {combo}");
            return true;
        }
        TryRegisterHotkey(_config.Hotkey); // 回退原热键
        Logger.Log($"hotkey change FAILED ({combo}), reverted to {_config.Hotkey}");
        return false;
    }

    internal void ApplyGridColumns(int columns)
    {
        _panel?.UpdateColumns(columns);
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
