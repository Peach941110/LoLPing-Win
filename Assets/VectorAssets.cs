using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using LoLPing.Core;

namespace LoLPing.Assets;

/// <summary>
/// 加载 ping-vectors.json（与 Mac 版共用同一份矢量轮廓），
/// 建立每信号的 StreamGeometry。JSON 坐标为 0..1 归一化、y 向上（AppKit 约定），
/// 在 WPF（y 向下）中绘制时翻转 y。
/// </summary>
public static class VectorAssets
{
    private static readonly Dictionary<PingKind, Geometry> Cache = new();
    private static bool _loaded;

    private static string AssetRoot =>
        Path.Combine(AppContext.BaseDirectory, "Assets");

    public static Geometry? PathFor(PingKind kind)
    {
        if (!_loaded) Load();
        return Cache.TryGetValue(kind, out var g) ? g : null;
    }

    public static Geometry? PathIn(PingKind kind, Rect rect)
    {
        var baseGeo = PathFor(kind);
        if (baseGeo == null) return null;
        var clone = baseGeo.Clone();
        clone.Transform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(rect.Width, rect.Height, 0, 0),
                new TranslateTransform(rect.X, rect.Y),
            }
        };
        clone.Freeze();
        return clone;
    }

    public static bool HasAll()
    {
        if (!_loaded) Load();
        foreach (PingKind k in Enum.GetValues<PingKind>())
            if (!Cache.ContainsKey(k)) return false;
        return true;
    }

    private static void Load()
    {
        _loaded = true;
        string file = Path.Combine(AssetRoot, "Artwork", "ping-vectors.json");
        if (!File.Exists(file)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        foreach (PingKind kind in Enum.GetValues<PingKind>())
        {
            if (!doc.RootElement.TryGetProperty(kind.VectorKey(), out var entry)) continue;
            if (!entry.TryGetProperty("contours", out var contours)) continue;
            var geo = BuildGeometry(contours);
            if (geo != null) { geo.Freeze(); Cache[kind] = geo; }
        }
    }

    private static Geometry? BuildGeometry(JsonElement contours)
    {
        var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
        bool any = false;
        using (var ctx = geo.Open())
        {
        foreach (var contour in contours.EnumerateArray())
        {
            var pts = new List<Point>();
            foreach (var p in contour.EnumerateArray())
            {
                double x = p[0].GetDouble();
                double y = 1.0 - p[1].GetDouble(); // AppKit y 向上 → WPF y 向下
                pts.Add(new Point(x, y));
            }
            int n = pts.Count;
            if (n < 3) continue;
            any = true;

            // 与 Mac 版一致：每点前后各取 12% 混入相邻点，用二次贝塞尔平滑拐角。
            Point Entry(int i)
            {
                var p = pts[i]; var prev = pts[(i + n - 1) % n];
                return new Point(p.X * 0.88 + prev.X * 0.12, p.Y * 0.88 + prev.Y * 0.12);
            }
            Point Exit(int i)
            {
                var p = pts[i]; var next = pts[(i + 1) % n];
                return new Point(p.X * 0.88 + next.X * 0.12, p.Y * 0.88 + next.Y * 0.12);
            }

            ctx.BeginFigure(Entry(0), true, true);
            for (int i = 0; i < n; i++)
            {
                ctx.QuadraticBezierTo(pts[i], Exit(i), true, true);
                ctx.LineTo(Entry((i + 1) % n), true, true);
            }
        }
            ctx.Close();
        }
        return any ? geo : null;
    }
}
