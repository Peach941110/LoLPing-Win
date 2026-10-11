using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LoLPing.Assets;
using LoLPing.Core;

namespace LoLPing.Ui;

/// <summary>
/// 设置主窗（代码构建 UI，海克斯科技风暗色主题）。
/// 固定尺寸、无边框自绘标题栏、胶囊开关、信号试用卡阵。
/// </summary>
public sealed class SettingsWindow : Window
{
    // ---- 主题色板（LoL 客户端海克斯配色）----
    static readonly Brush Bg = B("#12141A");
    static readonly Brush Card = B("#1B1E26");
    static readonly Brush CardHover = B("#232733");
    static readonly Brush Line = B("#2E3440");
    static readonly Brush Ink = B("#F0E6D2");
    static readonly Brush InkDim = B("#8B93A3");
    static readonly Brush Gold = B("#C8AA6E");
    static readonly Brush GoldSoft = B("#785A28");
    static readonly Brush Ok = B("#5AC97A");
    static readonly Brush Warn = B("#FFB04A");

    static SolidColorBrush B(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private readonly AppModel _model;
    private readonly ToggleButton _enableToggle;
    private readonly TextBlock _statusText;
    private readonly ToggleButton[] _chordToggles;
    private readonly ToggleButton[] _triggerToggles;
    private readonly Slider _volumeSlider;
    private readonly Slider _scaleSlider;
    private readonly TextBlock _hotkeyWarning;
    private System.Windows.Threading.DispatcherTimer? _armFeedbackTimer;
    private bool _suppress;

    public event Action? QuitRequested;

    /// <summary>附加触发键候选：显示名 → 键名。</summary>
    private static readonly (string Display, string Key)[] TriggerOptions =
    {
        ("无", ""),
        ("V", "V"), ("B", "B"), ("G", "G"), ("C", "C"), ("X", "X"), ("T", "T"),
        ("F", "F"), ("E", "E"), ("R", "R"), ("Q", "Q"), ("Z", "Z"),
        ("空格", "Space"), ("Tab", "Tab"),
    };

    public SettingsWindow(AppModel model)
    {
        _model = model;

        // ---- 窗口：固定尺寸 + 无边框 + 圆角投影 ----
        Width = 460;
        Height = 700;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Title = "LoLPing · 桌面信号";
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        TrySetAppIcon();

        var root = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = Bg,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.55, Color = Color.FromRgb(0, 0, 0) },
        };
        var dock = new DockPanel();

        // ---- 标题栏 ----
        var titleBar = new Grid { Height = 42, Background = B("#171A21") };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = LoadAppIcon();
        if (icon != null)
        {
            var img = new Image
            {
                Source = icon,
                Width = 18, Height = 18,
                Margin = new Thickness(14, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(img, 0);
            titleBar.Children.Add(img);
        }
        var titleText = new TextBlock
        {
            Text = "LoLPing · 桌面信号",
            Foreground = Ink,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        };
        Grid.SetColumn(titleText, 1);
        titleBar.Children.Add(titleText);

        var winBtns = new StackPanel { Orientation = Orientation.Horizontal };
        var minBtn = CaptionButton("—", () => WindowState = WindowState.Minimized);
        var closeBtn = CaptionButton("✕", Hide);
        winBtns.Children.Add(minBtn);
        winBtns.Children.Add(closeBtn);
        Grid.SetColumn(winBtns, 2);
        titleBar.Children.Add(winBtns);

        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            { try { DragMove(); } catch { } }
        };

        DockPanel.SetDock(titleBar, Dock.Top);
        dock.Children.Add(titleBar);

        // ---- 主体 ----
        var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

        // 启用开关（大号胶囊）
        var enableRow = new Grid { Margin = new Thickness(0, 2, 0, 10) };
        _enableToggle = Chip("启用 Ping", 110, 34, fontSize: 14);
        _enableToggle.IsChecked = _model.IsEnabled;
        _enableToggle.HorizontalAlignment = HorizontalAlignment.Left;
        _enableToggle.Checked += (_, _) => { if (!_suppress) _model.SetEnabled(true); };
        _enableToggle.Unchecked += (_, _) => { if (!_suppress) _model.SetEnabled(false); };
        enableRow.Children.Add(_enableToggle);
        body.Children.Add(enableRow);

        // 状态胶囊
        _statusText = new TextBlock
        {
            Text = _model.Message,
            Foreground = InkDim,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        };
        var statusPill = new Border
        {
            Background = Card,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 16),
            Child = _statusText,
        };
        body.Children.Add(statusPill);

