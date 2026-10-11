using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using LoLPing.Core;
using LoLPing.Platform;
using static LoLPing.Core.GestureMachine;
using static LoLPing.Platform.Native;

namespace LoLPing.Input;

/// <summary>
/// 全局输入。对应 Mac 版 GlobalInput：
/// CGEventTap → WH_KEYBOARD_LL + WH_MOUSE_LL；辅助功能授权 → 无需（低级钩子免授权）。
///
/// 稳定性要点（对应历次修复）：
/// 1. 修饰键用“物理键码集合”维护，不查 GetAsyncKeyState（钩子内不可靠），
///    也不做 +1/-1 计数（自动重复事件会导致计数泄漏、修饰键卡死）。
/// 2. 过滤 LLKHF_REPEAT 自动重复事件，不改变任何状态。
/// 3. 钩子回调内只做状态机计算，渲染/声音通过 BeginInvoke 异步派发，
///    避免 UI 线程忙时超过 LowLevelHooksTimeout(300ms) 被系统静默摘钩。
/// 4. 看门狗在静默期自动重装钩子，即使被摘也能在 10 秒内自愈。
/// </summary>
public sealed class GlobalInput
{
    public GestureMachine Machine { get; } = new();
    public Action<GestureAction>? OnAction;
    public Action<string>? OnTrace;
    private int _traceBudget = 1000;
    private void Trace(string s) => OnTrace?.Invoke(s);

    private IntPtr _kbdHook, _mouseHook;
    private HookProc? _kbdProc, _mouseProc; // 保持引用防 GC
    private DispatcherTimer? _armTimer;
    private DispatcherTimer? _watchdog;
    private readonly HashSet<uint> _otherKeysDown = new();
    private readonly HashSet<uint> _modKeysDown = new();
    private uint _triggerVk;            // 0 = 无附加触发键
    private bool _triggerDown;
    private bool _swallowedRight;
    private bool _running;
    private readonly Dispatcher _dispatcher;

    public GlobalInput() => _dispatcher = Dispatcher.CurrentDispatcher;

    // 左右修饰键 → 组
    private static readonly uint[] CtrlKeys = { VK_CONTROL, VK_LCONTROL, VK_RCONTROL };
    private static readonly uint[] AltKeys = { VK_MENU, VK_LMENU, VK_RMENU };
    private static readonly uint[] ShiftKeys = { VK_SHIFT, VK_LSHIFT, VK_RSHIFT };
    private static readonly uint[] WinKeys = { VK_LWIN, VK_RWIN };

    public void Start(Modifiers chordMods, uint triggerVk, double scale)
    {
        Stop();
        _running = true;
        _triggerVk = triggerVk;
        Machine.Chord = triggerVk == 0 ? chordMods : chordMods | Modifiers.Extra;
        Machine.DeadZone = WheelGeometry.CenterRadius * scale;
        Machine.Reset(blockUntilRelease: PhysicalModifiers() == Machine.Chord && Machine.Chord != Modifiers.None);

        InstallHooks();

        // 看门狗：静默期（空闲且无按键）每 10s 重装钩子，被系统摘钩也能自愈。
        _watchdog = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
        _watchdog.Tick += (_, _) =>
        {
            if (!_running) return;
            if (Machine.CurrentPhase != Phase.Idle) return;
            if (_modKeysDown.Count > 0 || _otherKeysDown.Count > 0 || _triggerDown) return;
            InstallHooks();
            _traceBudget = Math.Max(_traceBudget, 200); // 重装后保留一定诊断余量
            Trace("watchdog: hooks reinstalled");
        };
        _watchdog.Start();
    }

