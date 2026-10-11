using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LoLPing.Assets;
using LoLPing.Core;

namespace LoLPing.Overlays;

/// <summary>
/// 轮盘绘制。逐段移植自 Mac 版 WheelView.draw()。
/// 坐标约定：WPF y 向下；φ = 自北顺时针弧度；点(φ,r) = (cx+sin·r, cy−cos·r)。
/// </summary>
public sealed class WheelView : FrameworkElement
{
    private const double Base = 300; // 设计基数：几何常量均按 300 方框定义

    private static readonly Color Gold = Color.FromRgb(199, 168, 102);
    private static readonly Color Cyan = Color.FromRgb(5, 207, 196);

    public PingKind Selected { get; private set; } = PingKind.Generic;
    public double? Direction { get; private set; }

    private readonly double[] _weights = new double[8];
    private readonly double[] _origins = new double[8];
    private DateTime _transitionStart = DateTime.MinValue;
    private DateTime? _openingStart;
    private DispatcherTimer? _timer;

    private static double Clamp01(double v) => Math.Min(1, Math.Max(0, v));
    private static double Ease(double v) { double t = Clamp01(v); return t * t * (3 - 2 * t); }
    private static Color Mix(Color a, Color b, double w)
    {
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * Clamp01(w));
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    private double S => ActualWidth / Base;

