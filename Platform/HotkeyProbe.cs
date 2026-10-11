using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using LoLPing.Core;

namespace LoLPing.Platform;

/// <summary>
/// 全局快捷键冲突检测：
/// 1) 用 RegisterHotKey 在隐藏消息窗口上实测注册（失败=已被系统/其他程序占用）；
/// 2) 已知冲突启发式（Intel 显卡驱动热键、输入法切换键等）。
/// 纯修饰键组合（不带 vk）不能直接注册，用 F13~F15 这类罕见的 vk 试探。
/// </summary>
public static class HotkeyProbe
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static HwndSource? _source;
    private static int _nextId = 0xA000;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static IntPtr Handle
    {
        get
        {
            if (_source == null)
            {
                // 0×0 无样式窗口 + 立即隐藏：只用于 RegisterHotKey 探测，绝不显示
                var p = new HwndSourceParameters("LoLPingProbe", 0, 0)
                {
                    WindowStyle = 0,       // 去掉默认 WS_VISIBLE|WS_CAPTION
                    ExtendedWindowStyle = 0x80 /*WS_EX_TOOLWINDOW*/ | 0x08000000 /*WS_EX_NOACTIVATE*/,
                };
                _source = new HwndSource(p);
                ShowWindow(_source.Handle, 0 /*SW_HIDE*/);
            }
            return _source.Handle;
        }
    }

    /// <summary>
    /// 检测组合键可用性。返回 null 表示未见冲突，否则返回警告文案。
    /// triggerVk = 0 表示纯修饰键组合。
    /// </summary>
    public static string? Probe(Modifiers chordMods, uint triggerVk)
    {
        var warnings = new List<string>();

        // 1) 实测注册
        uint fs = 0;
        if (chordMods.HasFlag(Modifiers.Ctrl)) fs |= MOD_CONTROL;
        if (chordMods.HasFlag(Modifiers.Alt)) fs |= MOD_ALT;
        if (chordMods.HasFlag(Modifiers.Shift)) fs |= MOD_SHIFT;
        if (chordMods.HasFlag(Modifiers.Win)) fs |= MOD_WIN;

        bool registered = false;
        try
        {
            if (triggerVk != 0)
            {
                registered = TryOne(fs, triggerVk);
                if (!registered) warnings.Add("该组合键已被系统或其他程序占用（注册失败）。");
            }
            else
            {
                // 纯修饰键组合：用罕见 vk 试探修饰键本身是否被劫持
                uint[] probes = { 0x76, 0x77, 0x78 }; // F13 F14 F15
                int ok = 0;
                foreach (var vk in probes)
                    if (TryOne(fs, vk)) ok++;
                if (ok < probes.Length)
                    warnings.Add("该组合键疑似已被其他程序注册（部分试探失败），若无反应请更换。");
            }
        }
        catch (Exception ex)
        {
            warnings.Add("冲突检测不可用：" + ex.Message);
        }

        // 2) 已知冲突启发式
        if (chordMods == (Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win) && triggerVk == 0)
            warnings.Add("提示：Ctrl+Alt+Win 常被 Intel 显卡驱动用作屏幕旋转热键，若无反应请换组合。");
        if (chordMods.HasFlag(Modifiers.Alt) && chordMods.HasFlag(Modifiers.Shift)
            && !chordMods.HasFlag(Modifiers.Ctrl) && !chordMods.HasFlag(Modifiers.Win))
            warnings.Add("提示：Shift+Alt 组合常与输入法切换热键冲突，建议加上 Ctrl。");
        if (chordMods.HasFlag(Modifiers.Win) && chordMods.HasFlag(Modifiers.Shift))
            warnings.Add("提示：Win+Shift 组合多被系统窗口管理占用。");
        if (triggerVk == 0x54 && chordMods == (Modifiers.Ctrl | Modifiers.Alt)) // T
            warnings.Add("提示：Ctrl+Alt+T 常见于终端类程序的全局热键。");

        return warnings.Count > 0 ? string.Join("\n", warnings) : null;
    }

    private static bool TryOne(uint fs, uint vk)
    {
        int id = _nextId++;
        bool ok = RegisterHotKey(Handle, id, fs, vk);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != ERROR_HOTKEY_ALREADY_REGISTERED)
                return true; // 其他错误码（如系统保留 vk 0x??）不算明确冲突，放行由实测决定
            return false;
        }
        UnregisterHotKey(Handle, id);
        return true;
    }
}
