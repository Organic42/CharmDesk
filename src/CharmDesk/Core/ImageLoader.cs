using System;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using CharmDesk.Persistence;

namespace CharmDesk.Core;

/// <summary>
/// Loads a PNG into a frozen, ready-to-use <see cref="BitmapImage"/>.
///
/// Deliberately reads the file into a byte buffer and loads from a <see cref="MemoryStream"/>
/// rather than setting <c>BitmapImage.UriSource</c> directly. WPF's URI-based image loading path
/// goes through its own internal decoder cache keyed by the URI, which can surface spurious
/// <see cref="FileNotFoundException"/>s for a file that demonstrably exists and is readable -
/// this shows up in practice on charm images the Charm Manager can overwrite in place. Loading
/// from a stream sidesteps that cache entirely.
/// </summary>
public static class ImageLoader
{
    /// <summary>Returns null (and logs) if the file can't be loaded as an image after a few
    /// short retries for a transient lock (e.g. antivirus scanning a freshly-written file).
    ///
    /// <paramref name="decodePixelWidth"/> caps the decoded size for images that only ever render
    /// small (library thumbnails, the About page's decoration). A decoded bitmap costs
    /// width*height*4 bytes regardless of how small it's drawn, so a 957x660 "thumbnail" shown at
    /// 116px was costing ~2.5MB of the process's working set for no visible benefit.
    ///
    /// Leave it null for anything whose on-screen size is derived from the source image's own
    /// pixel dimensions - notably the live charm itself, where CharmWindow maps manifest
    /// clockFace coordinates through <c>BitmapImage.PixelWidth</c>. Shrinking the decode there
    /// would silently misplace the clock overlay.</summary>
    public static BitmapImage? TryLoad(string path, string context, int? decodePixelWidth = null)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes);

                // Clamp to the source's own width first. DecodePixelWidth larger than the source
                // upsamples into a *bigger* buffer than decoding normally would, and resamples
                // with a smoothing filter - which would both waste memory and visibly blur a
                // small hand-made pixel-art charm, the one thing NearestNeighbor is there to
                // avoid. DelayCreation reads just the header, not the pixels.
                var effectiveWidth = 0;
                if (decodePixelWidth is > 0)
                {
                    var probe = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    if (probe.Frames.Count > 0 && probe.Frames[0].PixelWidth > decodePixelWidth.Value)
                        effectiveWidth = decodePixelWidth.Value;
                    stream.Position = 0;
                }

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                if (effectiveWidth > 0) bmp.DecodePixelWidth = effectiveWidth;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                if (attempt == maxAttempts)
                {
                    Logger.Log($"ImageLoader.TryLoad ({context}: {path})", ex);
                    return null;
                }
                Thread.Sleep(60 * attempt);
            }
        }
        return null;
    }

    /// <summary>Same idea as <see cref="TryLoad"/>'s <c>decodePixelWidth</c>, but for the one
    /// caller (CharmWindow) that also needs the source image's true, undecoded pixel width for a
    /// separate calculation - a charm's <c>clockFace</c> region in manifest.json is authored in
    /// the source image's own pixel coordinates, and scaling that by whatever a *capped* decode
    /// happened to produce (rather than the real source size) would misplace the clock overlay.
    ///
    /// <paramref name="nativePixelWidth"/> is 0 if the file couldn't be read at all.</summary>
    public static BitmapImage? TryLoadForDisplay(
        string path, string context, int maxDecodePixelWidth, out int nativePixelWidth)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes);

                var probe = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var native = probe.Frames.Count > 0 ? probe.Frames[0].PixelWidth : 0;
                stream.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                if (native > maxDecodePixelWidth) bmp.DecodePixelWidth = maxDecodePixelWidth;
                bmp.StreamSource = stream;
                bmp.EndInit();
                bmp.Freeze();

                nativePixelWidth = native;
                return bmp;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                if (attempt == maxAttempts)
                {
                    Logger.Log($"ImageLoader.TryLoadForDisplay ({context}: {path})", ex);
                    nativePixelWidth = 0;
                    return null;
                }
                Thread.Sleep(60 * attempt);
            }
        }
        nativePixelWidth = 0;
        return null;
    }
}
