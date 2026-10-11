using System;
using System.IO;
using System.Windows;

namespace LoLPing.Assets;

/// <summary>
/// 内嵌资源读取器。音效/矢量/图标以 WPF Resource 编进程序集，
/// 支持真正的单文件 exe；旧的分发方式（Assets 文件夹随 exe 摆放）仍作为回退。
/// </summary>
public static class Embedded
{
    /// <summary>打开内嵌资源（相对工程根路径，如 "Assets/Sounds/alert.mp3"），不存在返回 null。</summary>
    public static Stream? Open(string relative)
    {
        try
        {
            return Application.GetResourceStream(PackUri(relative))?.Stream;
        }
        catch { return null; }
    }

    public static bool Exists(string relative)
    {
        using var s = Open(relative);
        return s != null;
    }

    /// <summary>内嵌资源的 pack URI（供 WPF 图像等资源使用）。</summary>
    public static Uri PackUri(string relative) =>
        new Uri("pack://application:,,,/" + relative.Replace('\\', '/'), UriKind.Absolute);

    /// <summary>
    /// 将内嵌资源解出到 %APPDATA%\LoLPing\{subDir}\ 并返回文件路径（MediaPlayer 对 pack URI
    /// 支持不稳，音效需先落地为文件播放）。大小相同则跳过重复写出。失败返回 null。
    /// </summary>
    public static string? ExtractToFile(string relative, string subDir)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LoLPing", subDir);
            string dest = Path.Combine(dir, Path.GetFileName(relative));
            using var s = Open(relative);
            if (s == null) return null;
            if (File.Exists(dest) && new FileInfo(dest).Length == s.Length) return dest;
            Directory.CreateDirectory(dir);
            using var fs = File.Create(dest + ".tmp");
            s.CopyTo(fs);
            fs.Close();
            File.Move(dest + ".tmp", dest, true);
            return dest;
        }
        catch { return null; }
    }
}
