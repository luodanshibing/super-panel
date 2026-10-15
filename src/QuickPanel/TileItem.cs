namespace QuickPanel;

/// <summary>面板条目。Type = app | file | folder | url（command 类型留给后续版本）。</summary>
public sealed class TileItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = "app";

    [System.Text.Json.Serialization.JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("workDir")]
    public string WorkDir { get; set; } = "";

    /// <summary>用户指定的自定义图标（.ico/.png 路径），优先于自动提取。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("iconPath")]
    public string IconPath { get; set; } = "";

    /// <summary>自动提取图标的缓存文件名（存于 config\icons\ 下）。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("iconCache")]
    public string IconCache { get; set; } = "";
}
