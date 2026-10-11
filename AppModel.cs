using System;
using System.Windows;
using Microsoft.Win32;
using LoLPing.Assets;
using LoLPing.Core;
using LoLPing.Input;
using LoLPing.Overlays;
using static LoLPing.Core.GestureMachine;

namespace LoLPing;

/// <summary>
/// 应用模型：连接输入、悬浮层与音效。对应 Mac 版 AppModel。
/// </summary>
public sealed class AppModel
{
    public AppSettings Settings { get; }
    public bool IsEnabled { get; private set; }
    public string Message { get; private set; } = "全局信号已关闭。你仍可在下方预览效果。";
    /// <summary>状态变化事件（UI 订阅以刷新）。</summary>
    public event Action? OnStateChange;
    /// <summary>组合键被检测到、轮盘即将呼出（用于 UI 即时反馈"快捷键有效"）。</summary>
    public event Action? OnGestureArm;

    private readonly GlobalInput _input = new();
    private readonly OverlayController _overlay = new();
    private readonly SoundPlayer _sound = new();

    /// <summary>诊断：设置路径后记录手势动作，用于免 UI 验证输入链路。</summary>
    public string? TracePath { get; set; }

    private void Trace(string text)
    {
        if (TracePath == null) return;
        try { System.IO.File.AppendAllText(TracePath, $"[{DateTime.Now:HH:mm:ss.fff}] {text}\n"); } catch { }
    }

    public AppModel()
    {
        Settings = AppSettings.Load();
        _input.OnAction = Handle;
        _input.OnTrace = Trace;

        SystemEvents.PowerModeChanged += (_, e) =>
        {
            var mode = e.Mode.ToString();
            if (mode == "Suspend" || mode == "Resume") CancelEffects();
        };
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff) CancelEffects();
        };
    }

    public void RestoreState()
    {
        if (Settings.Enabled) SetEnabled(true);
        if (!VectorAssets.HasAll())
            Message = "素材缺失：请确认 Assets 目录随程序完整分发。";
        OnStateChange?.Invoke();
    }

    public void SetEnabled(bool value)
    {
        if (!value)
        {
            _input.Stop(); _overlay.Clear(); _sound.StopAll();
            IsEnabled = false;
            Settings.Enabled = false; Settings.Save();
            Message = "全局信号已关闭。你仍可在下方预览效果。";
            OnStateChange?.Invoke();
            return;
        }
        try
        {
            Trace($"SetEnabled(true) chord={Settings.ChordModifiers} trigger={Settings.TriggerKey} scale={Settings.Scale}");
            _input.SetRenderScale(Settings.Scale);
            _input.Start(Settings.ChordModifiers, TriggerVk, Settings.Scale);
            Trace($"sounds loaded: {_sound.ReadyCount}/9");
            IsEnabled = true;
            Settings.Enabled = true; Settings.Save();
            Message = $"已就绪。按住 {ChordDisplay()}，移动选择，松开发送。";
        }
        catch (Exception ex)
        {
            IsEnabled = false;
            Settings.Enabled = false; Settings.Save();
            Message = ex.Message;
        }
        OnStateChange?.Invoke();
    }

    public uint TriggerVk => AppSettings.TriggerToVk(Settings.TriggerKey);

    public void SetChord(Modifiers chord)
    {
        Settings.ChordModifiers = chord;
        Settings.Save();
        RestartInputIfNeeded();
    }

    public void SetTriggerKey(string? key)
    {
        Settings.TriggerKey = key ?? "";
        Settings.Save();
        RestartInputIfNeeded();
    }

    public void SetVolume(double volume)
    {
        Settings.Volume = Math.Clamp(volume, 0, 1);
        Settings.Save();
    }

    public void SetScale(double scale)
    {
        Settings.Scale = Math.Clamp(scale, 0.75, 1.5);
        Settings.Save();
        RestartInputIfNeeded();
    }

    /// <summary>预览：在屏幕中央播放落点特效 + 音效（对应 Mac preview()）。</summary>
    public void Preview(PingKind kind)
    {
        var wa = Platform.Native.WorkAreaForPoint(
            SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        double cx = (wa.Left + wa.Right) / 2.0, cy = (wa.Top + wa.Bottom) / 2.0;
        _overlay.ShowPing(kind, cx, cy, Settings.Scale);
        _sound.Play(kind, Settings.Volume);
    }

    public void CancelEffects()
    {
        _input.CancelCurrent();
        _overlay.Clear();
        _sound.StopAll();
    }

    public void Shutdown()
    {
        _input.Stop(); _overlay.Clear(); _sound.StopAll();
    }

    private void RestartInputIfNeeded()
    {
        CancelEffects();
        if (IsEnabled) SetEnabled(true);
        OnStateChange?.Invoke();
    }

    private void Handle(GestureAction action)
    {
        if (!IsEnabled) return;
        if (action.Kind != ActionKind.Hover) Trace($"{action.Kind} ping={action.Ping} at=({action.X:F0},{action.Y:F0})");
        switch (action.Kind)
        {
            case ActionKind.Arm:
                OnGestureArm?.Invoke();
                break;
            case ActionKind.Show:
                var center = _overlay.ShowWheel(action.X, action.Y, Settings.Scale);
                _input.SetWheelCenter(center.X, center.Y);
                break;
            case ActionKind.Hover:
                _overlay.Select(action.Ping, action.Angle);
                break;
            case ActionKind.Hide:
                _overlay.HideWheel(false);
                break;
            case ActionKind.Dismiss:
                _overlay.HideWheel(true);
                break;
            case ActionKind.Commit:
                _overlay.ShowPing(action.Ping, action.X, action.Y, Settings.Scale);
                _sound.Play(action.Ping, Settings.Volume);
                if (_sound.LastError is string sndErr) Trace("sound: " + sndErr); // MediaFailed 异步，最多滞后一次提交可见
                break;
        }
    }

    public static string ChordSymbols(Modifiers m)
    {
        string[] parts = new string[4];
        int n = 0;
        if (m.HasFlag(Modifiers.Ctrl)) parts[n++] = "Ctrl";
        if (m.HasFlag(Modifiers.Alt)) parts[n++] = "Alt";
        if (m.HasFlag(Modifiers.Shift)) parts[n++] = "Shift";
        if (m.HasFlag(Modifiers.Win)) parts[n++] = "Win";
        return string.Join(" + ", parts, 0, n);
    }

    /// <summary>组合键的完整显示文本（含附加触发键）。</summary>
    public string ChordDisplay()
    {
        var t = ChordSymbols(Settings.ChordModifiers);
        if (!string.IsNullOrWhiteSpace(Settings.TriggerKey)) t += " + " + Settings.TriggerKey;
        return t;
    }
}
