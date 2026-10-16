using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace QuickPanel;

/// <summary>
/// 全局热键：RegisterHotKey + 消息窗口。"Ctrl+Win+Space" 这类组合串在这里解析。
/// 注册失败（被占用等）返回 false，由调用方提示，绝不静默失灵。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xB00B;
    private static readonly IntPtr HwndMessage = new(-3); // HWND_MESSAGE

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private bool _registered;

    public bool IsRegistered => _registered;

    public event Action? Pressed;

    public bool Register(string combo)
    {
        var (mods, vk) = ParseCombo(combo);
        var parameters = new HwndSourceParameters("QuickPanelHotkey")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            ParentWindow = HwndMessage,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _registered = RegisterHotKey(_source.Handle, HotkeyId, mods, vk);
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt64() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    internal static (uint mods, uint vk) ParseCombo(string combo)
    {
        uint mods = 0;
        uint vk = 0;
        var hasKey = false;

        foreach (var raw in combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= 0x2; break;
                case "alt": mods |= 0x1; break;
                case "shift": mods |= 0x4; break;
                case "win" or "windows": mods |= 0x8; break;
                default:
                    vk = KeyToVk(raw);
                    hasKey = true;
                    break;
            }
        }

        if (!hasKey)
        {
            throw new ArgumentException($"热键缺少主键：{combo}");
        }

        return (mods, vk);
    }

    private static uint KeyToVk(string k)
    {
        var key = k.ToLowerInvariant();
        if (key is "space" or "空格") return 0x20;
        if (key is "`" or "~" or "oem3" or "`") return 0xC0;
        if (key.Length == 1 && char.IsAsciiLetter(key[0])) return key.ToUpperInvariant()[0]; // A-Z
        if (key.Length == 1 && char.IsAsciiDigit(key[0])) return key[0]; // 0-9
        if (key.Length >= 2 && key[0] == 'f' && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24)
        {
            return (uint)(0x70 + f - 1); // F1-F24
        }
        throw new ArgumentException($"不支持的热键主键：{k}");
    }

    public void Dispose()
    {
        if (_registered && _source is not null)
        {
            try { UnregisterHotKey(_source.Handle, HotkeyId); } catch { /* ignore */ }
        }
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
        _registered = false;
    }
}
