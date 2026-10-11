using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LoLPing.Core;

namespace LoLPing.Overlays;

/// <summary>
/// 透明、置顶、点击穿透、不抢焦点的悬浮窗基类。
/// 对应 Mac 版 OverlayPanel（borderless nonactivating panel + ignoresMouseEvents）。
/// </summary>
public class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
    }
}

/// <summary>关闭 90ms 淡出后销毁，对应 Mac hideWheel(animated:) 的收尾。</summary>
public sealed class WheelWindow : OverlayWindow
{
    public WheelView View { get; }

    public WheelWindow()
    {
        View = new WheelView();
        Content = View;
    }

    public void FadeAndClose()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(90));
        anim.Completed += (_, _) => { try { Close(); } catch { } };
        BeginAnimation(OpacityProperty, anim);
    }
}

/// <summary>落点特效窗：2.05 秒后自毁。</summary>
public sealed class PingEffectWindow : OverlayWindow
{
    public PingEffectView View { get; }

    public PingEffectWindow(PingKind kind)
    {
        View = new PingEffectView(kind);
        Content = View;
    }
}
