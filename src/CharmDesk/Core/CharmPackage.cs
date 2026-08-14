using CharmDesk.Core.Models;

namespace CharmDesk.Core;

/// <summary>A charm manifest resolved against its on-disk package directory.</summary>
public sealed class CharmPackage
{
    public CharmManifest Manifest { get; }
    public string Directory { get; }

    public CharmPackage(CharmManifest manifest, string directory)
    {
        Manifest = manifest;
        Directory = directory;
    }

    public string ImagePath => System.IO.Path.Combine(Directory, Manifest.Image);
    public string ThumbnailPath => System.IO.Path.Combine(Directory, Manifest.Thumbnail);
    public string ManifestPath => System.IO.Path.Combine(Directory, "manifest.json");
}
