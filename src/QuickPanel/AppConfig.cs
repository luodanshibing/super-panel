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
            var defaults = new AppConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOpts));
            return defaults;
        }

        var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOpts) ?? new AppConfig();
        loaded.Normalize();
        File.WriteAllText(path, JsonSerializer.Serialize(loaded, JsonOpts)); // 补齐缺失键，用户始终能看到完整 schema
        return loaded;
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
    }
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
