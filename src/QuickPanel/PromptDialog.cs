using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickPanel;

/// <summary>极简输入对话框（代码构建，不引第三方库）：返回各字段文本；取消返回 null。</summary>
public static class PromptDialog
{
    public static string[]? Show(Window owner, string title, params (string Label, string Initial)[] fields)
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.Resources["BgBrush"],
        };

        var stack = new StackPanel { Margin = new Thickness(16) };
        var boxes = new List<TextBox>();

        foreach (var (label, initial) in fields)
        {
            stack.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 4),
            });
            var box = new TextBox
            {
                Text = initial,
                FontSize = 14,
                Padding = new Thickness(6, 5, 6, 5),
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                CaretBrush = (Brush)Application.Current.Resources["AccentBrush"],
            };
            boxes.Add(box);
            stack.Children.Add(box);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok = new Button
        {
            Content = "确定",
            Width = 84,
            Height = 30,
            IsDefault = true,
            Background = (Brush)Application.Current.Resources["AccentBrush"],
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
        };
        var cancel = new Button
        {
            Content = "取消",
            Width = 84,
            Height = 30,
            IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0),
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        win.Content = stack;

        var result = (string[]?)null;
        ok.Click += (_, _) => { result = boxes.Select(b => b.Text.Trim()).ToArray(); win.Close(); };
        cancel.Click += (_, _) => win.Close();

        if (boxes.Count > 0)
        {
            win.Loaded += (_, _) => { boxes[0].Focus(); boxes[0].SelectAll(); };
        }

        win.ShowDialog();
        return result;
    }
}
