using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickPanel;

public sealed class AppConfig
{
    [JsonPropertyName("theme")]
    public ThemeConfig Theme { get; set; } = new();

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";

    [JsonPropertyName("gridColumns")]
    public int GridColumns { get; set; } = 8;

    [JsonPropertyName("search")]
    public SearchConfig Search { get; set; } = new();

    [JsonPropertyName("window")]
    public WindowConfig? Window { get; set; }

    /// <summary>数据目录覆盖（tiles.json + 图标缓存）。留空 = exe 旁 config\；可指向群晖同步目录实现双机共用布局。</summary>
    [JsonPropertyName("dataDir")]
    public string DataDir { get; set; } = "";

    private string? _sourcePath;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>便携配置：exe 旁 config\config.json；不存在则生成默认值。</summary>
    public static AppConfig LoadOrCreate(string dir)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");

        if (!File.Exists(path))
        {
            var defaults = new AppConfig { _sourcePath = path };
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOpts));
            return defaults;
        }

        var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOpts) ?? new AppConfig();
        loaded.Normalize();
        loaded._sourcePath = path;
        File.WriteAllText(path, JsonSerializer.Serialize(loaded, JsonOpts)); // 补齐缺失键，用户始终能看到完整 schema
        return loaded;
    }

    /// <summary>就地保存（记住来源路径后可随时调用）。</summary>
    public void Save()
    {
        if (_sourcePath is null)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_sourcePath)!);
            File.WriteAllText(_sourcePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception ex)
        {
            QuickPanel.Logger.Log($"config save FAILED: {ex.Message}");
        }
    }

    private void Normalize()
    {
        Theme ??= new ThemeConfig();
        if (string.IsNullOrWhiteSpace(Hotkey))
        {
            Hotkey = "Ctrl+Alt+Space";
        }
        if (GridColumns is < 2 or > 16)
        {
            GridColumns = 8;
        }
        Search ??= new SearchConfig();
        if (Search.MaxResults is < 5 or > 100)
        {
            Search.MaxResults = 30;
        }
        Window ??= new WindowConfig();
    }
}

public sealed class WindowConfig
{
    [JsonPropertyName("left")]
    public double? Left { get; set; }

    [JsonPropertyName("top")]
    public double? Top { get; set; }

    /// <summary>用户拖动过面板才置 true；未拖过 = 每次弹在鼠标所在屏的默认位（修正 2026-10-16 位置错误后自愈旧数据）。</summary>
    [JsonPropertyName("placed")]
    public bool Placed { get; set; }
}

public sealed class SearchConfig
{
    [JsonPropertyName("everythingEnabled")]
    public bool EverythingEnabled { get; set; } = true;

    /// <summary>es.exe 路径；留空 = 自动探测（应用 tools\ → 注册表 Everything 安装目录 → 常见路径）。</summary>
    [JsonPropertyName("esPath")]
    public string EsPath { get; set; } = "";

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 30;
}

public sealed class ThemeConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "dark"; // dark | light | auto

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#4A9EFF";
}
