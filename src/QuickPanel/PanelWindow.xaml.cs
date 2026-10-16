using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace QuickPanel;

public partial class PanelWindow : Window
{
    private readonly AppConfig _config;
    private readonly string _tilesPath;
    private readonly string _iconsDir;
    private readonly System.Diagnostics.Stopwatch _showSw = new();

    private List<TileItem> _tiles = new();
    private bool _editMode;
    private DateTime _lastAutoHide = DateTime.MinValue;

    private TileItem? _pressedTile;
    private Point _pressPos;
    private bool _dragging;

    // 失焦自动隐藏的抑制器：右键菜单 / 对话框 / 拖拽进行中 / 编辑模式 都不隐藏
    private int _suppressHide;
    private bool _dragActive;

    private ContextMenu? _gridMenu;

    // P2 搜索
    private readonly ProgramIndex _programs = new();
    private readonly DispatcherTimer _searchTimer;
    private int _searchSeq;
    private string? _esPath;
    private Version? _esVersion;
    private readonly Dictionary<string, string> _tileInitials = new();

    public PanelWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        TileGrid.Columns = Math.Clamp(config.GridColumns, 2, 16);

        var configDir = Path.Combine(AppContext.BaseDirectory, "config");
        var dataDir = string.IsNullOrWhiteSpace(config.DataDir) ? configDir : config.DataDir;
        _tilesPath = Path.Combine(dataDir, "tiles.json");
        _iconsDir = Path.Combine(dataDir, "icons");

        _tiles = TilesStore.Load(_tilesPath);
        if (_tiles.Count == 0)
        {
            _editMode = true; // 空面板自动进编辑模式：钉住 + 可拖入
        }

