using System.Security.Cryptography;
using System.Text.Json;
using Meitou.Content;

namespace Meitou.Tests.Characters;

public class AnatomyAssetsTests
{
    static string OutputBundle => Path.Combine(AppContext.BaseDirectory, "assets", "anatomy");

    [Fact]
    public void Bundled_body_has_priority_over_a_different_legacy_install()
    {
        using var temp = new TemporaryBundle();
        string legacyFolder = Path.Combine(temp.Root, "mods", "Gauntlet of Crowns");
        Directory.CreateDirectory(legacyFolder);
        string legacyBody = Path.Combine(legacyFolder, "Ecorche.anatomy");
        File.WriteAllText(legacyBody, "legacy fixture, not the packaged anatomy");

        string expected = GameFiles.Resolve(Path.Combine(OutputBundle, "Ecorche.anatomy"));
        Assert.True(File.Exists(expected), "The test output must include the packaged anatomical bundle");
        string? selected = AnatomyAssets.FindBody(Path.Combine(temp.Root, "mods"));
        Assert.Equal(expected, selected);
        Assert.False(string.Equals(legacyBody, selected, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected, AnatomyAssets.FindBody());
    }

    [Fact]
    public void Companions_use_case_corrected_siblings_and_never_mix_bundles()
    {
        using var temp = new TemporaryBundle();
        string body = Path.Combine(temp.Root, "Ecorche.anatomy");
        File.WriteAllText(body, "body fixture");
        string brain = Path.Combine(temp.Root, "bRaIn.AnAtOmY");
        File.WriteAllText(brain, "this body's brain");
        string otherBundle = Path.Combine(temp.Root, "legacy", "Gauntlet of Crowns");
        Directory.CreateDirectory(otherBundle);
        File.WriteAllText(Path.Combine(otherBundle, "Mouth.anatomy"), "another body's mouth");

        string? selected = AnatomyAssets.Companion(body, "Brain.anatomy");
        Assert.NotNull(selected);
        Assert.Equal("this body's brain", File.ReadAllText(selected));
        Assert.True(string.Equals(brain, selected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(OutputBundle, "Mouth.anatomy")), "A bundled companion exists but must not be mixed into this separate body");
        Assert.Null(AnatomyAssets.Companion(body, "Mouth.anatomy"));
    }

    [Fact]
    public void Output_contains_the_complete_manifest_notices_and_byte_identical_anatomy()
    {
        string manifestPath = Path.Combine(OutputBundle, "manifest.json");
        Assert.True(File.Exists(manifestPath), "Anatomy manifest must travel with the output bundle");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("ECR1", root.GetProperty("format").GetString());
        var entries = root.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Brain.anatomy", "Ecorche.anatomy", "Mouth.anatomy" },
            entries.Select(entry => entry.GetProperty("path").GetString()!).Order(StringComparer.Ordinal).ToArray());
        foreach (var entry in entries)
        {
            string name = entry.GetProperty("path").GetString()!;
            Assert.Equal(name, Path.GetFileName(name));
            string path = Path.Combine(OutputBundle, name);
            Assert.True(File.Exists(path), $"Packaged anatomical mesh is missing: {name}");
            Assert.Equal(entry.GetProperty("bytes").GetInt64(), new FileInfo(path).Length);
            using var input = File.OpenRead(path);
            Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant());
        }
        var notices = root.GetProperty("notices").EnumerateArray().Select(entry => entry.GetString()!).ToArray();
        foreach (string required in new[] { "ATTRIBUTION.md", "Ecorche-CC-BY-SA-4.0.txt", "Ecorche-MIT.txt", "LICENSE.md" })
            Assert.Contains(required, notices);
        foreach (string name in notices)
        {
            Assert.Equal(name, Path.GetFileName(name));
            string path = Path.Combine(OutputBundle, name);
            Assert.True(File.Exists(path), $"Packaged anatomy notice is missing: {name}");
            Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(path)));
        }
    }

    sealed class TemporaryBundle : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Meitou-anatomy-resolver-" + Guid.NewGuid().ToString("N"));
        public TemporaryBundle() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
