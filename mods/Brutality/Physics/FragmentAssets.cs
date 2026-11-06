using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Data.Physics;

namespace Meitou.Brutality.Physics;

/// <summary>Decoded fragment inputs shared by equipment support, the fragment simulation and healthy render preparation.</summary>
internal sealed class FragmentAssets(CharacterRigs rigs)
{
    static readonly ConditionalWeakTable<CharacterRigs, FragmentAssets> caches = new();
    readonly ConcurrentDictionary<string, RagdollSkinSurface> skins = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<(string Path, SkeletonRig Rig, bool Robot), RagdollDefinition?> definitions = new();

    internal static FragmentAssets For(CharacterRigs rigs) => caches.GetValue(rigs, static value => new(value));

    internal RagdollSkinSurface Skin(string path) =>
        skins.GetOrAdd(path, static path => RagdollSkinSurface.Read(OgreMeshReader.ReadFile(path)));

    internal RagdollDefinition? Definition(CharacterAppearance look, SkeletonRig rig)
    {
        if (rigs.Find(RagdollName(look.Race, look.Female)) is not { } path) return null;
        return definitions.GetOrAdd((path, rig, look.Race.GetBool("is robot")),
            key => ReadDefinition(key.Path, key.Rig, key.Robot, rigs.Find));
    }

    internal void Prepare(string bodyPath, OgreMesh mesh, GameRecord race, bool female, SkeletonRig rig, OgreScriptResources indexed, string installRoot)
    {
        // ConcurrentDictionary publishes completed results only. A hit during preparation can decode its own inputs.
        if (!skins.ContainsKey(bodyPath)) skins.TryAdd(bodyPath, RagdollSkinSurface.Read(mesh));

        string? FindAvailable(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            bool absolute = Path.IsPathRooted(name);
            if (absolute && GameFiles.Exists(name)) return Path.GetFullPath(GameFiles.Resolve(name));
            if (indexed.Find(Path.GetFileName(name)) is { } configured) return configured;
            if (absolute) return null;
            var installed = GameFiles.Resolve(Path.GetFullPath(Path.Combine(installRoot, name)));
            return File.Exists(installed) ? installed : File.Exists(name) ? Path.GetFullPath(name) : null;
        }

        // Skip lookups that could need the locator's lazy full-directory scan. A missing hull here is inconclusive.
        if (FindAvailable(RagdollName(race, female)) is not { } path) return;
        var key = (Path: path, Rig: rig, Robot: race.GetBool("is robot"));
        if (definitions.ContainsKey(key)) return;
        bool complete = true;
        try
        {
            var definition = ReadDefinition(key.Path, key.Rig, key.Robot, name =>
            {
                var found = FindAvailable(name);
                if (found is null) complete = false;
                return found;
            });
            if (complete) definitions.TryAdd(key, definition);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            // The authoritative lookup keeps its existing error behavior on use.
        }
    }

    static string RagdollName(GameRecord race, bool female)
    {
        string file = race.GetPath(female ? "female ragdoll" : "male ragdoll");
        return string.IsNullOrEmpty(file) ? race.GetPath("male ragdoll") : file;
    }

    static RagdollDefinition? ReadDefinition(string path, SkeletonRig rig, bool robot, Func<string, string?> find)
    {
        Vector3[]? Hull(string name) => find(name) is { } hull
            ? CookedMeshReader.ReadConvex(File.ReadAllBytes(GameFiles.Resolve(hull))).Vertices : null;
        var definition = RagdollDefinition.Build(PhysicsFile.ReadFile(path), rig, 1, Hull);
        return robot && definition is not null ? RobotRagdollWeight.Weight(definition) : definition;
    }
}
