using System.IO;

namespace QuickPanel;

/// <summary>极简追加日志：logs\p0.log。P0 验证用（热键注册结果、面板弹出耗时）。</summary>
public static class Logger
{
    private static string? _dir;

    public static void Init(string dir)
    {
        _dir = dir;
        try { Directory.CreateDirectory(dir); } catch { /* ignore */ }
    }

    public static void Log(string message)
    {
        if (_dir is null)
        {
            return;
        }
        try
        {
            File.AppendAllText(
                Path.Combine(_dir, "p0.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响主功能
        }
    }
}
