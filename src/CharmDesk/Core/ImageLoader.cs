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
    /// short retries for a transient lock (e.g. antivirus scanning a freshly-written file).</summary>
    public static BitmapImage? TryLoad(string path, string context)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
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
}
