using System.IO;
using System.Text.Json;

namespace QuickPanel;

/// <summary>磁贴持久化：config\tiles.json（独立于 config.json，布局即数据）。</summary>
public static class TilesStore
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static List<TileItem> Load(string path)
    {
        if (!File.Exists(path))
        {
            return new List<TileItem>();
        }
        try
        {
            return JsonSerializer.Deserialize<List<TileItem>>(File.ReadAllText(path), Opts) ?? new List<TileItem>();
        }
        catch (Exception ex)
        {
            Logger.Log($"tiles.json 解析失败，按空布局启动：{ex.Message}");
            return new List<TileItem>();
        }
    }

    public static void Save(string path, List<TileItem> tiles)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(tiles, Opts));
    }
}
