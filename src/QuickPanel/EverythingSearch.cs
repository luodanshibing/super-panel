using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickPanel;

/// <summary>
/// Everything 通道（两层）：
/// ① SDK（Everything64.dll，随应用分发，P/Invoke 直连 IPC）——实测单查询 80-115ms，首选；
/// ② es.exe 子进程——兜底（本机实测每次 ~600ms 固定开销）。
/// es.exe 重定向输出是系统 ANSI 编码（中文系统 = GBK）；SDK 全程 UTF-16 无编码问题。
/// pinyin: 修饰符只有 Everything ≥1.5 支持，由调用方按版本决定是否使用。
/// </summary>
public static class EverythingSearch
{
    private static bool _providerRegistered;
    private static bool _sdkTried;
    private static bool _sdkAvailable;
    private static readonly object SdkLock = new();

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern void Everything_SetSearchW(string search);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern bool Everything_QueryW(bool wait);

    [DllImport("Everything64.dll")]
    private static extern uint Everything_GetNumResults();

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern void Everything_GetResultFullPathNameW(uint index, StringBuilder buffer, uint size);

    [DllImport("Everything64.dll")]
    private static extern void Everything_Reset();

    [DllImport("Everything64.dll")]
    private static extern void Everything_SetMax(uint max);

    /// <summary>加载 SDK DLL（tools\Everything64.dll）。失败不影响 es.exe 兜底。</summary>
    public static void EnsureSdk()
    {
        if (_sdkTried)
        {
            return;
        }
        _sdkTried = true;
        try
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "tools", "Everything64.dll");
            if (!File.Exists(dll))
            {
                Logger.Log("Everything SDK dll missing → es.exe 兜底");
                return;
            }
            NativeLibrary.Load(dll);
            _sdkAvailable = true;
            Logger.Log("Everything SDK loaded (Everything64.dll) — 快速通道就绪");
        }
        catch (Exception ex)
        {
            Logger.Log($"Everything SDK load FAILED: {ex.Message} → es.exe 兜底");
        }
    }

    /// <summary>快速查询：SDK 可用走 SDK（~80-115ms），否则 es.exe 子进程（~600ms+）。</summary>
    public static Task<List<string>> QueryFastAsync(string esPath, string query, int max)
    {
        EnsureSdk();
        if (_sdkAvailable)
        {
            return Task.Run(() => SdkQuery(query, max));
        }
        return QueryAsync(esPath, query, max, CancellationToken.None);
    }

    private static List<string> SdkQuery(string query, int max)
    {
        lock (SdkLock)
        {
            Everything_Reset();
            Everything_SetMax((uint)Math.Clamp(max, 1, 1000));
            Everything_SetSearchW(query);
            if (!Everything_QueryW(true))
            {
                throw new InvalidOperationException("Everything_QueryW 返回 false（Everything 未运行？）");
            }
            var count = Everything_GetNumResults();
            var list = new List<string>((int)Math.Min(count, max));
            var buffer = new StringBuilder(2048);
            for (uint i = 0; i < count && list.Count < max; i++)
            {
                Everything_GetResultFullPathNameW(i, buffer, 2048);
                var path = buffer.ToString();
                if (path.Length > 0)
                {
                    list.Add(path);
                }
            }
            return list;
        }
    }

    public static string? LocateEs(AppConfig config)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.Search.EsPath))
        {
            candidates.Add(config.Search.EsPath);
        }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "tools", "es.exe"));
        foreach (var keyName in new[]
                 {
                     @"SOFTWARE\voidtools\Everything",
                     @"SOFTWARE\WOW6432Node\voidtools\Everything",
                     @"SOFTWARE\WOW6432Node\voidtools\Everything15",
                 })
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyName);
                var location = key?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrEmpty(location))
                {
                    candidates.Add(Path.Combine(location, "es.exe"));
                }
            }
            catch
            {
                // 注册表读不到就跳过
            }
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", "es.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Everything", "es.exe"));

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    public static Version? DetectEverythingVersion(string esPath)
    {
        try
        {
            var everythingExe = Path.Combine(Path.GetDirectoryName(esPath) ?? "", "Everything.exe");
            if (!File.Exists(everythingExe))
            {
                foreach (var keyName in new[] { @"SOFTWARE\voidtools\Everything", @"SOFTWARE\WOW6432Node\voidtools\Everything" })
                {
                    try
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyName);
                        var location = key?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(location))
                        {
                            everythingExe = Path.Combine(location, "Everything.exe");
                            if (File.Exists(everythingExe))
                            {
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            if (!File.Exists(everythingExe))
            {
                return null;
            }
            var info = FileVersionInfo.GetVersionInfo(everythingExe);
            if (Version.TryParse(info.FileVersion ?? "", out var version))
            {
                return version;
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    public static async Task<List<string>> QueryAsync(string esPath, string query, int max, CancellationToken ct)
    {
        if (!_providerRegistered)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _providerRegistered = true;
        }
        var ansi = Encoding.GetEncoding(0);

        var psi = new ProcessStartInfo(esPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ansi,
            StandardErrorEncoding = ansi,
        };
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add(max.ToString());
        psi.ArgumentList.Add(query);

        using var process = Process.Start(psi)!;
        var readTask = process.StandardOutput.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* ignore */ }
            throw;
        }
        var text = await readTask;
        return text
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();
    }
}
