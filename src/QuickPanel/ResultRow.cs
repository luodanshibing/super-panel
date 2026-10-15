using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace QuickPanel;

/// <summary>搜索结果行（磁贴 / 程序 / 文件 / 文件夹）。Icon 支持后台填充（INPC）。</summary>
public sealed class ResultRow : INotifyPropertyChanged
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string KindText { get; set; } = "";
    public string Target { get; set; } = "";
    public TileItem? Tile { get; set; }

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
