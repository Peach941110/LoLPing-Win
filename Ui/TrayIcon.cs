using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LoLPing.Assets;
using LoLPing.Core;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace LoLPing.Ui;

/// <summary>托盘图标（对应 Mac 菜单栏 NSStatusItem）。右键菜单：打开/启用/退出。</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly AppModel _model;
    private readonly WinForms.ToolStripMenuItem _enableItem;
    private Drawing.Icon? _hicon;

    public TrayIcon(AppModel model, SettingsWindow window)
    {
        _model = model;
        _icon = new WinForms.NotifyIcon
        {
            Text = "LoLPing · 桌面信号",
            Icon = LoadIcon(),
            Visible = true,
        };

        var menu = new WinForms.ContextMenuStrip();
        var openItem = new WinForms.ToolStripMenuItem("打开 LoLPing");
        openItem.Click += (_, _) => window.ShowFromTray();
        _enableItem = new WinForms.ToolStripMenuItem("启用 Ping") { CheckOnClick = false };
        _enableItem.Click += (_, _) => _model.SetEnabled(!_model.IsEnabled);
        var exitItem = new WinForms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => window.Quit();
        menu.Items.AddRange(new WinForms.ToolStripItem[] { openItem, _enableItem, new WinForms.ToolStripSeparator(), exitItem });
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => window.ShowFromTray();

        _model.OnStateChange += () =>
        {
            try { _enableItem.Checked = _model.IsEnabled; } catch { }
        };
        _enableItem.Checked = _model.IsEnabled;
    }

    private Drawing.Icon LoadIcon()
    {
        // 1) 首选：内嵌的官方图标（从英雄联盟客户端提取）；回退外置文件
        try
        {
            using var s = LoLPing.Assets.Embedded.Open("Assets/app-icon.ico");
            if (s != null) return new Drawing.Icon(s);
        }
        catch { /* 继续回退 */ }
        try
        {
            var icoFile = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
            if (System.IO.File.Exists(icoFile)) return new Drawing.Icon(icoFile);
        }
        catch { /* 继续回退 */ }

        // 2) 回退：用蓝色 ping 矢量自绘 32px
        try
        {
            var geo = VectorAssets.PathIn(PingKind.Generic, new Rect(2, 2, 28, 28));
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1c)), null,
                    new Rect(0, 0, 32, 32), 7, 7);
                if (geo != null)
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x1a, 0xb8, 0xf2)), null, geo);
            }
            var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            var bmp = new Drawing.Bitmap(ms);
            _hicon = Drawing.Icon.FromHandle(bmp.GetHicon());
            return _hicon;
        }
        catch
        {
            return Drawing.SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _hicon?.Dispose();
    }
}
