using System.Windows.Media;

namespace LoLPing.Core;

/// <summary>九种信号，与 Mac 版 PingKind 一一对应。</summary>
public enum PingKind
{
    Retreat, Push, OnMyWay, AllIn, Assist, NeedVision, Missing, EnemyVision, Generic
}

public static class PingInfo
{
    /// <summary>轮盘八方向顺序（自北向南顺时针），对应 Mac 版 PingKind.wheel。</summary>
    public static readonly PingKind[] Wheel =
    {
        PingKind.Retreat, PingKind.Push, PingKind.OnMyWay, PingKind.AllIn,
        PingKind.Assist, PingKind.NeedVision, PingKind.Missing, PingKind.EnemyVision
    };

    public static string Title(this PingKind kind) => kind switch
    {
        PingKind.Retreat => "撤退",
        PingKind.Push => "推进",
        PingKind.OnMyWay => "正在路上",
        PingKind.AllIn => "全力进攻",
        PingKind.Assist => "请求协助",
        PingKind.NeedVision => "需要视野",
        PingKind.Missing => "敌人消失",
        PingKind.EnemyVision => "敌方视野",
        _ => "普通信号",
    };

    public static string SoundName(this PingKind kind) => kind switch
    {
        PingKind.Retreat => "retreat",
        PingKind.Push => "push",
        PingKind.OnMyWay => "on_my_way",
        PingKind.AllIn => "all_in",
        PingKind.Assist => "assist_me",
        PingKind.NeedVision => "need_vision",
        PingKind.Missing => "q_mark",
        PingKind.EnemyVision => "enemy_vision",
        _ => "alert",
    };

    /// <summary>JSON 矢量资源的键名（与 Mac 版 PingKind.rawValue 一致）。</summary>
    public static string VectorKey(this PingKind kind) => kind switch
    {
        PingKind.Retreat => "retreat",
        PingKind.Push => "push",
        PingKind.OnMyWay => "onMyWay",
        PingKind.AllIn => "allIn",
        PingKind.Assist => "assist",
        PingKind.NeedVision => "needVision",
        PingKind.Missing => "missing",
        PingKind.EnemyVision => "enemyVision",
        _ => "generic",
    };

    private static Color Srgb(double r, double g, double b) =>
        Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));

    /// <summary>主色，与 Mac 版 Assets.tint 相同。</summary>
    public static Color Tint(this PingKind kind) => kind switch
    {
        PingKind.Retreat or PingKind.EnemyVision => Srgb(1, 0.30, 0.30),
        PingKind.Missing or PingKind.AllIn => Srgb(1, 0.80, 0.02),
        PingKind.Assist or PingKind.Push or PingKind.NeedVision => Srgb(0, 0.89, 0.60),
        _ => Srgb(0.10, 0.72, 0.95), // onMyWay / generic
    };

    /// <summary>轮盘未选中时的颜色，与 Mac 版 wheelIdleTint 相同。</summary>
    public static Color WheelIdleTint(this PingKind kind) => kind switch
    {
        PingKind.Push or PingKind.AllIn or PingKind.NeedVision or PingKind.EnemyVision
            => Srgb(0.80, 0.76, 0.57),
        _ => kind.Tint(),
    };
}
