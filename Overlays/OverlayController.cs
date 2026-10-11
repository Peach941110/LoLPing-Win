using System;
using System.Collections.Generic;
using System.Windows.Threading;
using LoLPing.Core;
using LoLPing.Platform;
using static LoLPing.Platform.Native;

namespace LoLPing.Overlays;

/// <summary>
/// 悬浮窗编排。对应 Mac 版 OverlayController：
/// 轮盘窗（300×scale，夹到工作区内）、落点特效窗（220×scale，最多 16 个，2.05s 自毁）。
/// 所有入参坐标为物理像素（y 向下），窗口定位时按所在屏 DPI 换算为 WPF DIU。
/// </summary>
public sealed class OverlayController
{
    private WheelWindow? _wheel;
    private readonly List<PingEffectWindow> _effects = new();
    private readonly Dispatcher _dispatcher;

    public OverlayController() => _dispatcher = Dispatcher.CurrentDispatcher;

    /// <summary>在锚点处弹出轮盘，返回轮盘中心（物理像素，已夹到工作区）。</summary>
    public (double X, double Y) ShowWheel(double anchorX, double anchorY, double scale)
    {
        HideWheel(false);
        double dpi = DpiScaleForPoint(anchorX, anchorY);
        double sideDiu = 300 * scale;
        double sidePx = sideDiu * dpi;
        double radius = sidePx / 2;

        var work = WorkAreaForPoint(anchorX, anchorY);
        double cx = anchorX, cy = anchorY;
        WheelGeometry.ClampCenter(ref cx, ref cy, work.Left, work.Top, work.Right, work.Bottom, radius);

        var win = new WheelWindow
        {
            Width = sideDiu,
            Height = sideDiu,
            Left = (cx - sidePx / 2) / dpi,
            Top = (cy - sidePx / 2) / dpi,
        };
        win.Show();
        win.View.Open();
        _wheel = win;
        return (cx, cy);
    }

    public void Select(PingKind kind, double? angle) => _wheel?.View.Select(kind, angle);

    public void HideWheel(bool animated = true)
    {
        var win = _wheel;
        if (win == null) return;
        _wheel = null;
        win.View.Stop();
        if (!animated) { win.Close(); return; }
        win.FadeAndClose();
    }

    /// <summary>在锚点处播放落点特效。footprint 中心精确落在锚点上。</summary>
    public void ShowPing(PingKind kind, double anchorX, double anchorY, double scale)
    {
        if (_effects.Count >= 16)
        {
            var oldest = _effects[0];
            _effects.RemoveAt(0);
            CloseEffect(oldest);
        }
        double dpi = DpiScaleForPoint(anchorX, anchorY);
        double sideDiu = 220 * scale;
        double sidePx = sideDiu * dpi;
        // 本地脚点 (side/2, side/2 + 13s)：窗口原点需向回偏移
        double groundLocalPx = (sideDiu / 2 + 13 * scale) * dpi;

        var win = new PingEffectWindow(kind)
        {
            Width = sideDiu,
            Height = sideDiu,
            Left = (anchorX - sidePx / 2) / dpi,
            Top = (anchorY - groundLocalPx) / dpi,
        };
        win.Show();
        win.View.Start();
        _effects.Add(win);

        var cleanup = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2.05) };
        cleanup.Tick += (_, _) =>
        {
            cleanup.Stop();
            if (_effects.Remove(win)) CloseEffect(win);
        };
        cleanup.Start();
    }

    private static void CloseEffect(PingEffectWindow win)
    {
        win.View.Stop();
        try { win.Close(); } catch { }
    }

    /// <summary>立即清空全部悬浮（对应 Mac 版 clear()，用于总开关关闭/会话切换）。</summary>
    public void Clear()
    {
        HideWheel(false);
        foreach (var e in _effects) CloseEffect(e);
        _effects.Clear();
    }
}
