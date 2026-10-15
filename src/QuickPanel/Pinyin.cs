using System.IO;
using System.Text;
using System.Windows;

namespace QuickPanel;

/// <summary>
/// 离线拼音首字母表（assets\pinyin-initials.txt，源自 mozillazg/pinyin-data，CJK 26171 字）。
/// 用于磁贴/程序层的拼音首字母匹配；全盘文件层的拼音依赖 Everything ≥1.5 的 pinyin: 修饰符。
/// </summary>
public static class Pinyin
{
    private static Dictionary<char, char>? _map;

    public static void Init()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/assets/pinyin-initials.txt");
            var resource = Application.GetResourceStream(uri);
            if (resource is null)
            {
                Logger.Log("pinyin table missing (resource not found)");
                return;
            }
            using var reader = new StreamReader(resource.Stream);
            var map = new Dictionary<char, char>(26000);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                // 行格式：U+4E2D,z
                if (line.Length < 7 || !line.StartsWith("U+") || line[6] != ',')
                {
                    continue;
                }
                if (!int.TryParse(line.AsSpan(2, 4), System.Globalization.NumberStyles.HexNumber, null, out var cp))
                {
                    continue;
                }
                map[(char)cp] = line[6];
            }
            _map = map;
            Logger.Log($"pinyin table loaded: {map.Count} chars");
        }
        catch (Exception ex)
        {
            Logger.Log($"pinyin table load FAILED: {ex.Message}");
        }
    }

    /// <summary>取文本的检索键：ASCII 字母数字原样小写，CJK 取拼音首字母，其余跳过。</summary>
    public static string Initials(string text)
    {
        if (_map is null || string.IsNullOrEmpty(text))
        {
            return "";
        }
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length;)
        {
            var ch = text[i];
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i += 2; // 扩展区字符不在表内，跳过
                continue;
            }
            i++;
            var lower = char.ToLowerInvariant(ch);
            if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
            {
                sb.Append(lower);
                continue;
            }
            if (_map.TryGetValue(ch, out var initial))
            {
                sb.Append(initial);
            }
        }
        return sb.ToString();
    }
}