    public WheelView()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
    }

    public void Open()
    {
        _openingStart = DateTime.UtcNow;
        StartTimer();
        InvalidateVisual();
    }

    public void Select(PingKind kind, double? angle)
    {
        Direction = kind == PingKind.Generic ? null : angle;
        if (kind != Selected)
        {
            for (int i = 0; i < 8; i++) _origins[i] = _weights[i];
            Selected = kind;
            _transitionStart = DateTime.UtcNow;
            StartTimer();
        }
        InvalidateVisual();
    }

    public void Stop()
    {
        _timer?.Stop(); _timer = null;
    }

    private void StartTimer()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            UpdateAnimation(now);
            InvalidateVisual();
            if ((now - _transitionStart).TotalSeconds >= 0.12 &&
                (_openingStart == null || (now - _openingStart.Value).TotalSeconds >= 0.09))
            {
                Stop(); _openingStart = null;
            }
        };
        _timer.Start();
    }

    private void UpdateAnimation(DateTime now)
    {
        if (_transitionStart == DateTime.MinValue) return;
        double p = Ease((now - _transitionStart).TotalSeconds / 0.12);
        for (int i = 0; i < 8; i++)
        {
            double target = PingInfo.Wheel[i] == Selected ? 1 : 0;
            _weights[i] = _origins[i] + (target - _origins[i]) * p;
        }
    }

    /// <summary>角度 φ（自北顺时针）下半径 r 的点。</summary>
    private static Point Polar(double cx, double cy, double phi, double r) =>
        new(cx + Math.Sin(phi) * r, cy - Math.Cos(phi) * r);

    protected override void OnRender(DrawingContext dc)
    {
        double side = ActualWidth;
        if (side <= 0) return;
        double s = side / Base;
        var c = new Point(side / 2, side / 2);
        double outer = WheelGeometry.OuterRadius * s;
        double inner = WheelGeometry.CenterRadius * s;

        double openAlpha = 1;
        if (_openingStart != null) openAlpha = Ease((DateTime.UtcNow - _openingStart.Value).TotalSeconds / 0.09);

        dc.PushOpacity(openAlpha);

        for (int i = 0; i < 8; i++)
        {
            var kind = PingInfo.Wheel[i];
            double phi = i * Math.PI / 4;
            double a0 = phi - Math.PI / 8, a1 = phi + Math.PI / 8;

            // --- 扇区：暗色径向底 + 悬停青色辉光（按权重）---
            var sector = SectorGeometry(c, inner, outer, a0, a1);
            dc.PushClip(sector);
            dc.DrawGeometry(SectorBaseBrush(c, inner, outer), null, sector);
            double w = _weights[i];
            if (w > 0.001)
                dc.DrawGeometry(SectorGlowBrush(c, inner, outer, w), null, sector);
            dc.Pop();

            // --- 分隔线（扇区起始边界）---
            var pIn = Polar(c.X, c.Y, a0, inner);
            var pOut = Polar(c.X, c.Y, a0, outer);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)(0.30 * 255), Gold.R, Gold.G, Gold.B)), 0.55 * s), pIn, pOut);

            // --- 图标：22 → 30 随权重放大，颜色从 idle 色混向主色 ---
            var p = Polar(c.X, c.Y, phi, WheelGeometry.IconRadius * s);
            double iconSide = (22 + 8 * w) * s;
            var color = Mix(kind.WheelIdleTint(), kind.Tint(), w);
            var rect = new Rect(p.X - iconSide / 2, p.Y - iconSide / 2, iconSide, iconSide);
            var geo = VectorAssets.PathIn(kind, rect);
            if (geo != null) dc.DrawGeometry(new SolidColorBrush(color), null, geo);
        }

        // --- 中心圆 ---
        var coreBrush = new SolidColorBrush(Color.FromArgb((byte)(0.94 * 255), 7, 7, 7));
        dc.DrawGeometry(coreBrush, null, new EllipseGeometry(c, inner, inner));
        if (Selected == PingKind.Generic)
        {
            dc.PushClip(new EllipseGeometry(c, inner, inner));
            var glow = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = c, GradientOrigin = c, RadiusX = inner, RadiusY = inner,
            };
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, Cyan.R, Cyan.G, Cyan.B), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(0.19 * 255), Cyan.R, Cyan.G, Cyan.B), 1));
            glow.Freeze();
            dc.DrawGeometry(glow, null, new EllipseGeometry(c, inner, inner));
            dc.Pop();
        }
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(0.70 * 255), Gold.R, Gold.G, Gold.B)), 1.2 * s),
            new EllipseGeometry(c, inner, inner));
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Gold), 1.4 * s),
            new EllipseGeometry(c, inner - 3 * s, inner - 3 * s));

        // --- 指针：金色尖角 + 青色短弧 ---
        if (Direction is double dir)
        {
            var tipL = Polar(c.X, c.Y, dir + 0.06, inner);
            var tip = Polar(c.X, c.Y, dir, inner + 6 * s);
            var tipR = Polar(c.X, c.Y, dir - 0.06, inner);
            var notch = new StreamGeometry();
            using (var g = notch.Open())
            {
                g.BeginFigure(tipL, false, true);
                g.LineTo(tip, true, true);
                g.LineTo(tipR, true, true);
            }
            notch.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Gold), 1.7 * s) { LineJoin = PenLineJoin.Miter }, notch);

            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                const int seg = 16;
                bool first = true;
                for (int k = 0; k <= seg; k++)
                {
                    double phi = dir - Math.PI / 10 + (Math.PI / 5) * k / seg;
                    var pt = Polar(c.X, c.Y, phi, inner - 7 * s);
                    if (first) { g.BeginFigure(pt, false, false); first = false; }
                    else g.LineTo(pt, true, true);
                }
            }
            arc.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Cyan), 1.8 * s), arc);
        }

        // --- 中心文字 ---
        if (Selected == PingKind.Generic)
        {
            var pingGeo = VectorAssets.PathIn(PingKind.Generic,
                new Rect(c.X - 11 * s, c.Y - 40 * s, 22 * s, 22 * s));
            if (pingGeo != null)
                dc.DrawGeometry(new SolidColorBrush(PingKind.Generic.Tint()), null, pingGeo);
            DrawText(dc, "信号", c.X, c.Y, 13 * s, PingKind.Generic.Tint(), true);
        }
        else
        {
            DrawText(dc, Selected.Title(), c.X, c.Y, 13 * s, Selected.Tint(), true);
        }
        DrawText(dc, "右键取消", c.X, c.Y + 31 * s, 8.5 * s, Color.FromArgb((byte)(0.9 * 255), 133, 133, 133), false);

        dc.Pop();
    }

    private static Geometry SectorGeometry(Point c, double inner, double outer, double a0, double a1)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(Polar(c.X, c.Y, a0, inner), true, true);
            g.LineTo(Polar(c.X, c.Y, a0, outer), true, true);
            g.ArcTo(Polar(c.X, c.Y, a1, outer), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true, true);
            g.LineTo(Polar(c.X, c.Y, a1, inner), true, true);
            g.ArcTo(Polar(c.X, c.Y, a0, inner), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true, true);
        }
        geo.Freeze();
        return geo;
    }

    private static RadialGradientBrush SectorBaseBrush(Point c, double inner, double outer)
    {
        var b = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = c, GradientOrigin = c, RadiusX = outer, RadiusY = outer,
        };
        byte g7 = (byte)Math.Round(0.035 * 255), g6 = (byte)Math.Round(0.025 * 255);
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(0.60 * 255), g7, g7, g7), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(0.60 * 255), g7, g7, g7), inner / outer));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, g6, g6, g6), 1));
        b.Freeze();
        return b;
    }

    private static RadialGradientBrush SectorGlowBrush(Point c, double inner, double outer, double w)
    {
        var b = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = c, GradientOrigin = c, RadiusX = outer, RadiusY = outer,
        };
        byte a1 = (byte)Math.Round(0.85 * w * 255), a2 = (byte)Math.Round(0.32 * w * 255);
        double f = inner / outer;
        b.GradientStops.Add(new GradientStop(Color.FromArgb(a1, Cyan.R, Cyan.G, Cyan.B), f));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(a2, Cyan.R, Cyan.G, Cyan.B), f + (1 - f) * 0.6));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, Cyan.R, Cyan.G, Cyan.B), 1));
        b.Freeze();
        return b;
    }

    private void DrawText(DrawingContext dc, string text, double cx, double cy, double size, Color color, bool semibold)
    {
        var tf = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal,
            semibold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        double ppd = IsInitialized && PresentationSource.FromVisual(this) != null
            ? VisualTreeHelper.GetDpi(this).PixelsPerDip : 1.0;
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, tf, size, new SolidColorBrush(color), ppd);
        dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }
}
