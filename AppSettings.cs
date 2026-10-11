using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LoLPing.Core;

namespace LoLPing;

public sealed class AppSettings
{
    public bool Enabled { get; set; }
    /// <summary>组合键，例如 "Ctrl,Alt,Win"。</summary>
    public string Chord { get; set; } = "Ctrl,Alt,Win";
    /// <summary>附加触发键（可选）："V"、"Space"、"Tab" 等，空串表示无。</summary>
    public string TriggerKey { get; set; } = "";
    public double Volume { get; set; } = 0.55;
    public double Scale { get; set; } = 1.0;
    /// <summary>呼出延时（毫秒），0 = 瞬时。默认 60。</summary>
    public int TriggerDelayMs { get; set; } = 60;

    /// <summary>把触发键名解析为虚拟键码；无法识别返回 0。</summary>
    public static uint TriggerToVk(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        return key.Trim().ToLowerInvariant() switch
        {
            "space" => 0x20,
            "tab" => 0x09,
            { Length: 1 } c when c[0] >= 'a' && c[0] <= 'z' => (uint)(c[0] - 'a' + 0x41),
            { Length: 1 } d when d[0] >= '0' && d[0] <= '9' => (uint)(d[0] - '0' + 0x30),
            _ => 0,
        };
    }

    /// <summary>校验：≥2 个修饰键，或 ≥1 个修饰键 + 触发键。</summary>
    public static bool IsChordValid(Modifiers mods, uint triggerVk)
    {
        int count = 0;
        foreach (Modifiers f in new[] { Modifiers.Ctrl, Modifiers.Alt, Modifiers.Shift, Modifiers.Win })
            if (mods.HasFlag(f)) count++;
        return triggerVk != 0 ? count >= 1 : count >= 2;
    }

    [JsonIgnore]
    public Modifiers ChordModifiers
    {
        get
        {
            Modifiers m = Modifiers.None;
            foreach (var part in Chord.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Enum.TryParse<Modifiers>(part, true, out var parsed)
                    && parsed is not (Modifiers.None or Modifiers.Command or Modifiers.Extra)) m |= parsed;
            // 非法配置回退默认（合法性完整校验在 IsChordValid，由 UI 层负责）。
            if (m == Modifiers.None) m = Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win;
            return m;
        }
        set => Chord = string.Join(',', value.ToString().Split(','));
    }

    private static string Path_ =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LoLPing");
    private static string File_ => Path.Combine(Path_, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(File_))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(File_));
                if (s != null)
                {
                    s.Volume = Math.Clamp(s.Volume, 0, 1);
                    s.Scale = Math.Clamp(s.Scale, 0.75, 1.5);
                    s.TriggerDelayMs = Math.Clamp(s.TriggerDelayMs, 0, 300);
                    return s;
                }
            }
        }
        catch { /* 用默认值 */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path_);
            File.WriteAllText(File_, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 忽略写失败 */ }
    }
}