        Pinyin.Init();

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RunSearch(); };

        _esPath = EverythingSearch.LocateEs(_config);
        _esVersion = _esPath is null ? null : EverythingSearch.DetectEverythingVersion(_esPath);
        Logger.Log($"es.exe: {_esPath ?? "未找到（全盘搜索禁用）"}; Everything 版本: {_esVersion?.ToString() ?? "未知"}");

        _programs.RebuildAsync(n => Logger.Log($"program index built: {n} entries"));

        SourceInitialized += (_, _) => ApplyWin11WindowPolish();
        BuildGridMenu();
        RenderTiles();
        UpdateEditUi();
    }

    // ---------- 显隐 ----------

    public void ToggleShow()
    {
        if (IsVisible)
        {
            HidePanel();
            return;
        }

        // 刚因失焦自动隐藏（如点了托盘），这次点击应理解为"关掉"而不是立刻又弹回
        if ((DateTime.Now - _lastAutoHide).TotalMilliseconds < 250)
        {
            return;
        }

        if (_programs.IsStale)
        {
            _programs.RebuildAsync(n => Logger.Log($"program index rebuilt: {n} entries"));
        }

        // 位置：用户拖动过（placed）→ 恢复上次位置；否则弹在鼠标所在屏幕的默认位
        var area = GetWorkAreaNearCursor();
        var savedLeft = _config.Window?.Left;
        var savedTop = _config.Window?.Top;
        if (_config.Window?.Placed == true && savedLeft.HasValue && savedTop.HasValue &&
            IsWithinVirtualScreen(savedLeft.Value, savedTop.Value))
        {
            Left = savedLeft.Value;
            Top = savedTop.Value;
        }
        else
        {
            Left = area.DipLeft + (area.DipWidth - Width) / 2;
            Top = area.DipTop + (area.DipHeight - Height) / 3;
        }

        _showSw.Restart();
        SearchBox.Clear(); // 每次呼出都清空上次搜索，避免残留过滤状态（用户 2026-10-15 反馈）
        Show();
        EnsureForeground(); // Activate() 可能被前台锁静默拒绝；未激活则失焦隐藏永远不触发（2026-10-16 bug）
        SearchBox.Focus();
        StartVisibilityWatch();

        // 渲染一帧完成后记录耗时与位置自检（DPI 定位验证）：热键→可见的实测值进日志
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _showSw.Stop();
            Logger.Log($"panel show = {_showSw.ElapsedMilliseconds}ms（含热键触发到首帧）");
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (GetWindowRect(hwnd, out var r))
                {
                    Logger.Log(
                        $"posCheck: dpi={area.DpiX:F0} workPhys=({area.PhysLeft},{area.PhysTop})-({area.PhysRight},{area.PhysBottom}) " +
                        $"winPhys=({r.Left},{r.Top})-({r.Right},{r.Bottom}) active={GetForegroundWindow() == hwnd}");
                }
            }
            catch
            {
                // 自检日志失败无碍
            }
        });
    }

    private void OnDeactivated(object sender, EventArgs e)
    {
        // 这些情况下必须保持面板可见：编辑模式（拖入添加的前提）、
        // 本面板的右键菜单 / 模态对话框打开中、有拖拽悬在面板上
        if (ShouldStayVisible())
        {
            Logger.Log("deactivated but staying visible (edit/menu/dialog/drag)");
            return;
        }
        _lastAutoHide = DateTime.Now;
        HidePanel();
    }

    /// <summary>失焦隐藏的放行判断：查实际状态而非计数器，杜绝"抑制卡死"。</summary>
    private bool ShouldStayVisible()
    {
        if (_editMode)
        {
            return true;
        }
        if (_suppressHide > 0)
        {
            return true; // 模态对话框（PromptDialog / 文件对话框）打开中
        }
        if (_dragActive)
        {
            return true; // 拖拽悬停中（10s 到期保护，见 OnWindowDragEnter）
        }
        if (ContextMenu?.IsOpen == true || ResultList.ContextMenu?.IsOpen == true)
        {
            return true;
        }
        foreach (var border in TileGrid.Children.OfType<Border>())
        {
            if (border.ContextMenu?.IsOpen == true)
            {
                return true;
            }
        }
        return false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_editMode)
            {
                ToggleEditMode(); // 编辑模式下 Esc = 退出编辑，不关面板
            }
            else if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Clear(); // 有搜索词：先清搜索回到磁贴视图
            }
            else
            {
                _lastAutoHide = DateTime.MinValue; // Esc 是主动关闭，不算失焦
                HidePanel();
            }
            e.Handled = true;
            return;
        }

        if (ResultList.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Enter or Key.Return:
                    OpenSelected();
                    e.Handled = true;
                    break;
            }
        }
    }

    private void BeginSuppress() => _suppressHide++;
    private void EndSuppress() => _suppressHide = Math.Max(0, _suppressHide - 1);

    // ---------- 前台激活与可见性兜底 ----------

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    private DispatcherTimer? _visibilityWatch;

    /// <summary>
    /// 强制把面板带到前台。Activate() 会被 Windows 前台锁静默拒绝——面板从未激活时，
    /// "点外面"不会产生 Deactivated 事件，失焦隐藏就永远不触发（2026-10-16 实测 bug）。
    /// 惯用解法：先按一次 Alt 解锁，再 SetForegroundWindow。
    /// </summary>
    private void EnsureForeground()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (GetForegroundWindow() != hwnd)
            {
                keybd_event(0x12, 0, 0, IntPtr.Zero); // ALT down：解除前台锁
                SetForegroundWindow(hwnd);
                keybd_event(0x12, 0, 2, IntPtr.Zero); // ALT up
            }
        }
        catch
        {
            // 失败还有巡检兜底
        }
    }

    /// <summary>兜底巡检：可见但未激活且无抑制理由 → 隐藏。任何"卡住不消失"的保险丝。</summary>
    private void StartVisibilityWatch()
    {
        _visibilityWatch?.Stop();
        _visibilityWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _visibilityWatch.Tick += (_, _) =>
        {
            if (!IsVisible)
            {
                _visibilityWatch?.Stop();
                return;
            }
            if (!IsActive && !ShouldStayVisible())
            {
                _visibilityWatch?.Stop();
                Logger.Log("visibility watch: panel visible but inactive → hide");
                _lastAutoHide = DateTime.Now;
                HidePanel();
            }
        };
        _visibilityWatch.Start();
    }

    // ---------- 搜索（P2 核心） ----------

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text.Trim();
        SearchPlaceholder.Opacity = string.IsNullOrEmpty(text) ? 1 : 0;
        _searchTimer.Stop();
        if (string.IsNullOrEmpty(text))
        {
            _searchSeq++; // 作废未决的全盘回调
            ShowTileView();
        }
        else
        {
            _searchTimer.Start(); // 防抖 120ms 后统一搜索
        }
    }

    private void RunSearch()
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0)
        {
            ShowTileView();
            return;
        }

        _searchSeq++;
        var seq = _searchSeq;

        // ① 磁贴层（内存，瞬时）：名称或拼音首字母匹配
        var rows = new List<ResultRow>();
        foreach (var tile in _tiles)
        {
            var ini = InitialsOfTile(tile);
            if (tile.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (ini.Length > 0 && ini.Contains(q, StringComparison.OrdinalIgnoreCase)))
            {
                rows.Add(RowFromTile(tile));
            }
        }

        // ② 程序层（开始菜单索引，内存）：同样支持拼音首字母
        foreach (var program in _programs.Search(q,
                     (name, initials) =>
                         name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                         (initials.Length > 0 && initials.Contains(q, StringComparison.OrdinalIgnoreCase)),
                     12))
        {
            if (!rows.Any(r => r.Target.Equals(program.Path, StringComparison.OrdinalIgnoreCase)))
            {
                rows.Add(new ResultRow
                {
                    Title = program.Name,
                    Subtitle = program.Path,
                    KindText = "程序",
                    Target = program.Path,
                    Icon = GetProgramIcon(program.Path),
                });
            }
        }

        RenderResultRows(rows);

        // ③ 全盘层（Everything，异步、防抖后补充上屏）
        if (_esPath is null || !_config.Search.EverythingEnabled)
        {
            return;
        }
        var esPath = _esPath;
        var max = _config.Search.MaxResults;
        var usePinyin = _esVersion is not null && _esVersion >= new Version(1, 5);
        _ = Task.Run(async () =>
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var hits = await EverythingSearch.QueryAsync(esPath, q, max,
                    new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
                if (usePinyin && hits.Count < 8 && q.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9'))
                {
                    try
                    {
                        var extra = await EverythingSearch.QueryAsync(esPath, "pinyin:" + q, max,
                            new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
                        hits.AddRange(extra.Where(h => !hits.Contains(h, StringComparer.OrdinalIgnoreCase)));
                    }
                    catch
                    {
                        // 拼音补充失败不影响主结果
                    }
                }
                sw.Stop();
                _ = Dispatcher.BeginInvoke(() =>
                {
                    if (seq != _searchSeq)
                    {
                        return; // 已被更新的输入作废
                    }
                    Logger.Log($"es '{q}' {sw.ElapsedMilliseconds}ms, {hits.Count} hits");
                    AppendFileRows(hits);
                });
            }
            catch (OperationCanceledException)
            {
                // 超时/取消，静默
            }
            catch (Exception ex)
            {
                Logger.Log($"everything query FAILED: {ex.Message}");
            }
        });
    }

    private string InitialsOfTile(TileItem tile)
    {
        if (!_tileInitials.TryGetValue(tile.Id, out var initials))
        {
            initials = Pinyin.Initials(tile.Label);
            _tileInitials[tile.Id] = initials;
        }
        return initials;
    }

    private ResultRow RowFromTile(TileItem tile)
    {
        var missing = tile.Type != "url" && !File.Exists(tile.Target) && !Directory.Exists(tile.Target);
        return new ResultRow
        {
            Title = tile.Label,
            Subtitle = missing ? "目标不存在：" + tile.Target : tile.Target,
            KindText = "磁贴",
            Target = tile.Target,
            Tile = tile,
            Icon = ResolveIcon(tile),
        };
    }

    private ImageSource? GetProgramIcon(string lnkPath)
    {
        // 程序图标：按路径哈希缓存，避免每次搜索重复提取
        uint hash = 2166136261;
        foreach (var c in lnkPath)
        {
            hash = (hash ^ c) * 16777619;
        }
        var cacheName = "p" + hash.ToString("x8") + ".png";
        var cachePath = Path.Combine(_iconsDir, cacheName);
        if (File.Exists(cachePath))
        {
            return IconExtractor.LoadImageFile(cachePath);
        }
        var bmp = IconExtractor.Extract(lnkPath);
        if (bmp is not null)
        {
            try { IconExtractor.SavePng(bmp, cachePath); } catch { /* 缓存失败不影响 */ }
            return bmp;
        }
        return null;
    }

    private void RenderResultRows(List<ResultRow> rows)
    {
        TileViewHost.Visibility = Visibility.Collapsed;
        ResultList.Visibility = Visibility.Visible;
        EditHint.Visibility = Visibility.Collapsed;
        ResultList.ItemsSource = rows;
        ResultList.SelectedIndex = rows.Count > 0 ? 0 : -1;

        if (rows.Count == 0)
        {
            EmptyHint.Text = _esPath is null
                ? "无匹配（未找到 es.exe，全盘搜索不可用）"
                : "无匹配——全盘结果即将出现…";
            EmptyHint.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyHint.Visibility = Visibility.Collapsed;
        }
    }

    private void AppendFileRows(List<string> hits)
    {
        if (ResultList.ItemsSource is not List<ResultRow> rows)
        {
            return;
        }
        var added = new List<ResultRow>();
        foreach (var raw in hits)
        {
            var isDir = raw.EndsWith("\\") || Directory.Exists(raw);
            var path = raw.TrimEnd('\\');
            var name = Path.GetFileName(path);
            if (name.Length == 0)
            {
                continue;
            }
            if (rows.Any(r => r.Target.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                continue; // 与磁贴/程序层去重
            }
            var row = new ResultRow
            {
                Title = name,
                Subtitle = Path.GetDirectoryName(path) ?? path,
                KindText = isDir ? "文件夹" : "文件",
                Target = path,
            };
            rows.Add(row);
            added.Add(row);
        }
        if (added.Count == 0)
        {
            return;
        }
        var selectedIndex = ResultList.SelectedIndex;
        ResultList.ItemsSource = null;
        ResultList.ItemsSource = rows;
        ResultList.SelectedIndex = selectedIndex >= 0 ? Math.Min(selectedIndex, rows.Count - 1) : (rows.Count > 0 ? 0 : -1);

        // 图标后台提取（每行 ~1ms，不卡 UI）
        _ = Task.Run(() =>
        {
            foreach (var row in added)
            {
                var icon = IconExtractor.Extract(row.Target);
                if (icon is not null)
                {
                    var captured = row;
                    Dispatcher.BeginInvoke(() => captured.Icon = icon);
                }
            }
        });
    }

    private void MoveSelection(int delta)
    {
        var count = ResultList.Items.Count;
        if (count == 0)
        {
            return;
        }
        var idx = ResultList.SelectedIndex + delta;
        if (idx < 0) idx = 0;
        if (idx >= count) idx = count - 1;
        ResultList.SelectedIndex = idx;
        ResultList.ScrollIntoView(ResultList.SelectedItem);
    }

    // ---------- 结果行右键：添加到面板 / 打开 / 打开所在位置 ----------

    private void OnResultContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var item = ItemsControl.ContainerFromElement(ResultList, source) as ListBoxItem;
        if (item?.Content is not ResultRow row)
        {
            ResultList.ContextMenu = null;
            return;
        }

        var menu = new ContextMenu();
        if (row.Tile is null)
        {
            menu.Items.Add(MakeItem("添加到面板", () => AddTileFromRow(row)));
            menu.Items.Add(MakeItem("打开", () => OpenRow(row)));
            if (File.Exists(row.Target) || Directory.Exists(row.Target))
            {
                menu.Items.Add(MakeItem("打开所在位置", () => RevealInExplorer(row.Target)));
            }
        }
        else
        {
            menu.Items.Add(MakeItem("打开", () => OpenRow(row)));
            menu.Items.Add(MakeItem("从面板移除", () => RemoveTile(row.Tile)));
        }
        ResultList.ContextMenu = menu;
    }

    /// <summary>把搜索到的程序/文件/文件夹固定成磁贴（用户 2026-10-16 需求）。</summary>
    private void AddTileFromRow(ResultRow row)
    {
        if (_tiles.Any(t => t.Target.Equals(row.Target, StringComparison.OrdinalIgnoreCase)))
        {
            FlashHint("已在面板中：" + row.Title);
            return;
        }
        var ext = Path.GetExtension(row.Target).ToLowerInvariant();
        var type = Directory.Exists(row.Target) ? "folder"
            : ext is ".lnk" or ".exe" ? "app"
            : "file";
        _tiles.Add(new TileItem { Type = type, Label = row.Title, Target = row.Target });
        SaveTiles();
        RenderTiles();
        Logger.Log($"tile added from search: {row.Target}");
        FlashHint("已添加到面板：" + row.Title);
    }

    private void RemoveTile(TileItem tile)
    {
        _tiles.Remove(tile);
        SaveTiles();
        RenderTiles();
    }

    private void RevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            HidePanel();
        }
        catch (Exception ex)
        {
            Logger.Log($"reveal FAILED: {path} - {ex.Message}");
        }
    }

    private DispatcherTimer? _hintTimer;

    /// <summary>轻提示：底部闪现一条文字，2.5s 后恢复。</summary>
    private void FlashHint(string text)
    {
        EditHint.Text = text;
        EditHint.Visibility = Visibility.Visible;
        _hintTimer?.Stop();
        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer?.Stop();
            UpdateEditUi();
        };
        _hintTimer.Start();
    }

    private void OpenSelected()
    {
        if (ResultList.SelectedItem is ResultRow row)
        {
            OpenRow(row);
        }
    }

    private void OnResultClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var item = ItemsControl.ContainerFromElement(ResultList, source) as ListBoxItem;
        if (item?.Content is ResultRow row)
        {
            OpenRow(row); // 单击即打开（启动器惯例）
        }
    }

    private void OpenRow(ResultRow row)
    {
        _searchSeq++;
        if (row.Tile is not null)
        {
            Launch(row.Tile);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(row.Target) { UseShellExecute = true });
            Logger.Log($"open OK: {row.Target}");
            HidePanel();
            SearchBox.Clear();
        }
        catch (Exception ex)
        {
            Logger.Log($"open FAILED: {row.Target} - {ex.Message}");
            BeginSuppress();
            try
            {
                MessageBox.Show(this, $"无法打开：{row.Target}\n\n{ex.Message}", "快捷面板");
            }
            finally
            {
                EndSuppress();
            }
        }
    }

    private void ShowTileView()
    {
        TileViewHost.Visibility = Visibility.Visible;
        ResultList.Visibility = Visibility.Collapsed;
        RenderTiles();
        UpdateEditUi();
    }

    // ---------- 渲染磁贴 ----------

    private void RenderTiles()
    {
        TileGrid.Children.Clear();

        foreach (var tile in _tiles)
        {
            TileGrid.Children.Add(BuildTile(tile));
        }

        if (_tiles.Count == 0)
        {
            EmptyHint.Text = _editMode
                ? "编辑模式（面板已钉住）：从资源管理器把软件/文件/文件夹拖进来，或点右上角 ＋"
                : "点右上角 ＋ 添加，或点 ✎ 进入编辑模式后拖入";
            EmptyHint.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyHint.Visibility = Visibility.Collapsed;
        }
    }

    private Border BuildTile(TileItem tile)
    {
        var border = new Border { Style = (Style)FindResource("TileBorderStyle"), Tag = tile };

        if (_editMode)
        {
            border.BorderBrush = (Brush)FindResource("AccentBrush");
            border.BorderThickness = new Thickness(2);
        }

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        // 图标区：优先自定义图标 > 图标缓存 > 提取 > 字符兜底
        var iconSource = ResolveIcon(tile);
        FrameworkElement iconPart;
        if (iconSource is not null)
        {
            iconPart = new Image
            {
                Source = iconSource,
                Width = 38,
                Height = 38,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else
        {
            iconPart = new TextBlock
            {
                Text = tile.Type == "url" ? "🌐" : "⚪",
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        var iconBox = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(9),
            Background = iconSource is null ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
            Opacity = iconSource is null ? 0.92 : 1,
            Child = iconPart,
        };

        var text = new TextBlock
        {
            Text = tile.Label,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 62,
        };

        stack.Children.Add(iconBox);
        stack.Children.Add(text);
        border.Child = stack;

        // 目标丢失：整块变暗提示
        var missing = tile.Type != "url" && !File.Exists(tile.Target) && !Directory.Exists(tile.Target);
        if (missing)
        {
            border.Opacity = 0.45;
            text.Text = tile.Label + "（目标不存在）";
        }
        border.ToolTip = missing ? $"目标不存在：{tile.Target}" : tile.Target;

        // 交互：普通模式点击启动 + 支持拖拽排序；编辑模式靠右键菜单
        if (!_editMode)
        {
            border.MouseLeftButtonDown += (_, e) =>
            {
                _pressedTile = tile;
                _pressPos = e.GetPosition(this);
            };
            border.MouseMove += (_, e) =>
            {
                if (_pressedTile != tile || e.LeftButton != MouseButtonState.Pressed)
                {
                    return;
                }
                var pos = e.GetPosition(this);
                if (Math.Abs(pos.X - _pressPos.X) < 8 && Math.Abs(pos.Y - _pressPos.Y) < 8)
                {
                    return;
                }
                _dragging = true;
                _pressedTile = null;
                var data = new DataObject("QuickPanelTileId", tile.Id);
                DragDrop.DoDragDrop(border, data, DragDropEffects.Move);
                _dragging = false;
            };
            border.MouseLeftButtonUp += (_, _) =>
            {
                if (!_dragging && _pressedTile == tile)
                {
                    Launch(tile);
                }
                _pressedTile = null;
            };
        }

        border.AllowDrop = true;
        border.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("QuickPanelTileId")
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        };
        border.Drop += (_, e) =>
        {
            e.Handled = true;
            _dragActive = false;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                AddFromFiles((string[])e.Data.GetData(DataFormats.FileDrop)!);
                return;
            }
            if (e.Data.GetDataPresent("QuickPanelTileId"))
            {
                Reorder((string)e.Data.GetData("QuickPanelTileId")!, tile);
            }
        };

        border.ContextMenu = BuildTileMenu(tile);
        return border;
    }

    private ImageSource? ResolveIcon(TileItem tile)
    {
        if (!string.IsNullOrEmpty(tile.IconPath))
        {
            return IconExtractor.LoadImageFile(tile.IconPath);
        }

        if (!string.IsNullOrEmpty(tile.IconCache))
        {
            var cachePath = Path.Combine(_iconsDir, tile.IconCache);
            if (File.Exists(cachePath))
            {
                return IconExtractor.LoadImageFile(cachePath);
            }
        }

        if (string.IsNullOrEmpty(tile.Target) || tile.Type == "url")
        {
            return null; // 网址用 🌐 字符兜底
        }

        var bmp = IconExtractor.Extract(tile.Target);
        if (bmp is not null)
        {
            try
            {
                tile.IconCache = tile.Id + ".png";
                IconExtractor.SavePng(bmp, Path.Combine(_iconsDir, tile.IconCache));
            }
            catch
            {
                // 缓存写失败不影响显示
            }
        }
        return bmp;
    }

    // ---------- 启动 ----------

    private void Launch(TileItem tile)
    {
        try
        {
            var psi = new ProcessStartInfo(tile.Target)
            {
                UseShellExecute = true,
            };
            if (!string.IsNullOrEmpty(tile.Arguments))
            {
                psi.Arguments = tile.Arguments;
            }
            if (!string.IsNullOrEmpty(tile.WorkDir))
            {
                psi.WorkingDirectory = tile.WorkDir;
            }
            Process.Start(psi);
            Logger.Log($"launch OK: {tile.Label} -> {tile.Target}");
            HidePanel();
            SearchBox.Clear();
        }
        catch (Exception ex)
        {
            Logger.Log($"launch FAILED: {tile.Target} - {ex.Message}");
            BeginSuppress();
            try
            {
                MessageBox.Show(this, $"无法打开：{tile.Target}\n\n{ex.Message}", "快捷面板");
            }
            finally
            {
                EndSuppress();
            }
        }
    }

    // ---------- 添加 / 排序 / 删除 ----------

    private void AddFromFiles(string[] paths)
    {
        var added = 0;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p))
            {
                continue;
            }
            var tile = TileFromPath(p);
            _tiles.Add(tile);
            added++;
        }
        if (added > 0)
        {
            SaveTiles();
            Logger.Log($"added {added} tile(s) via drag-drop");
            RenderTiles();
        }
    }

    private static TileItem TileFromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var type = Directory.Exists(path) ? "folder"
            : ext is ".lnk" or ".exe" ? "app"
            : ext == ".url" ? "url"
            : "file";
        var label = type == "folder" ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetFileNameWithoutExtension(path);
        return new TileItem { Type = type, Label = label, Target = path };
    }

    private void Reorder(string draggedId, TileItem targetTile)
    {
        var dragged = _tiles.FirstOrDefault(t => t.Id == draggedId);
        if (dragged is null || dragged.Id == targetTile.Id)
        {
            return;
        }
        _tiles.Remove(dragged);
        var targetIndex = _tiles.IndexOf(targetTile);
        _tiles.Insert(Math.Max(0, targetIndex), dragged);
        SaveTiles();
        RenderTiles();
    }

    private void SaveTiles()
    {
        TilesStore.Save(_tilesPath, _tiles);
        _tileInitials.Clear(); // 标签可能变，拼音缓存作废
    }

    // ---------- 拖放（从资源管理器拖入） ----------

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDragLeave(object sender, DragEventArgs e)
    {
        _dragActive = false;
        _dragExpire?.Stop();
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        _dragActive = false;
        _dragExpire?.Stop();
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            AddFromFiles((string[])e.Data.GetData(DataFormats.FileDrop)!);
        }
    }

    private DispatcherTimer? _dragExpire;

    /// <summary>拖拽悬停期间不失焦隐藏；10s 到期保护防止 DragLeave 丢失导致永久钉住。</summary>
    private void OnWindowDragEnter(object sender, DragEventArgs e)
    {
        _dragActive = true;
        _dragExpire?.Stop();
        _dragExpire = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _dragExpire.Tick += (_, _) =>
        {
            _dragExpire?.Stop();
            _dragActive = false;
        };
        _dragExpire.Start();
    }

    // ---------- 顶栏按钮 ----------

    private void OnCloseClick(object sender, RoutedEventArgs e) => HidePanel();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).OpenSettings();
    }

    // ---------- 右键菜单 ----------

    private void BuildGridMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MakeItem("添加文件…", () => AddViaDialogs("file")));
        menu.Items.Add(MakeItem("添加文件夹…", () => AddViaDialogs("folder")));
        menu.Items.Add(MakeItem("添加网址…", AddUrl));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem(EditMenuText(), ToggleEditMode));
        _gridMenu = menu;
        ContextMenu = menu; // 面板空白处右键
    }

    private ContextMenu BuildTileMenu(TileItem tile)
    {
        var menu = new ContextMenu();
        if (!_editMode)
        {
            menu.Items.Add(MakeItem("打开", () => Launch(tile)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem("进入编辑模式", ToggleEditMode));
        }
        else
        {
            menu.Items.Add(MakeItem("重命名…", () => RenameTile(tile)));
            menu.Items.Add(MakeItem("更换图标…", () => ChangeIcon(tile)));
            menu.Items.Add(MakeItem("删除", () => DeleteTile(tile)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem("退出编辑模式", ToggleEditMode));
        }
        return menu;
    }

    /// <summary>菜单弹出会抢走窗口焦点：现在由 ShouldStayVisible 直接查 IsOpen，这里不再需要计数器。</summary>
    private static MenuItem MakeItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private string EditMenuText() => _editMode ? "退出编辑模式" : "进入编辑模式";

    // ---------- 顶栏按钮 ----------

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (_gridMenu is null)
        {
            return;
        }
        _gridMenu.PlacementTarget = AddButton;
        _gridMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        _gridMenu.IsOpen = true;
    }

    private void OnEditClick(object sender, RoutedEventArgs e) => ToggleEditMode();

    // ---------- 编辑动作 ----------

    private void ToggleEditMode()
    {
        _editMode = !_editMode;
        BuildGridMenu(); // 菜单文案随模式变
        RenderTiles();
        UpdateEditUi();
        Logger.Log($"edit mode = {_editMode}");
    }

    private void UpdateEditUi()
    {
        EditHint.Visibility = _editMode ? Visibility.Visible : Visibility.Collapsed;
        if (_editMode)
        {
            EditButton.Background = (Brush)FindResource("AccentBrush");
            EditButton.Foreground = Brushes.White;
        }
        else
        {
            EditButton.Background = Brushes.Transparent;
            EditButton.Foreground = (Brush)FindResource("TextDimBrush");
        }
    }

    private void RenameTile(TileItem tile)
    {
        BeginSuppress();
        try
        {
            var r = PromptDialog.Show(this, "重命名磁贴", ("名称", tile.Label));
            if (r is not null && !string.IsNullOrWhiteSpace(r[0]))
            {
                tile.Label = r[0];
                SaveTiles();
                RenderTiles();
            }
        }
        finally
        {
            EndSuppress();
        }
    }

    private void ChangeIcon(TileItem tile)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择图标",
            Filter = "图标|*.ico;*.png|所有文件|*.*",
        };
        BeginSuppress();
        try
        {
            if (dlg.ShowDialog(this) == true)
            {
                tile.IconPath = dlg.FileName;
                SaveTiles();
                RenderTiles();
            }
        }
        finally
        {
            EndSuppress();
        }
    }

    private void DeleteTile(TileItem tile)
    {
        _tiles.Remove(tile);
        SaveTiles();
        RenderTiles();
    }

    private void AddViaDialogs(string kind)
    {
        if (kind == "folder")
        {
            var dlg = new OpenFolderDialog { Title = "选择要添加的文件夹" };
            BeginSuppress();
            try
            {
                if (dlg.ShowDialog(this) == true)
                {
                    AddFromFiles(new[] { dlg.FolderName });
                }
            }
            finally
            {
                EndSuppress();
            }
            return;
        }

        var fileDlg = new OpenFileDialog
        {
            Title = "选择要添加的程序或文件",
            Filter = "程序与快捷方式|*.exe;*.lnk|所有文件|*.*",
            Multiselect = true,
        };
        BeginSuppress();
        try
        {
            if (fileDlg.ShowDialog(this) == true)
            {
                AddFromFiles(fileDlg.FileNames);
            }
        }
        finally
        {
            EndSuppress();
        }
    }

    private void AddUrl()
    {
        BeginSuppress();
        try
        {
            var r = PromptDialog.Show(this, "添加网址", ("名称", ""), ("网址", "https://"));
            if (r is null || string.IsNullOrWhiteSpace(r[0]) || string.IsNullOrWhiteSpace(r[1]))
            {
                return;
            }
            var target = r[1];
            if (!target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                target = "https://" + target;
            }
            _tiles.Add(new TileItem { Type = "url", Label = r[0], Target = target });
            SaveTiles();
            RenderTiles();
        }
        finally
        {
            EndSuppress();
        }
    }

    // ---------- 隐藏 / 位置 / 设置入口（P3） ----------

    /// <summary>统一隐藏出口：记位置 → 隐藏 → 作废未决搜索 → 延迟 GC 压回闲置内存。</summary>
    private void HidePanel()
    {
        _visibilityWatch?.Stop();
        if (!IsVisible)
        {
            return;
        }
        _config.Window = new WindowConfig { Left = Left, Top = Top };
        _config.Save();
        Hide();
        _searchSeq++;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500);
                GC.Collect(2, GCCollectionMode.Optimized, false);
            }
            catch
            {
                // GC 失败无碍
            }
        });
    }

    public void UpdateColumns(int columns)
    {
        TileGrid.Columns = Math.Clamp(columns, 2, 16);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// 鼠标所在显示器的工作区（多屏下面板弹在用什么屏）。
    /// 返回 DIP（WPF Left/Top 的单位）与物理像素两套值 + 该屏 DPI：物理值必须除以缩放比，
    /// 否则 125%/150% 缩放屏上面板会整体偏右下（2026-10-16 位置错误 bug 的根因）。
    /// </summary>
    private (double DipLeft, double DipTop, double DipWidth, double DipHeight,
        int PhysLeft, int PhysTop, int PhysRight, int PhysBottom, double DpiX) GetWorkAreaNearCursor()
    {
        try
        {
            if (GetCursorPos(out var pt))
            {
                var monitor = MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    double dpiX = 96;
                    try
                    {
                        if (GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dx, out _) == 0)
                        {
                            dpiX = dx;
                        }
                    }
                    catch
                    {
                        // shcore 不可用回退 96
                    }
                    var scale = dpiX / 96.0;
                    var workW = info.rcWork.Right - info.rcWork.Left;
                    var workH = info.rcWork.Bottom - info.rcWork.Top;
                    return (
                        info.rcWork.Left / scale, info.rcWork.Top / scale, workW / scale, workH / scale,
                        info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom, dpiX);
                }
            }
        }
        catch
        {
            // 回退主屏工作区
        }
        var wa = SystemParameters.WorkArea;
        return (wa.Left, wa.Top, wa.Width, wa.Height, 0, 0, 0, 0, 96);
    }

    private bool IsWithinVirtualScreen(double left, double top)
    {
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vr = vl + SystemParameters.VirtualScreenWidth;
        var vb = vt + SystemParameters.VirtualScreenHeight;
        return left >= vl - 20 && top >= vt - 20 && left + Width <= vr + 20 && top + Height <= vb + 20;
    }

    /// <summary>按住顶栏空白处拖动面板；拖完记位置并标记 placed（之后每次弹在这里）。</summary>
    private void OnTopBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 点到搜索框/按钮时不拖动
        if (e.OriginalSource is System.Windows.Controls.TextBox or System.Windows.Controls.Button)
        {
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
                _config.Window = new WindowConfig { Left = Left, Top = Top, Placed = true };
                _config.Save();
                Logger.Log($"panel moved to ({Left:F0},{Top:F0}), placed=true");
            }
            catch
            {
                // DragMove 偶发异常（如非按下状态）忽略
            }
        }
    }

    // ---------- Win11 视觉 ----------

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
}
