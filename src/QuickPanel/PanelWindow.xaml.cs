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
    private string? _filterQuery;

    private TileItem? _pressedTile;
    private Point _pressPos;
    private bool _dragging;
    private DateTime _lastAutoHide = DateTime.MinValue;

    // 失焦自动隐藏的抑制器：右键菜单 / 对话框 / 拖拽进行中 / 编辑模式 都不隐藏
    private int _suppressHide;
    private bool _dragActive;

    private ContextMenu? _gridMenu;

    public PanelWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        TileGrid.Columns = Math.Clamp(config.GridColumns, 2, 16);

        var configDir = Path.Combine(AppContext.BaseDirectory, "config");
        _tilesPath = Path.Combine(configDir, "tiles.json");
        _iconsDir = Path.Combine(configDir, "icons");

        _tiles = TilesStore.Load(_tilesPath);
        if (_tiles.Count == 0)
        {
            _editMode = true; // 空面板自动进编辑模式：钉住 + 可拖入
        }

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
        SearchBox.Clear(); // 每次呼出都清空上次搜索，避免残留过滤状态（用户 2026-10-15 反馈）
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
        // 这些情况下必须保持面板可见：编辑模式（拖入添加的前提）、
        // 本面板的右键菜单 / 对话框打开中、有拖拽悬在面板上
        if (_editMode || _suppressHide > 0 || _dragActive)
        {
            return;
        }
        _lastAutoHide = DateTime.Now;
        Hide();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_editMode)
            {
                ToggleEditMode(); // 编辑模式下 Esc = 退出编辑，不关面板
            }
            else
            {
                _lastAutoHide = DateTime.MinValue; // Esc 是主动关闭，不算失焦
                Hide();
            }
            e.Handled = true;
        }
    }

    private void BeginSuppress() => _suppressHide++;
    private void EndSuppress() => _suppressHide = Math.Max(0, _suppressHide - 1);

    // ---------- 渲染 ----------

    private void RenderTiles()
    {
        TileGrid.Children.Clear();

        var query = _filterQuery;
        IEnumerable<TileItem> visible = _tiles;
        if (!string.IsNullOrEmpty(query))
        {
            visible = _tiles.Where(t => t.Label.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        var list = visible.ToList();

        foreach (var tile in list)
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
        else if (list.Count == 0)
        {
            EmptyHint.Text = "没有匹配的磁贴";
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
            Hide();
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
            TilesStore.Save(_tilesPath, _tiles);
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
        TilesStore.Save(_tilesPath, _tiles);
        RenderTiles();
    }

    // ---------- 拖放（从资源管理器拖入） ----------

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDragLeave(object sender, DragEventArgs e) => _dragActive = false;

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        _dragActive = false;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            AddFromFiles((string[])e.Data.GetData(DataFormats.FileDrop)!);
        }
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
        WireMenuLifetime(menu);
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
        WireMenuLifetime(menu);
        return menu;
    }

    /// <summary>菜单弹出会抢走窗口焦点；不抑制的话失焦隐藏会把菜单和面板一起杀掉。</summary>
    private void WireMenuLifetime(ContextMenu menu)
    {
        menu.Opened += (_, _) => BeginSuppress();
        menu.Closed += (_, _) => EndSuppress();
    }

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
                TilesStore.Save(_tilesPath, _tiles);
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
                TilesStore.Save(_tilesPath, _tiles);
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
        TilesStore.Save(_tilesPath, _tiles);
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
            TilesStore.Save(_tilesPath, _tiles);
            RenderTiles();
        }
        finally
        {
            EndSuppress();
        }
    }

    // ---------- 过滤 ----------

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text.Trim();
        SearchPlaceholder.Opacity = string.IsNullOrEmpty(text) ? 1 : 0;
        _filterQuery = string.IsNullOrEmpty(text) ? null : text;
        RenderTiles();
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
