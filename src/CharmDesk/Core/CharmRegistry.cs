using System;
using System.IO;
using System.IO.Compression;
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

    /// <summary>Copies any bundled charm not already present in the user's library, matched by
    /// folder name. Runs on every launch rather than only the first: a charm already installed
    /// - including one the user has since edited via the Charm Manager - is left untouched, but
    /// a charm added in a later app update still reaches existing installs instead of only ever
    /// landing on a totally fresh one. <paramref name="excludeIds"/> lets a caller keep charms
    /// the user has explicitly deleted from coming back - folder absence alone can't tell "never
    /// installed" apart from "deliberately removed". Best-effort: a sync failure shouldn't block
    /// the app from starting.</summary>
    public void SyncBundledCharms(string bundledCharmsDirectory, IReadOnlyCollection<string>? excludeIds = null)
    {
        try
        {
            if (!System.IO.Directory.Exists(bundledCharmsDirectory))
                return;

            var existingIds = new HashSet<string>(
                System.IO.Directory.EnumerateDirectories(CharmsDirectory).Select(d => Path.GetFileName(d)!),
                StringComparer.OrdinalIgnoreCase);
            if (excludeIds is not null)
                existingIds.UnionWith(excludeIds);

            foreach (var srcDir in System.IO.Directory.EnumerateDirectories(bundledCharmsDirectory))
            {
                var id = Path.GetFileName(srcDir)!;
                if (existingIds.Contains(id))
                    continue;

                var destDir = Path.Combine(CharmsDirectory, id);
                CopyDirectory(srcDir, destDir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log("CharmRegistry.SyncBundledCharms", ex);
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

    /// <summary>Zips a charm's manifest + assets into a flat, shareable pack - no folder
    /// structure inside, just manifest.json + the image files at the archive root.</summary>
    public void ExportPack(CharmPackage package, string destZipPath)
    {
        if (File.Exists(destZipPath)) File.Delete(destZipPath);
        using var archive = ZipFile.Open(destZipPath, ZipArchiveMode.Create);
        foreach (var file in System.IO.Directory.GetFiles(package.Directory))
            archive.CreateEntryFromFile(file, Path.GetFileName(file));
    }

    /// <summary>Imports a charm pack (a .zip produced by <see cref="ExportPack"/>, or hand-built
    /// the same way: manifest.json + charm.png [+ thumbnail.png] at the archive root). Throws
    /// <see cref="InvalidDataException"/> with a user-facing message if the pack looks wrong.</summary>
    public CharmPackage ImportPack(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("This doesn't look like a charm pack - no manifest.json inside.");

        CharmManifest manifest;
        using (var stream = manifestEntry.Open())
        using (var reader = new StreamReader(stream))
        {
            manifest = JsonSerializer.Deserialize<CharmManifest>(reader.ReadToEnd(), JsonOptions)
                ?? throw new InvalidDataException("This charm pack's manifest.json couldn't be read.");
        }

        if (archive.GetEntry(manifest.Image) is null)
            throw new InvalidDataException($"This charm pack is missing its image ({manifest.Image}).");

        manifest.Id = UniqueId(string.IsNullOrWhiteSpace(manifest.Id) ? "charm" : manifest.Id);
        var dir = Path.Combine(CharmsDirectory, manifest.Id);
        System.IO.Directory.CreateDirectory(dir);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // skip directory entries
            entry.ExtractToFile(Path.Combine(dir, entry.Name), overwrite: true);
        }

        var package = new CharmPackage(manifest, dir);
        Save(package); // re-write manifest.json in case the id above got deduplicated
        return package;
    }

    /// <summary>Returns <paramref name="preferred"/> if it's free, otherwise the first
    /// "preferred-2", "preferred-3", ... that doesn't already exist.</summary>
    private string UniqueId(string preferred)
    {
        if (!System.IO.Directory.Exists(Path.Combine(CharmsDirectory, preferred)))
            return preferred;
        var n = 1;
        string candidate;
        do { candidate = $"{preferred}-{++n}"; }
        while (System.IO.Directory.Exists(Path.Combine(CharmsDirectory, candidate)));
        return candidate;
    }
}
