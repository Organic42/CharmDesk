// Explicit rather than implicit: enabling UseWPF (needed to exercise WindowSizing's Rect maths)
// switches this project to the WindowsDesktop SDK's implicit usings, which don't include System.IO.
using System.IO;
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
    public void SyncBundledCharms_SeedsOnFirstRunLikeBefore()
    {
        var bundledDir = Path.Combine(Path.GetTempPath(), "CharmDeskTests", Guid.NewGuid().ToString("N") + "_bundled");
        WriteBundledCharm(bundledDir, "seed-charm", "Seed Charm");

        _registry.SyncBundledCharms(bundledDir);

        Assert.Single(_registry.LoadAll());
        Assert.Equal("Seed Charm", _registry.Find("seed-charm")!.Manifest.Name);

        Directory.Delete(bundledDir, recursive: true);
    }

    [Fact]
    public void SyncBundledCharms_AddsANewlyBundledCharmToAnAlreadyPopulatedLibrary()
    {
        // Simulates the real scenario this exists for: a user who already has the app
        // installed gets an update that bundles an additional charm - it should still reach
        // them, not just fresh installs with an empty charms folder.
        var bundledDir = Path.Combine(Path.GetTempPath(), "CharmDeskTests", Guid.NewGuid().ToString("N") + "_bundled");
        WriteBundledCharm(bundledDir, "old-charm", "Old Charm");
        _registry.SyncBundledCharms(bundledDir);
        Assert.Single(_registry.LoadAll());

        // User adds their own charm, unrelated to the bundle.
        var sourcePng = WriteDummyPng(Path.Combine(_charmsDir, "_src2"));
        _registry.Import(new CharmManifest { Id = "user-added", Name = "User Added" }, sourcePng, null);

        // The "update" ships a second bundled charm alongside the original.
        WriteBundledCharm(bundledDir, "cute-ghost", "Boo");
        _registry.SyncBundledCharms(bundledDir);

        var all = _registry.LoadAll();
        Assert.Equal(3, all.Count);
        Assert.Contains(all, p => p.Manifest.Id == "old-charm");
        Assert.Contains(all, p => p.Manifest.Id == "user-added");
        Assert.Contains(all, p => p.Manifest.Id == "cute-ghost");

        Directory.Delete(bundledDir, recursive: true);
    }

    [Fact]
    public void SyncBundledCharms_DoesNotResurrectAnExcludedId()
    {
        // Regression test: SyncBundledCharms used to match "already installed" purely by folder
        // presence, so a bundled charm the user deleted via Charm Manager would silently come
        // back on the next sync. The caller (App.xaml.cs) now passes deleted ids as excludeIds.
        var bundledDir = Path.Combine(Path.GetTempPath(), "CharmDeskTests", Guid.NewGuid().ToString("N") + "_bundled");
        WriteBundledCharm(bundledDir, "old-charm", "Old Charm");
        _registry.SyncBundledCharms(bundledDir);
        Assert.NotNull(_registry.Find("old-charm"));

        _registry.Delete(_registry.Find("old-charm")!);
        Assert.Null(_registry.Find("old-charm"));

        _registry.SyncBundledCharms(bundledDir, excludeIds: new[] { "old-charm" });

        Assert.Null(_registry.Find("old-charm"));

        Directory.Delete(bundledDir, recursive: true);
    }

    [Fact]
    public void SyncBundledCharms_NeverOverwritesAnAlreadyInstalledCharm()
    {
        // A charm the user has since edited via the Charm Manager must survive a re-sync of
        // the same bundled charm - matching by folder name must not mean "always overwrite".
        var bundledDir = Path.Combine(Path.GetTempPath(), "CharmDeskTests", Guid.NewGuid().ToString("N") + "_bundled");
        WriteBundledCharm(bundledDir, "old-charm", "Old Charm");
        _registry.SyncBundledCharms(bundledDir);

        var installed = _registry.Find("old-charm")!;
        installed.Manifest.Name = "My Custom Charm";
        installed.Manifest.DisplayScale = 2.0;
        _registry.Save(installed);

        _registry.SyncBundledCharms(bundledDir);

        var reloaded = _registry.Find("old-charm")!;
        Assert.Equal("My Custom Charm", reloaded.Manifest.Name);
        Assert.Equal(2.0, reloaded.Manifest.DisplayScale);
    }

    private static void WriteBundledCharm(string bundledDir, string id, string name)
    {
        var dir = Path.Combine(bundledDir, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            $$"""{"id":"{{id}}","name":"{{name}}","image":"charm.png","thumbnail":"charm.png"}""");
        File.WriteAllBytes(Path.Combine(dir, "charm.png"), [0x89, 0x50, 0x4E, 0x47]);
    }
}