        // 呼出组合键
        body.Children.Add(Header("呼出组合键", "按住约 0.18 秒唤出轮盘，松开发送"));
        var chordPanel = new WrapPanel { Margin = new Thickness(0, 2, 0, 14) };
        var chord = _model.Settings.ChordModifiers;
        (string, Modifiers)[] defs =
        {
            ("Ctrl", Modifiers.Ctrl), ("Alt", Modifiers.Alt), ("Shift", Modifiers.Shift), ("Win", Modifiers.Win),
        };
        _chordToggles = new ToggleButton[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            var (label, flag) = defs[i];
            var chip = Chip(label, 0, 32);
            chip.Tag = flag;
            chip.IsChecked = chord.HasFlag(flag);
            chip.Checked += (_, _) => ApplyChord();
            chip.Unchecked += (_, _) => ApplyChord();
            _chordToggles[i] = chip;
            chordPanel.Children.Add(chip);
        }
        body.Children.Add(chordPanel);

        // 附加触发键
        body.Children.Add(Header("附加触发键", "可选，如 Ctrl+Alt+V 长按唤出"));
        var triggerPanel = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        _triggerToggles = new ToggleButton[TriggerOptions.Length];
        for (int i = 0; i < TriggerOptions.Length; i++)
        {
            var (display, key) = TriggerOptions[i];
            var chip = Chip(display, 0, 30, fontSize: 12);
            chip.Tag = key;
            var capturedKey = key;
            chip.Checked += (_, _) => { if (!_suppress) ApplyTrigger(capturedKey); };
            chip.Unchecked += (_, _) => { if (!_suppress) SelectTrigger(_model.Settings.TriggerKey); };
            _triggerToggles[i] = chip;
            triggerPanel.Children.Add(chip);
        }
        SelectTrigger(_model.Settings.TriggerKey);
        body.Children.Add(triggerPanel);

