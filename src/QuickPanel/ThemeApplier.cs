using System.Windows;
using System.Windows.Media;

namespace QuickPanel;

/// <summary>把主题配置应用到 Application.Resources 的 6 个画刷键上。</summary>
public static class ThemeApplier
{
    public static bool IsDark { get; private set; } = true;

    public static void Apply(ThemeConfig theme)
    {
        var mode = (theme.Mode ?? "dark").Trim().ToLowerInvariant();
        if (mode == "auto")
        {
            mode = SystemPrefersLight() ? "light" : "dark";
        }
        IsDark = mode != "light";

        Color bg, surface, border, text, dim;
        if (IsDark)
        {
            bg = Color.FromRgb(0x1E, 0x1F, 0x24);
            surface = Color.FromRgb(0x26, 0x27, 0x2E);
            border = Color.FromRgb(0x3A, 0x3B, 0x44);
            text = Color.FromRgb(0xE8, 0xE8, 0xEA);
            dim = Color.FromRgb(0x9A, 0x9A, 0xA3);
        }
        else
        {
            bg = Color.FromRgb(0xF5, 0xF5, 0xF7);
            surface = Colors.White;
            border = Color.FromRgb(0xD9, 0xD9, 0xDE);
            text = Color.FromRgb(0x1B, 0x1B, 0x1F);
            dim = Color.FromRgb(0x6E, 0x6E, 0x78);
        }

        Set("BgBrush", bg);
        Set("SurfaceBrush", surface);
        Set("BorderBrush", border);
        Set("TextBrush", text);
        Set("TextDimBrush", dim);
        Set("AccentBrush", ParseColor(theme.AccentColor, Color.FromRgb(0x4A, 0x9E, 0xFF)));
    }

    private static void Set(string key, Color color)
    {
        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private static bool SystemPrefersLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                return (Color)ColorConverter.ConvertFromString(hex.Trim());
            }
        }
        catch
        {
            // 非法颜色值退回默认，不崩
        }
        return fallback;
    }
}