    private void InstallHooks()
    {
        if (_kbdHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbdHook); _kbdHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        _kbdProc = KeyboardProc;
        _mouseProc = MouseProc;
        _kbdHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbdProc, GetModuleHandle(null), 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
        if (_kbdHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            throw new InvalidOperationException("无法安装全局键盘/鼠标钩子。");
    }

    public void Stop()
    {
        _running = false;
        _watchdog?.Stop(); _watchdog = null;
        _armTimer?.Stop(); _armTimer = null;
        if (_kbdHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbdHook); _kbdHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        _kbdProc = null; _mouseProc = null;
        _otherKeysDown.Clear();
        _modKeysDown.Clear();
        _triggerDown = false;
        _swallowedRight = false;
        Machine.Reset();
    }

    public void CancelCurrent()
    {
        _armTimer?.Stop(); _armTimer = null;
        Dispatch(Machine.Cancel());
    }

    public void SetWheelCenter(double x, double y) => Dispatch(Machine.SetWheelCenter(x, y));

    // ---------------- 键盘 ----------------

    private static bool IsModifierVk(uint vk) =>
        vk is VK_CONTROL or VK_LCONTROL or VK_RCONTROL
           or VK_MENU or VK_LMENU or VK_RMENU
           or VK_SHIFT or VK_LSHIFT or VK_RSHIFT
           or VK_LWIN or VK_RWIN;

    /// <summary>物理键集合 → 修饰键组合（不受重复事件/异步状态时序影响）。</summary>
    private Modifiers TrackedModifiers()
    {
        Modifiers m = Modifiers.None;
        foreach (var vk in _modKeysDown)
        {
            if (Contains(CtrlKeys, vk)) m |= Modifiers.Ctrl;
            else if (Contains(AltKeys, vk)) m |= Modifiers.Alt;
            else if (Contains(ShiftKeys, vk)) m |= Modifiers.Shift;
            else if (Contains(WinKeys, vk)) m |= Modifiers.Win;
        }
        if (_triggerDown) m |= Modifiers.Extra;
        return m;
    }

    private static bool Contains(uint[] arr, uint vk)
    {
        foreach (var a in arr) if (a == vk) return true;
        return false;
    }

    private Modifiers PhysicalModifiers()
    {
        Modifiers m = Modifiers.None;
        if (IsDown(VK_CONTROL)) m |= Modifiers.Ctrl;
        if (IsDown(VK_MENU)) m |= Modifiers.Alt;
        if (IsDown(VK_SHIFT)) m |= Modifiers.Shift;
        if (IsDown(VK_LWIN) || IsDown(VK_RWIN)) m |= Modifiers.Win;
        return m;
    }

    private bool GestureActive => Machine.CurrentPhase is Phase.Arming or Phase.Open;

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_kbdHook, nCode, wParam, lParam);
        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int msg = (int)wParam;
        uint vk = info.vkCode;

        bool isDownMsg = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool isUpMsg = msg is WM_KEYUP or WM_SYSKEYUP;
        bool isRepeat = (info.flags & 0x40) != 0; // LLKHF_REPEAT：长按自动重复，忽略

        // ---- 修饰键 ----
        if (IsModifierVk(vk))
        {
            if (!isRepeat)
            {
                if (isDownMsg) _modKeysDown.Add(vk);
                else if (isUpMsg) _modKeysDown.Remove(vk);
                var flags = TrackedModifiers();
                if (_traceBudget-- > 0) Trace($"MOD vk=0x{vk:X2} msg=0x{msg:X} flags={flags} phase={Machine.CurrentPhase}");
                GetCursorPos(out var p);
                bool otherHeld = MouseButtonsHeld() || _otherKeysDown.Count > 0;
                Dispatch(Machine.FlagsChanged(flags, p.X, p.Y, otherHeld));
            }
            // 手势进行中吞掉 Win 键（含自动重复与最终抬起），防开始菜单弹出。
            if ((vk == VK_LWIN || vk == VK_RWIN)
                && (Machine.Chord & Modifiers.Win) != 0
                && Machine.CurrentPhase != Phase.Idle)
                return (IntPtr)1;
            return IntPtr.Zero;
        }