        _hotkeyWarning = new TextBlock
        {
            Foreground = Warn,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 14),
            FontSize = 12,
        };
        body.Children.Add(_hotkeyWarning);
        RunHotkeyProbe();

        // 音量
        body.Children.Add(Header("音量", null));
        _volumeSlider = AccentSlider(0, 1, _model.Settings.Volume);
        _volumeSlider.LostMouseCapture += (_, _) => _model.SetVolume(_volumeSlider.Value);
        body.Children.Add(_volumeSlider);

        // 大小
        body.Children.Add(Header("大小（75% ~ 150%）", null));
        _scaleSlider = AccentSlider(0.75, 1.5, _model.Settings.Scale);
        _scaleSlider.TickFrequency = 0.25;
        _scaleSlider.LostMouseCapture += (_, _) => _model.SetScale(_scaleSlider.Value);
        body.Children.Add(_scaleSlider);

        // 信号试用
        body.Children.Add(Header("信号试用", "点击播放效果与语音，不受开关限制"));
        var grid = new WrapPanel { Margin = new Thickness(0, 2, 0, 14) };
        foreach (PingKind kind in Enum.GetValues<PingKind>())
        {
            var btn = new Button
            {
                Width = 98, Height = 88,
                Margin = new Thickness(0, 0, 8, 8),
                Style = CardButtonStyle(),
                Content = BuildPreviewCell(kind),
                Focusable = false,
            };
            var captured = kind;
            btn.Click += (_, _) => _model.Preview(captured);
            grid.Children.Add(btn);
        }
        body.Children.Add(grid);

        // 退出
        var exitBtn = new Button
        {
            Content = "退出软件",
            Padding = new Thickness(18, 7, 18, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = GhostDangerStyle(),
            Focusable = false,
        };
        exitBtn.Click += (_, _) => QuitRequested?.Invoke();
        body.Children.Add(exitBtn);

        var scroller = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        DockPanel.SetDock(scroller, Dock.Top);
        dock.Children.Add(scroller);
        root.Child = dock;
        Content = root;

        _model.OnStateChange += Refresh;
        _model.OnGestureArm += () => Dispatcher.Invoke(() =>
        {
            SetStatus("✔ 检测到组合键，轮盘已唤出", Ok);
            _armFeedbackTimer?.Stop();
            _armFeedbackTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _armFeedbackTimer.Tick += (_, _) =>
            {
                _armFeedbackTimer.Stop();
                SetStatus(_model.Message, InkDim);
            };
            _armFeedbackTimer.Start();
        });
    }

    // ================= 行为 =================

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    public void Quit() => QuitRequested?.Invoke();

    public void ShowFromTray()
    {
        try
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        catch (InvalidOperationException) { }
    }

    private void ApplyChord()
    {
        if (_suppress) return;
        Modifiers m = Modifiers.None;
        foreach (var t in _chordToggles)
            if (t.IsChecked == true) m |= (Modifiers)t.Tag!;

        uint triggerVk = AppSettings.TriggerToVk(_model.Settings.TriggerKey);
        if (!AppSettings.IsChordValid(m, triggerVk))
        {
            RollbackChordUi();
            ShowWarning("组合键无效：至少选 2 个修饰键，或 1 个修饰键 + 附加触发键。");
            return;
        }
        _model.SetChord(m);
        RunHotkeyProbe();
    }

    private void ApplyTrigger(string key)
    {
        if (_suppress) return;
        uint triggerVk = AppSettings.TriggerToVk(key);
        Modifiers m = Modifiers.None;
        foreach (var t in _chordToggles)
            if (t.IsChecked == true) m |= (Modifiers)t.Tag!;
        if (!AppSettings.IsChordValid(m, triggerVk))
        {
            SelectTrigger(_model.Settings.TriggerKey);
            ShowWarning(key == ""
                ? "取消触发键需要至少 2 个修饰键。"
                : "至少勾选 1 个修饰键再搭配触发键。");
            return;
        }
        _model.SetTriggerKey(key);
        SelectTrigger(key);
        RunHotkeyProbe();
    }

    private void RollbackChordUi()
    {
        _suppress = true;
        var chord = _model.Settings.ChordModifiers;
        foreach (var t in _chordToggles) t.IsChecked = chord.HasFlag((Modifiers)t.Tag!);
        _suppress = false;
    }

    private void SelectTrigger(string key)
    {
        _suppress = true;
        for (int i = 0; i < TriggerOptions.Length; i++)
            _triggerToggles[i].IsChecked = TriggerOptions[i].Key == (key ?? "");
        _suppress = false;
    }

    private void RunHotkeyProbe()
    {
        var warn = Platform.HotkeyProbe.Probe(_model.Settings.ChordModifiers, _model.TriggerVk);
        ShowWarning(warn);
    }

    private void ShowWarning(string? text)
    {
        _hotkeyWarning.Text = text ?? "✔ 未见冲突，可用。";
        _hotkeyWarning.Foreground = text == null ? Ok : Warn;
    }

    private void SetStatus(string text, Brush brush)
    {
        _statusText.Text = text;
        _statusText.Foreground = brush;
    }

    private void Refresh() => Dispatcher.Invoke(() =>
    {
        _suppress = true;
        _enableToggle.IsChecked = _model.IsEnabled;
        _suppress = false;
        SetStatus(_model.Message, InkDim);
    });

    /// <summary>标题栏图标：LoL 客户端官方图标。</summary>
    private void TrySetAppIcon()
    {
        try { var ic = LoadAppIcon(); if (ic != null) Icon = ic; } catch { }
    }

    /// <summary>应用图标：优先内嵌资源，回退外置 Assets 文件。</summary>
    private static ImageSource? LoadAppIcon()
    {
        using (var s = LoLPing.Assets.Embedded.Open("Assets/Artwork/app-icon.png"))
        {
            if (s != null)
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.StreamSource = s;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
        }
        var file = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Artwork", "app-icon.png");
        return System.IO.File.Exists(file) ? LoadBitmap(file) : null;
    }

    private static ImageSource LoadBitmap(string file)
    {
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(file);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // ================= 样式工厂 =================

    private static FrameworkElement Header(string title, string? hint)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Gold,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
        });
        if (hint != null)
            panel.Children.Add(new TextBlock
            {
                Text = hint,
                Foreground = InkDim,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            });
        return panel;
    }

    private static ToggleButton Chip(string label, double width, double height, double fontSize = 13)
    {
        var tb = new ToggleButton
        {
            Content = label,
            FontSize = fontSize,
            Foreground = Ink,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
        };
        if (width > 0) tb.Width = width;
        if (height > 0) tb.Height = height;
        tb.Margin = new Thickness(0, 0, 8, 8);
        tb.Template = ChipTemplate();
        return tb;
    }

    private static ControlTemplate ChipTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        cp.SetValue(ContentPresenter.MarginProperty, new Thickness(12, 2, 12, 2));
        border.AppendChild(cp);

        var tpl = new ControlTemplate(typeof(ToggleButton)) { VisualTree = border };

        var normal = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = false };
        normal.Setters.Add(new Setter(BackgroundProperty, Card));
        normal.Setters.Add(new Setter(BorderBrushProperty, Line));
        normal.Setters.Add(new Setter(ForegroundProperty, Ink));
        tpl.Triggers.Add(normal);

        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(BackgroundProperty, Gold));
        on.Setters.Add(new Setter(BorderBrushProperty, Gold));
        on.Setters.Add(new Setter(ForegroundProperty, B("#14161C")));
        tpl.Triggers.Add(on);

        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BorderBrushProperty, GoldSoft));
        tpl.Triggers.Add(hover);
        return tpl;
    }

    private static Style CardButtonStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);

        var style = new Style(typeof(Button));
        var tpl = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, CardHover));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, GoldSoft));
        tpl.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, B("#0F1116")));
        tpl.Triggers.Add(pressed);

        style.Setters.Add(new Setter(TemplateProperty, tpl));
        style.Setters.Add(new Setter(BackgroundProperty, Card));
        style.Setters.Add(new Setter(BorderBrushProperty, Line));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));
        return style;
    }

    private static Style GhostDangerStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);

        var tpl = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, B("#3A2224")));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, B("#6E3A3E")));
        tpl.Triggers.Add(hover);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(TemplateProperty, tpl));
        style.Setters.Add(new Setter(BackgroundProperty, Card));
        style.Setters.Add(new Setter(BorderBrushProperty, Line));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(ForegroundProperty, B("#C97A7A")));
        style.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));
        return style;
    }

    private static Button CaptionButton(string glyph, Action onClick)
    {
        var btn = new Button
        {
            Content = glyph,
            Width = 40, Height = 42,
            Foreground = InkDim,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Focusable = false,
        };
        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        tpl.VisualTree = bd;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BackgroundProperty, B("#2A2E38")));
        hover.Setters.Add(new Setter(ForegroundProperty, Ink));
        tpl.Triggers.Add(hover);
        btn.Template = tpl;
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static Slider AccentSlider(double min, double max, double val) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = val,
        Margin = new Thickness(0, 2, 0, 14),
        IsMoveToPointEnabled = true,
        Foreground = Gold,
    };

    private static FrameworkElement BuildPreviewCell(PingKind kind)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var geo = VectorAssets.PathIn(kind, new Rect(0, 0, 30, 30));
        if (geo != null)
        {
            var img = new System.Windows.Shapes.Path
            {
                Data = geo,
                Fill = new SolidColorBrush(kind.Tint()),
                Width = 30, Height = 30,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 4),
            };
            panel.Children.Add(img);
        }
        panel.Children.Add(new TextBlock
        {
            Text = kind.Title(),
            FontSize = 11,
            Foreground = InkDim,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        return panel;
    }

    private static Brush Bg2 => Bg; // 保留备用
}
