using System;
using System.IO;
using System.Text.Json;
using CharmDesk.Core.Models;
using CharmDesk.Persistence;

namespace CharmDesk.Core;

/// <summary>
/// Discovers charm packages from the user's local data directory (one folder per charm,
/// each holding a manifest.json + its own assets). No charm is ever hardcoded into the
/// engine: dropping a new folder with a valid manifest.json here is enough to load it,
/// no recompilation required.
/// </summary>
public sealed class CharmRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string CharmsDirectory { get; }

    public CharmRegistry(string charmsDirectory)
    {
        CharmsDirectory = charmsDirectory;
        System.IO.Directory.CreateDirectory(CharmsDirectory);
    }

    /// <summary>Copies bundled default charm packages into the user data dir on first run only.
    /// Best-effort: a seeding failure shouldn't block the app from starting (the library will
    /// just come up empty and the user can still import charms manually).</summary>
    public void SeedFromBundledIfEmpty(string bundledCharmsDirectory)
    {
        try
        {
            if (!System.IO.Directory.Exists(bundledCharmsDirectory))
                return;

            if (System.IO.Directory.EnumerateDirectories(CharmsDirectory).Any())
                return;

            foreach (var srcDir in System.IO.Directory.EnumerateDirectories(bundledCharmsDirectory))
            {
                var destDir = Path.Combine(CharmsDirectory, Path.GetFileName(srcDir));
                CopyDirectory(srcDir, destDir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log("CharmRegistry.SeedFromBundledIfEmpty", ex);
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        System.IO.Directory.CreateDirectory(destDir);
        foreach (var file in System.IO.Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in System.IO.Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    public List<CharmPackage> LoadAll()
    {
        var result = new List<CharmPackage>();
        foreach (var dir in System.IO.Directory.EnumerateDirectories(CharmsDirectory))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<CharmManifest>(json, JsonOptions);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                    continue;

                manifest.SourceDirectory = dir;
                result.Add(new CharmPackage(manifest, dir));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Skip malformed or unreadable manifests rather than crashing the whole registry.
                Logger.Log($"CharmRegistry.LoadAll ({dir})", ex);
            }
        }
        return result.OrderBy(p => p.Manifest.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public CharmPackage? Find(string id) =>
        LoadAll().FirstOrDefault(p => string.Equals(p.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));

    public void Save(CharmPackage package)
    {
        var json = JsonSerializer.Serialize(package.Manifest, JsonOptions);
        File.WriteAllText(package.ManifestPath, json);
    }

    /// <summary>Creates a new charm package folder, copying in the supplied source image files.</summary>
    public CharmPackage Import(CharmManifest manifest, string sourceImagePath, string? sourceThumbnailPath)
    {
        var id = string.IsNullOrWhiteSpace(manifest.Id) ? Guid.NewGuid().ToString("N")[..8] : manifest.Id;
        manifest.Id = id;

        var dir = Path.Combine(CharmsDirectory, id);
        System.IO.Directory.CreateDirectory(dir);

        var imageExt = Path.GetExtension(sourceImagePath);
        manifest.Image = "charm" + imageExt;
        File.Copy(sourceImagePath, Path.Combine(dir, manifest.Image), overwrite: true);

        if (!string.IsNullOrWhiteSpace(sourceThumbnailPath) && File.Exists(sourceThumbnailPath))
        {
            var thumbExt = Path.GetExtension(sourceThumbnailPath);
            manifest.Thumbnail = "thumbnail" + thumbExt;
            File.Copy(sourceThumbnailPath, Path.Combine(dir, manifest.Thumbnail), overwrite: true);
        }
        else
        {
            // No dedicated thumbnail supplied: reuse the main image as its own thumbnail.
            manifest.Thumbnail = manifest.Image;
        }

        var package = new CharmPackage(manifest, dir);
        Save(package);
        return package;
    }

    public void Delete(CharmPackage package)
    {
        if (System.IO.Directory.Exists(package.Directory))
            System.IO.Directory.Delete(package.Directory, recursive: true);
    }
}
