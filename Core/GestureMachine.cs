using System;
using System.Collections.Generic;

namespace LoLPing.Core;

[Flags]
public enum Modifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Command = 4, // 对应 Mac 的 Option(Alt)，Windows 上即 Alt
    Shift = 8,
    Win = 16,
    /// <summary>虚拟位：附加触发键（如 V）已按下时置位，与修饰键一起参与组合匹配。</summary>
    Extra = 32,
}

/// <summary>
/// 纯手势状态机，逐行移植自 Mac 版 GestureMachine（坐标改为 y 向下的物理像素）。
/// 平台层负责时钟、输入钩子与渲染。
/// </summary>
public sealed class GestureMachine
{
    public enum Phase { Idle, Arming, Open, Blocked }

    public enum ActionKind { Arm, Show, Hover, Hide, Dismiss, Commit }

    public sealed class GestureAction
    {
        public ActionKind Kind { get; init; }
        public PingKind Ping { get; init; } = PingKind.Generic;
        public double? Angle { get; init; }
        public double X { get; init; }
        public double Y { get; init; }

        public static GestureAction Arm(double x, double y) => new() { Kind = ActionKind.Arm, X = x, Y = y };
        public static GestureAction Show(double x, double y) => new() { Kind = ActionKind.Show, X = x, Y = y };
        public static GestureAction Hover(PingKind kind, double? angle) => new() { Kind = ActionKind.Hover, Ping = kind, Angle = angle };
        public static readonly GestureAction Hide = new() { Kind = ActionKind.Hide };
        public static readonly GestureAction Dismiss = new() { Kind = ActionKind.Dismiss };
        public static GestureAction Commit(PingKind kind, double x, double y) => new() { Kind = ActionKind.Commit, Ping = kind, X = x, Y = y };
    }

    public Phase CurrentPhase { get; private set; } = Phase.Idle;
    public PingKind Selected { get; private set; } = PingKind.Generic;
    public (double X, double Y) Anchor { get; private set; }
    public Modifiers CurrentModifiers { get; private set; }

    /// <summary>触发组合键（例如 Ctrl|Alt|Win）。可自定义。</summary>
    public Modifiers Chord { get; set; } = Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win;

    /// <summary>中心死区半径（物理像素），= 66 × scale × dpiScale。</summary>
    public double DeadZone { get; set; } = WheelGeometry.CenterRadius;

    private double _centerX, _centerY;
    private double _pointerX, _pointerY;

    /// <summary>修饰键集合变化。otherInputHeld：按住鼠标键或普通键。</summary>
    public List<GestureAction> FlagsChanged(Modifiers flags, double pointX, double pointY, bool otherInputHeld)
    {
        CurrentModifiers = flags;
        if (CurrentPhase == Phase.Blocked)
        {
            if ((flags & Chord) == Modifiers.None) CurrentPhase = Phase.Idle;
            return new List<GestureAction>();
        }
        if (CurrentPhase == Phase.Idle)
        {
            if (flags != Chord) return new List<GestureAction>();
            if (otherInputHeld) { CurrentPhase = Phase.Blocked; return new List<GestureAction>(); }
            Anchor = (pointX, pointY);
            _centerX = pointX; _centerY = pointY;
            _pointerX = pointX; _pointerY = pointY;
            Selected = PingKind.Generic;
            CurrentPhase = Phase.Arming;
            return new List<GestureAction> { GestureAction.Arm(pointX, pointY) };
        }
        // 按下第四个修饰键属于另一个快捷键，永不提交。
        if ((flags & ~Chord) != Modifiers.None) return Cancel();
        if (flags != Chord)
        {
            bool wasOpen = CurrentPhase == Phase.Open;
            CurrentPhase = (flags & Chord) == Modifiers.None ? Phase.Idle : Phase.Blocked;
            if (wasOpen)
                return new List<GestureAction> { GestureAction.Dismiss, GestureAction.Commit(Selected, Anchor.X, Anchor.Y) };
            return new List<GestureAction> { GestureAction.Hide };
        }
        return new List<GestureAction>();
    }

    /// <summary>0.18 秒延时到期。</summary>
    public List<GestureAction> DelayElapsed()
    {
        if (CurrentPhase != Phase.Arming || CurrentModifiers != Chord)
            return new List<GestureAction>();
        CurrentPhase = Phase.Open;
        return new List<GestureAction> { GestureAction.Show(Anchor.X, Anchor.Y) };
    }

    /// <summary>屏幕边缘时轮盘内移，但原始锚点保持不变。</summary>
    public List<GestureAction> SetWheelCenter(double x, double y)
    {
        _centerX = x; _centerY = y;
        if (Math.Sqrt((_pointerX - Anchor.X) * (_pointerX - Anchor.X) + (_pointerY - Anchor.Y) * (_pointerY - Anchor.Y)) <= 4)
            return new List<GestureAction>();
        return MovedTo(_pointerX, _pointerY);
    }

    public List<GestureAction> MovedTo(double x, double y)
    {
        _pointerX = x; _pointerY = y;
        if (CurrentPhase != Phase.Open) return new List<GestureAction>();
        var next = WheelGeometry.Selection(x, y, _centerX, _centerY, DeadZone);
        Selected = next;
        return new List<GestureAction>
        {
            GestureAction.Hover(next, WheelGeometry.Direction(x, y, _centerX, _centerY, DeadZone))
        };
    }

    public List<GestureAction> Cancel()
    {
        bool hadGesture = CurrentPhase == Phase.Arming || CurrentPhase == Phase.Open;
        CurrentPhase = (CurrentModifiers & Chord) == Modifiers.None ? Phase.Idle : Phase.Blocked;
        Selected = PingKind.Generic;
        return hadGesture ? new List<GestureAction> { GestureAction.Hide } : new List<GestureAction>();
    }

    public void Reset(bool blockUntilRelease = false)
    {
        CurrentPhase = blockUntilRelease ? Phase.Blocked : Phase.Idle;
        Selected = PingKind.Generic;
        CurrentModifiers = Modifiers.None;
    }
}
