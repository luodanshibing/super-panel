using System.IO;

namespace QuickPanel;

public sealed class ProgramEntry
{
    public string Name = "";
    public string Path = "";
    public string Initials = "";
}

/// <summary>已装程序索引：开始菜单两处 *.lnk，启动时后台建 + 每 6 小时过期重建。</summary>
public sealed class ProgramIndex
{
    private readonly object _lock = new();
    private List<ProgramEntry> _entries = new();
    public DateTime BuiltAt { get; private set; } = DateTime.MinValue;

    public int Count
    {
        get { lock (_lock) { return _entries.Count; } }
    }

    public bool IsStale => BuiltAt == DateTime.MinValue || (DateTime.Now - BuiltAt).TotalHours > 6;

    public void RebuildAsync(Action<int>? onDone)
    {
        Task.Run(() =>
        {
            var list = new List<ProgramEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + @"\Programs",
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) + @"\Programs",
            };
            foreach (var dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    continue;
                }
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }
                foreach (var f in files)
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (name.Length == 0 || !seen.Add(name))
                    {
                        continue; // 同名去重
                    }
                    list.Add(new ProgramEntry { Name = name, Path = f, Initials = Pinyin.Initials(name) });
                }
            }
            lock (_lock)
            {
                _entries = list;
                BuiltAt = DateTime.Now;
            }
            onDone?.Invoke(list.Count);
        });
    }

    public List<ProgramEntry> Search(string query, Func<string, string, bool> matches, int take)
    {
        lock (_lock)
        {
            var result = new List<ProgramEntry>();
            foreach (var entry in _entries)
            {
                if (matches(entry.Name, entry.Initials))
                {
                    result.Add(entry);
                    if (result.Count >= take)
                    {
                        break;
                    }
                }
            }
            return result;
        }
    }
}
