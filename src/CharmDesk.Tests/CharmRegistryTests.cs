using System.IO.Compression;
using CharmDesk.Core;
using CharmDesk.Core.Models;

namespace CharmDesk.Tests;

/// <summary>Each test gets its own throwaway charms directory under the OS temp folder,
/// deleted afterward - CharmRegistry works entirely off a directory path, so no mocking
/// is needed to exercise the real file I/O.</summary>
public sealed class CharmRegistryTests : IDisposable
{
    private readonly string _charmsDir;
    private readonly CharmRegistry _registry;

    public CharmRegistryTests()
    {
        _charmsDir = Path.Combine(Path.GetTempPath(), "CharmDeskTests", Guid.NewGuid().ToString("N"));
        _registry = new CharmRegistry(_charmsDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_charmsDir))
            Directory.Delete(_charmsDir, recursive: true);
    }

    private static string WriteDummyPng(string dir, string fileName = "source.png")
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]); // just needs to exist; registry never decodes it
        return path;
    }

    [Fact]
    public void LoadAll_EmptyDirectory_ReturnsEmptyList()
    {
        Assert.Empty(_registry.LoadAll());
    }

    [Fact]
    public void Import_CreatesLoadablePackageWithCopiedImage()
    {
        var sourceDir = Path.Combine(_charmsDir, "_src");
        var sourcePng = WriteDummyPng(sourceDir);

        var manifest = new CharmManifest { Id = "test-charm", Name = "Test Charm" };
        var package = _registry.Import(manifest, sourcePng, sourceThumbnailPath: null);

        Assert.True(File.Exists(package.ImagePath));
        Assert.True(File.Exists(package.ManifestPath));

        var loaded = _registry.Find("test-charm");
        Assert.NotNull(loaded);
        Assert.Equal("Test Charm", loaded!.Manifest.Name);
        // No dedicated thumbnail supplied - the main image is reused as its own thumbnail.
        Assert.Equal(loaded.Manifest.Image, loaded.Manifest.Thumbnail);
    }

    [Fact]
    public void Delete_RemovesPackageDirectory()
    {
        var sourcePng = WriteDummyPng(Path.Combine(_charmsDir, "_src"));
        var package = _registry.Import(new CharmManifest { Id = "to-delete", Name = "Gone Soon" }, sourcePng, null);

        Assert.True(Directory.Exists(package.Directory));
        _registry.Delete(package);
        Assert.False(Directory.Exists(package.Directory));
        Assert.Null(_registry.Find("to-delete"));
    }

    [Fact]
    public void ExportThenImportPack_RoundTripsManifest()
    {
        var sourcePng = WriteDummyPng(Path.Combine(_charmsDir, "_src"));
        var original = _registry.Import(
            new CharmManifest { Id = "roundtrip", Name = "Round Trip", Category = "Test", DisplayScale = 1.4 },
            sourcePng, null);

        var zipPath = Path.Combine(_charmsDir, "export.zip");
        _registry.ExportPack(original, zipPath);
        _registry.Delete(original);
        Assert.Null(_registry.Find("roundtrip"));

        var reimported = _registry.ImportPack(zipPath);

        Assert.Equal("roundtrip", reimported.Manifest.Id);
        Assert.Equal("Round Trip", reimported.Manifest.Name);
        Assert.Equal(1.4, reimported.Manifest.DisplayScale);
        Assert.True(File.Exists(reimported.ImagePath));
    }

    [Fact]
    public void ImportPack_WithConflictingId_GetsDeduplicatedSuffix()
    {
        var sourcePng = WriteDummyPng(Path.Combine(_charmsDir, "_src"));
        _registry.Import(new CharmManifest { Id = "dup", Name = "Original" }, sourcePng, null);

        // Build a pack from scratch (as if authored elsewhere) that happens to reuse the same id
        // as the charm already installed locally.
        var zipPath = Path.Combine(_charmsDir, "dup-pack.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open()))
                writer.Write("""{"id":"dup","name":"Duplicate","image":"charm.png","thumbnail":"charm.png"}""");
            archive.CreateEntryFromFile(sourcePng, "charm.png");
        }

        var imported = _registry.ImportPack(zipPath);

        Assert.NotEqual("dup", imported.Manifest.Id);
        Assert.StartsWith("dup", imported.Manifest.Id);
        // Both the original and the newly imported copy must still be independently loadable.
        Assert.Equal(2, _registry.LoadAll().Count(p => p.Manifest.Name is "Original" or "Duplicate"));
    }

    [Fact]
    public void SeedFromBundledIfEmpty_OnlySeedsWhenCharmsDirectoryIsEmpty()
    {
        // Must live outside _charmsDir - SeedFromBundledIfEmpty bails out as soon as the
        // *destination* charms directory has any subdirectory at all, and dropping the bundled
        // source inside it would trip that guard before seeding ever runs.
        var bundledDir = Path.Combine(Path.GetTempPath(), "CharmDeskTests", Guid.NewGuid().ToString("N") + "_bundled");
        var bundledCharmDir = Path.Combine(bundledDir, "seed-charm");
        Directory.CreateDirectory(bundledCharmDir);
        File.WriteAllText(Path.Combine(bundledCharmDir, "manifest.json"),
            """{"id":"seed-charm","name":"Seed Charm","image":"charm.png","thumbnail":"charm.png"}""");
        File.WriteAllBytes(Path.Combine(bundledCharmDir, "charm.png"), [0x89, 0x50, 0x4E, 0x47]);

        _registry.SeedFromBundledIfEmpty(bundledDir);
        Assert.Single(_registry.LoadAll());

        // Add a second, unrelated local charm, then try seeding again - the directory is no
        // longer empty, so nothing bundled should be re-copied over it.
        var sourcePng = WriteDummyPng(Path.Combine(_charmsDir, "_src2"));
        _registry.Import(new CharmManifest { Id = "user-added", Name = "User Added" }, sourcePng, null);
        _registry.SeedFromBundledIfEmpty(bundledDir);

        Assert.Equal(2, _registry.LoadAll().Count);

        Directory.Delete(bundledDir, recursive: true);
    }
}
