using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LoLPing.Assets;
using LoLPing.Core;

namespace LoLPing.Overlays;

/// <summary>
/// 落点特效绘制。逐段移植自 Mac 版 PingEffectView.draw()：
/// 地面光斑、两层错时扩散波、火花、图标升起+弹跳+辉光，约 2 秒淡出。
/// </summary>
public sealed class PingEffectView : FrameworkElement
{
    private const double Base = 220; // 设计基数

    public PingKind Kind { get; }
    private double _elapsed = 2;
    private DateTime _started;
    private DispatcherTimer? _timer;

    private static double Clamp01(double v) => Math.Min(1, Math.Max(0, v));
    /// <summary>smoothstep，与 Mac 版 ease() 相同。</summary>
    private static double Smooth(double v) { double t = Clamp01(v); return t * t * (3 - 2 * t); }

    public PingEffectView(PingKind kind)
    {
        Kind = kind;
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    public void Start()
    {
        _timer?.Stop();
        _started = DateTime.UtcNow;
        _elapsed = 0;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) =>
        {
            _elapsed = (DateTime.UtcNow - _started).TotalSeconds;
            InvalidateVisual();
            if (_elapsed >= 2) { _timer?.Stop(); _timer = null; }
        };
        _timer.Start();
        InvalidateVisual();
    }

    public void Stop()
    {
        _timer?.Stop(); _timer = null;
        _elapsed = 2;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double side = ActualWidth;
        if (side <= 0) return;
        double t = _elapsed;
        if (t < 0 || t >= 2) return;
        double s = side / Base;
        var ground = new Point(side / 2, side / 2 + 13 * s); // y 向下：脚点位于中心下方
        var tint = Kind.Tint();
        double alpha = Smooth(t / 0.08) * (1 - Smooth((t - 1.25) / 0.75));

        // --- 地面光斑（压扁的径向渐变椭圆）---
        double groundR = 48 * s;
        var groundBrush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = ground, GradientOrigin = ground, RadiusX = groundR, RadiusY = groundR,
        };
        groundBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(0.10 * 255), tint.R, tint.G, tint.B), 0));
        groundBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(0.32 * (1 - Smooth(t / 1.2)) * 255), tint.R, tint.G, tint.B), 0.6));
        groundBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, tint.R, tint.G, tint.B), 1));
        groundBrush.Freeze();
        dc.PushOpacity(alpha);
        dc.DrawGeometry(groundBrush, null,
            new EllipseGeometry(ground, groundR, groundR * 0.52));
        dc.Pop();

        // --- 两层错时扩散波 ---
        for (int i = 0; i < 2; i++)
        {
            double age = t - i * 0.24;
            if (age < 0 || age >= 1.15) continue;
            double progress = age / 1.15;
            double radius = (10 + 49 * (1 - Math.Pow(1 - progress, 2))) * s;
            double ringAlpha = Smooth(age / 0.045) * Math.Pow(1 - progress, 1.5);
            var ellipse = new EllipseGeometry(ground, radius, radius * 0.52);
            // 近似 Mac 的 4pt 阴影辉光：先画一条宽而淡的底衬
            dc.DrawGeometry(null, new Pen(
                new SolidColorBrush(Color.FromArgb((byte)(ringAlpha * 0.65 * 255), tint.R, tint.G, tint.B)),
                (i == 0 ? 1.9 : 1.1) * s * 2.5), ellipse);
            dc.DrawGeometry(null, new Pen(
                new SolidColorBrush(Color.FromArgb((byte)(ringAlpha * 0.88 * 255), tint.R, tint.G, tint.B)),
                (i == 0 ? 1.9 : 1.1) * s), ellipse);
        }

        // --- 短暂火花 ---
        if (t < 0.65)
        {
            double p = t / 0.65;
            var sparkBrush = new SolidColorBrush(Color.FromArgb((byte)((1 - p) * 0.7 * 255), tint.R, tint.G, tint.B));
            for (int i = 0; i < 5; i++)
            {
                double angle = i * 2 * Math.PI / 5 + Math.PI / 7;
                double dist = (10 + 31 * p) * s;
                double x = ground.X + Math.Cos(angle) * dist;
                double y = ground.Y + Math.Sin(angle) * dist * 0.6;
                dc.DrawGeometry(sparkBrush, null, new EllipseGeometry(new Point(x, y), s, s));
            }
        }

        // --- 图标：弹跳 pop + 升起 lift + 辉光双层 ---
        double pop;
        if (t < 0.12) pop = 0.50 + 0.58 * Smooth(t / 0.12);
        else if (t < 0.25) pop = 1.08 - 0.08 * Smooth((t - 0.12) / 0.13);
        else pop = 1;
        double lift = (9 * Smooth(t / 0.30) + 2 * Math.Sin(Math.Min(t, 1.4) * Math.PI)) * s;
        double iconSide = 43 * pop * s;
        var center = new Point(ground.X, ground.Y - 8 * s - lift - iconSide / 2);
        var rect = new Rect(center.X - iconSide / 2, center.Y - iconSide / 2, iconSide, iconSide);

        // 辉光近似：两级放大低透明度轮廓，模拟 Mac 的 6pt 阴影
        dc.PushOpacity(alpha * 0.40);
        foreach (double k in new[] { 1.28, 1.13 })
        {
            double gs = iconSide * k;
            var glowRect = new Rect(center.X - gs / 2, center.Y - gs / 2, gs, gs);
            var glow = VectorAssets.PathIn(Kind, glowRect);
            if (glow != null)
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)((k > 1.2 ? 0.35 : 0.6) * alpha * 0.40 * 255), tint.R, tint.G, tint.B)), null, glow);
        }
        dc.Pop();

        dc.PushOpacity(alpha);
        var body = VectorAssets.PathIn(Kind, rect);
        if (body != null) dc.DrawGeometry(new SolidColorBrush(tint), null, body);
        dc.Pop();
    }
}
