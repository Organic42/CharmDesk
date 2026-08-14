using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using CharmDesk.Persistence;

namespace CharmDesk.Audio;

/// <summary>Plays CharmDesk's short UI chimes. Players are loaded once and reused - repeated
/// `new SoundPlayer(path).Play()` calls would each re-read the file and race their own disposal
/// against async playback; caching a loaded instance per sound avoids both problems.</summary>
public static class SoundEffects
{
    private static readonly string SoundsDir = Path.Combine(AppContext.BaseDirectory, "Sounds");
    private static readonly Dictionary<string, SoundPlayer?> Cache = new();

    public static void PlayPickup() => Play("pickup.wav");
    public static void PlayBounce() => Play("bounce.wav");
    public static void PlaySpin() => Play("spin.wav");

    private static void Play(string fileName)
    {
        try
        {
            if (!Cache.TryGetValue(fileName, out var player))
            {
                var path = Path.Combine(SoundsDir, fileName);
                player = File.Exists(path) ? new SoundPlayer(path) : null;
                player?.Load(); // synchronous, but these clips are a few hundred ms
                Cache[fileName] = player;
            }
            player?.Play();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            Logger.Log($"SoundEffects.Play ({fileName})", ex);
        }
    }
}
