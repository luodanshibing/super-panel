using System;
using System.Windows;
using System.Windows.Controls;

namespace QuickPanel;

/// <summary>
/// 等大磁贴面板：固定列数时单元格拉伸占满宽度（与旧 UniformGrid 间距一致），
/// 末行不满时整行水平居中（用户 2026-10-16 需求：「两边看起来差不多」）。
/// FixedColumns=0 时按磁贴尺寸自适应列数。
/// </summary>
public class TilePanel : Panel
{
    public static readonly DependencyProperty FixedColumnsProperty = DependencyProperty.Register(
        nameof(FixedColumns), typeof(int), typeof(TilePanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int FixedColumns
    {
        get => (int)GetValue(FixedColumnsProperty);
        set => SetValue(FixedColumnsProperty, value);
    }

    private (double W, double H) CellSize()
    {
        double w = 0, h = 0;
        foreach (UIElement child in InternalChildren)
        {
            w = Math.Max(w, child.DesiredSize.Width);
            h = Math.Max(h, child.DesiredSize.Height);
        }
        if (w <= 0)
        {
            w = 80;
            h = 92; // 空面板兜底（与磁贴样式一致：68+12 / 80+12）
        }
        return (w, h);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (cellW, cellH) = CellSize();
        int cols;
        if (FixedColumns > 0)
        {
            cols = FixedColumns;
        }
        else
        {
            cols = double.IsInfinity(availableSize.Width)
                ? Math.Max(1, InternalChildren.Count)
                : Math.Max(1, (int)Math.Floor(availableSize.Width / cellW));
        }
        var rows = Math.Max(1, (int)Math.Ceiling(InternalChildren.Count / (double)cols));
        var width = FixedColumns > 0 || double.IsInfinity(availableSize.Width) ? cols * cellW : availableSize.Width;
        return new Size(width, rows * cellH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (cellW, cellH) = CellSize();
        var count = InternalChildren.Count;
        if (count == 0)
        {
            return finalSize;
        }

        int cols;
        if (FixedColumns > 0)
        {
            cols = FixedColumns;
            cellW = finalSize.Width / cols;
        }
        else
        {
            cols = Math.Max(1, (int)Math.Floor(finalSize.Width / cellW));
        }

        var totalRows = (count + cols - 1) / cols;
        for (var i = 0; i < count; i++)
        {
            var row = i / cols;
            var col = i % cols;
            var inRow = row == totalRows - 1 ? count - row * cols : cols;
            var lineOffset = Math.Max(0, (finalSize.Width - inRow * cellW) / 2);
            InternalChildren[i].Arrange(new Rect(lineOffset + col * cellW, row * cellH, cellW, cellH));
        }
        return finalSize;
    }
}
