using System.Diagnostics;
using System.IO;
using System.Text;

namespace QuickPanel;

/// <summary>
/// Everything 通道：es.exe 子进程（官方 CLI，IPC 到 Everything 引擎）。
/// 注意：es.exe 重定向输出是系统 ANSI 编码（中文系统 = GBK），必须按 ACP 读，否则中文路径乱码。
/// pinyin: 修饰符只有 Everything ≥1.5 支持，由调用方按版本决定是否使用。
/// </summary>
public static class EverythingSearch
{
    private static bool _providerRegistered;

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
