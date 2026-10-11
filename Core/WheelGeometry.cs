using System;

namespace LoLPing.Core;

/// <summary>
/// 轮盘几何。移植自 Mac 版 WheelGeometry。
/// 注意：Mac 用 AppKit 坐标（y 向上），Windows 全部使用屏幕物理像素（y 向下）。
/// “自北顺时针角度”统一为 atan2(dx, -dy)，北=0，东=π/2。
/// </summary>
public static class WheelGeometry
{
    public const double CenterRadius = 66;   // 中心圆半径（设计单位，300 基数）
    public const double IconRadius = 101;    // 图标所在半径
    public const double OuterRadius = 140;   // 外圈半径

    private static double Hyp(double x, double y) => Math.Sqrt(x * x + y * y);

    /// <summary>指针相对轮盘中心的顺时针方位角；位于中心普通信号区时返回 null。</summary>
    public static double? Direction(double px, double py, double cx, double cy, double deadZone)
    {
        double dx = px - cx, dy = py - cy;
        if (Hyp(dx, dy) <= deadZone) return null;
        return Math.Atan2(dx, -dy);
    }

    /// <summary>指针所在的信号；死区内为普通信号。</summary>
    public static PingKind Selection(double px, double py, double cx, double cy, double deadZone)
    {
        double dx = px - cx, dy = py - cy;
        if (Hyp(dx, dy) <= deadZone) return PingKind.Generic;
        double clockwiseFromNorth = Math.Atan2(dx, -dy);
        int index = (int)Math.Floor((clockwiseFromNorth + Math.PI / 8) / (Math.PI / 4));
        return PingInfo.Wheel[((index % 8) + 8) % 8];
    }

    /// <summary>把轮盘中心夹到工作区内（物理像素）。</summary>
    public static void ClampCenter(ref double x, ref double y,
        double workLeft, double workTop, double workRight, double workBottom, double radius)
    {
        double Clamp(double v, double lo, double hi) => lo > hi ? (lo + hi) / 2 : Math.Min(Math.Max(v, lo), hi);
        x = Clamp(x, workLeft + radius, workRight - radius);
        y = Clamp(y, workTop + radius, workBottom - radius);
    }
}
