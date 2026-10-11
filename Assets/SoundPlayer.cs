using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;
using System.Windows.Media;
using LoLPing.Core;

namespace LoLPing.Assets;

/// <summary>
/// MP3 音效池。对应 Mac 版 SoundPlayer（AVAudioPlayer → WPF MediaPlayer）。
/// 同时最多 12 个，超出时停用最旧的，与 Mac 版一致。
/// </summary>
public sealed class SoundPlayer
{
    private readonly Dictionary<PingKind, Uri> _uris = new();
    private readonly List<MediaPlayer> _players = new();
    private readonly Dispatcher _dispatcher;
    public string? LastError { get; private set; }

    public SoundPlayer()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        foreach (PingKind kind in Enum.GetValues<PingKind>())
        {
            string file = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", kind.SoundName() + ".mp3");
            if (File.Exists(file)) _uris[kind] = new Uri(file);
        }
    }

    public void Play(PingKind kind, double volume)
    {
        if (volume <= 0) return;
        if (!_uris.TryGetValue(kind, out var uri)) { LastError = "无法读取音效：" + kind.Title(); return; }
        try
        {
            var player = new MediaPlayer();
            player.Volume = Math.Min(1.0, volume);
            player.MediaEnded += (_, _) => ClosePlayer(player);
            player.MediaFailed += (_, _) => ClosePlayer(player);
            player.Open(uri);
            player.Play();
            lock (_players)
            {
                if (_players.Count >= 12) { var oldest = _players[0]; _players.RemoveAt(0); SafeClose(oldest); }
                _players.Add(player);
            }
        }
        catch (Exception ex) { LastError = "音效播放失败: " + ex.Message; }
    }

    public void StopAll()
    {
        lock (_players)
        {
            foreach (var p in _players) SafeClose(p);
            _players.Clear();
        }
    }

    private void ClosePlayer(MediaPlayer player)
    {
        _dispatcher.BeginInvoke(() =>
        {
            lock (_players)
            {
                if (_players.Remove(player)) SafeClose(player);
            }
        });
    }

    private static void SafeClose(MediaPlayer player)
    {
        try { player.Stop(); player.Close(); } catch { /* 忽略 */ }
    }
}