        // ---- 附加触发键（如 V）----
        if (_triggerVk != 0 && vk == _triggerVk)
        {
            if (!isRepeat)
            {
                if (isDownMsg) _triggerDown = true;
                else if (isUpMsg) _triggerDown = false;
                var flags = TrackedModifiers();
                if (_traceBudget-- > 0) Trace($"TRG vk=0x{vk:X2} msg=0x{msg:X} flags={flags} phase={Machine.CurrentPhase}");
                GetCursorPos(out var p);
                bool otherHeld = MouseButtonsHeld() || _otherKeysDown.Count > 0;
                Dispatch(Machine.FlagsChanged(flags, p.X, p.Y, otherHeld));
            }
            if (Machine.CurrentPhase != Phase.Idle) return (IntPtr)1; // 手势期间吞掉，防漏字进游戏
            return IntPtr.Zero;
        }

        // ---- 普通键 ----
        if (isDownMsg)
        {
            if (!isRepeat)
            {
                _otherKeysDown.Add(vk);
                if (_traceBudget-- > 0) Trace($"KEY vk=0x{vk:X2} down phase={Machine.CurrentPhase} mods={TrackedModifiers()}");
                bool active = GestureActive;
                CancelCurrent();
                if (active && vk == VK_ESCAPE) return (IntPtr)1;
            }
        }
        else if (isUpMsg)
        {
            _otherKeysDown.Remove(vk);
        }
        return IntPtr.Zero;
    }

    // ---------------- 鼠标 ----------------

    private static bool MouseButtonsHeld() =>
        IsDown(VK_LBUTTON) || IsDown(VK_RBUTTON) || IsDown(VK_MBUTTON) ||
        IsDown(VK_XBUTTON1) || IsDown(VK_XBUTTON2);

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        int msg = (int)wParam;

        switch (msg)
        {
            case WM_MOUSEMOVE:
                if (Machine.CurrentPhase == Phase.Open)
                    Dispatch(Machine.MovedTo(info.x, info.y));
                return IntPtr.Zero;

            case WM_RBUTTONDOWN:
                bool active = GestureActive;
                CancelCurrent();
                if (active) { _swallowedRight = true; return (IntPtr)1; }
                return IntPtr.Zero;

            case WM_RBUTTONUP:
                if (_swallowedRight) { _swallowedRight = false; return (IntPtr)1; }
                return IntPtr.Zero;

            case WM_LBUTTONDOWN:
            case WM_MBUTTONDOWN:
            case WM_XBUTTONDOWN:
            case WM_MOUSEWHEEL:
            case WM_NCLBUTTONDOWN:
            case WM_NCMBUTTONDOWN:
            case WM_NCRBUTTONDOWN:
                CancelCurrent();
                return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    // ---------------- 派发 ----------------

    private void Dispatch(List<GestureAction> actions)
    {
        foreach (var action in actions)
        {
            switch (action.Kind)
            {
                case ActionKind.Arm:
                    _armTimer?.Stop();
                    _armTimer = new DispatcherTimer(DispatcherPriority.Send)
                    { Interval = TimeSpan.FromMilliseconds(Math.Max(1, TriggerDelayMs)) };
                    _armTimer.Tick += (_, _) =>
                    {
                        _armTimer?.Stop(); _armTimer = null;
                        Machine.DeadZone = WheelGeometry.CenterRadius * _renderScale
                            * DpiScaleForPoint(Machine.Anchor.X, Machine.Anchor.Y);
                        Dispatch(Machine.DelayElapsed());
                    };
                    _armTimer.Start();
                    break;
                case ActionKind.Hide:
                case ActionKind.Dismiss:
                    _armTimer?.Stop(); _armTimer = null;
                    Post(action);
                    break;
                default:
                    Post(action);
                    break;
            }
        }
    }

    /// <summary>渲染/声音等副作用移出钩子回调：回调必须尽快返回，避免被摘钩。</summary>
    private void Post(GestureAction action)
    {
        var handler = OnAction;
        if (handler == null) return;
        _dispatcher.BeginInvoke(new Action(() => handler(action)), DispatcherPriority.Send);
    }

    private double _renderScale = 1.0;
    public void SetRenderScale(double scale) => _renderScale = scale;

    /// <summary>呼出延时（毫秒）。0 = 瞬时（内部按 1ms 触发）。</summary>
    public int TriggerDelayMs { get; set; } = 60;
}
