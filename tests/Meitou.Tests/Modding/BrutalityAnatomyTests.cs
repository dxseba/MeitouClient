using System.Numerics;
using System.Text;
using Meitou.Brutality;
using Meitou.Brutality.Anatomy;
using Meitou.Content;

namespace Meitou.Tests.Modding;

public class BrutalityAnatomyTests
{
    sealed class Package : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("meitou-brutality-anatomy-").FullName;
        public string Folder(string name = "Brutality") => Path.Combine(Root, name);
        public string Asset(string name, string package = "Brutality") => Path.Combine(Folder(package), "assets", "anatomy", name);
        public void Write(string name, byte[] bytes, string package = "Brutality")
        {
            string path = Asset(name, package);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    static readonly string[] FileNames = ["Ecorche.anatomy", "Brain.anatomy", "Mouth.anatomy"];
    static readonly AnatomyKind[] Kinds = [AnatomyKind.Body, AnatomyKind.Brain, AnatomyKind.Mouth];

    [Fact]
    public void A_synthetic_import_keeps_rigid_batch_bones_layers_and_vertices_and_is_cached_after_the_file_changes()
    {
        using var package = new Package();
        var first = Vertices(4);
        var second = Vertices(12);
        package.Write("Ecorche.anatomy", Encoded(new AnatomyBatch("Bip01 Spine1", 0, first), new AnatomyBatch("Bip01 Head", 6, second)));
        var assets = new AnatomyAssets(package.Folder());
        var library = new AnatomyLibrary(assets);

        var imported = library.Get(AnatomyKind.Body);

        Assert.Equal(2, imported.Count);
        Assert.Equal("Bip01 Spine1", imported[0].Bone);
        Assert.Equal(0, imported[0].Layer);
        Assert.Equal(first, imported[0].Vertices);
        Assert.Equal("Bip01 Head", imported[1].Bone);
        Assert.Equal(6, imported[1].Layer);
        Assert.Equal(second, imported[1].Vertices);
        Assert.Equal(Path.GetFullPath(package.Asset("Ecorche.anatomy")), library.PathFor(AnatomyKind.Body));
        File.WriteAllText(assets.Require("Ecorche.anatomy"), "not anatomy anymore");
        Assert.Same(imported, library.Get(AnatomyKind.Body));
        var repeated = new IReadOnlyList<AnatomyBatch>[16];
        Parallel.For(0, repeated.Length, i => repeated[i] = library.Get(AnatomyKind.Body));
        Assert.All(repeated, value => Assert.Same(imported, value));
    }

    [Fact]
    public void Missing_optional_files_remain_missing_even_when_another_mod_has_anatomy_and_names_cannot_escape_the_package()
    {
        using var package = new Package();
        string unrelated = Path.Combine(package.Root, "Gauntlet of Crowns");
        Directory.CreateDirectory(unrelated);
        foreach (string name in FileNames) File.WriteAllBytes(Path.Combine(unrelated, name), Encoded(new AnatomyBatch("Bip01 Head", 0, Vertices(7))));
        Directory.CreateDirectory(package.Folder());
        var assets = new AnatomyAssets(package.Folder());
        var library = new AnatomyLibrary(assets);

        Assert.Null(assets.Ecorche);
        Assert.Null(assets.Brain);
        Assert.Null(assets.Mouth);
        foreach (var kind in Kinds)
        {
            Assert.Null(library.PathFor(kind));
            Assert.Empty(library.Get(kind));
            Assert.Same(library.Get(kind), library.Get(kind));
        }
        Assert.Throws<FileNotFoundException>(() => assets.Require("Ecorche.anatomy"));
        Assert.Throws<ArgumentOutOfRangeException>(() => assets.Resolve("../../Gauntlet of Crowns/Ecorche.anatomy"));
        Assert.Throws<ArgumentOutOfRangeException>(() => assets.Resolve(Path.Combine(unrelated, "Brain.anatomy")));
        Assert.Throws<ArgumentOutOfRangeException>(() => library.Get((AnatomyKind)99));
    }

    [Fact]
    public void All_three_mod_relative_assets_can_be_copied_to_another_folder_without_the_original_package()
    {
        using var package = new Package();
        for (int i = 0; i < FileNames.Length; i++) package.Write(FileNames[i], Encoded(new AnatomyBatch("Bip01 Head", i + 4, Vertices(10 + i))));
        const string relocated = "RelocatedBrutality";
        foreach (string name in FileNames)
        {
            package.Write(name, File.ReadAllBytes(package.Asset(name)), relocated);
            File.Delete(package.Asset(name));
        }
        var assets = new AnatomyAssets(package.Folder(relocated));
        var library = new AnatomyLibrary(assets);

        for (int i = 0; i < FileNames.Length; i++)
        {
            string path = Assert.IsType<string>(library.PathFor(Kinds[i]));
            Assert.Equal(Path.GetFullPath(package.Asset(FileNames[i], relocated)), path);
            Assert.Equal(path, assets.Require(FileNames[i]));
            var batch = Assert.Single(library.Get(Kinds[i]));
            Assert.Equal(i + 4, batch.Layer);
            Assert.Equal(Vertices(10 + i), batch.Vertices);
        }
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("batch-count")]
    [InlineData("layer")]
    [InlineData("vertex-count")]
    [InlineData("bone-name")]
    [InlineData("position")]
    [InlineData("normal")]
    [InlineData("uv")]
    public void Corrupt_headers_or_nonfinite_geometry_are_reported_instead_of_returning_partial_batches(string defect)
    {
        var vertices = Vertices(1);
        if (defect == "position") vertices[1] = vertices[1] with { Position = new(float.NaN, 0, 0) };
        if (defect == "normal") vertices[1] = vertices[1] with { Normal = new(0, float.PositiveInfinity, 0) };
        if (defect == "uv") vertices[1] = vertices[1] with { Uv = new(0, float.NegativeInfinity) };
        var bytes = Encoded(new AnatomyBatch(defect == "bone-name" ? "bad\0bone" : "Bip01 Head", defect == "layer" ? 7 : 0, vertices));
        if (defect == "magic") bytes[0] = 0;
        if (defect == "batch-count") BitConverter.GetBytes(91).CopyTo(bytes, 4);
        if (defect == "vertex-count") BitConverter.GetBytes(4).CopyTo(bytes, 16);
        using var stream = new MemoryStream(bytes);

        Assert.Throws<InvalidDataException>(() => AnatomyFile.Read(stream));
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(22)]
    [InlineData(42)]
    [InlineData(124)]
    public void A_truncated_file_never_exposes_partial_geometry(int length)
    {
        var encoded = Encoded(new AnatomyBatch("Bip01 Head", 0, Vertices(1)));
        Assert.True(length < encoded.Length);
        using var stream = new MemoryStream(encoded[..length]);

        Assert.ThrowsAny<IOException>(() => AnatomyFile.Read(stream));
    }

    [Fact]
    public void A_present_corrupt_optional_file_is_reported_rather_than_treated_as_missing()
    {
        using var package = new Package();
        package.Write("Brain.anatomy", Encoding.ASCII.GetBytes("broken"));
        var library = new AnatomyLibrary(new AnatomyAssets(package.Folder()));

        Assert.NotNull(library.PathFor(AnatomyKind.Brain));
        Assert.Throws<InvalidDataException>(() => library.Get(AnatomyKind.Brain));
        Assert.Empty(library.Get(AnatomyKind.Body));
        Assert.Empty(library.Get(AnatomyKind.Mouth));
    }

    [Fact, Slow]
    public void The_three_installed_local_anatomy_files_are_read_without_copying_them_into_the_repository()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi is not installed");
        string folder = GameFiles.Resolve(Path.Combine(install.ModsDirectory, "Gauntlet of Crowns"));
        var paths = FileNames.Select(name => GameFiles.Resolve(Path.Combine(folder, name))).ToArray();
        Assert.SkipWhen(paths.Any(path => !File.Exists(path)), "The local Gauntlet of Crowns anatomy files are absent");

        foreach (string path in paths)
        {
            using var stream = File.OpenRead(path);
            var batches = AnatomyFile.Read(stream);
            Assert.NotEmpty(batches);
            Assert.InRange(batches.Sum(batch => batch.Vertices.Length), 3, 1500000);
            Assert.All(batches, batch =>
            {
                Assert.False(string.IsNullOrWhiteSpace(batch.Bone));
                Assert.InRange(batch.Layer, 0, 6);
                Assert.Equal(0, batch.Vertices.Length % 3);
            });
        }
    }

    static AnatomyVertex[] Vertices(float marker) =>
    [
        new(new(marker, 2, 3), Vector3.UnitY, new(0, 0)),
        new(new(marker + 1, 2, 3), Vector3.UnitY, new(1, 0)),
        new(new(marker, 3, 3), Vector3.UnitY, new(0, 1)),
    ];

    static byte[] Encoded(params AnatomyBatch[] batches)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("ECR1"));
        writer.Write(batches.Length);
        foreach (var batch in batches)
        {
            var name = Encoding.ASCII.GetBytes(batch.Bone);
            writer.Write(name.Length); writer.Write(batch.Layer); writer.Write(batch.Vertices.Length); writer.Write(name);
            foreach (var vertex in batch.Vertices)
            {
                writer.Write(vertex.Position.X); writer.Write(vertex.Position.Y); writer.Write(vertex.Position.Z);
                writer.Write(vertex.Normal.X); writer.Write(vertex.Normal.Y); writer.Write(vertex.Normal.Z);
                writer.Write(vertex.Uv.X); writer.Write(vertex.Uv.Y);
            }
        }
        writer.Flush();
        return stream.ToArray();
    }
}
