using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Meitou.Data.Characters;
using Meitou.Rendering;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public sealed class RuntimeCharacterReadinessTests
{
    sealed class Fixture
    {
        public CharacterRenderer Renderer { get; } = (CharacterRenderer)RuntimeHelpers.GetUninitializedObject(typeof(CharacterRenderer));
        public CharacterDrawList Draws { get; } = new();
        public Dictionary<CharacterAppearance, CharacterAsset?> Native { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<(CharacterAppearance Appearance, RuntimeCharacterGeometry Geometry), CharacterAsset?> Runtime { get; } = [];
        public Fixture()
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            void Set(string name, object value) => typeof(CharacterRenderer).GetField(name, fields)!.SetValue(Renderer, value);
            Set("assets", Native); Set("runtimeAssets", Runtime);
            Set("pendingLooks", new HashSet<(CharacterAppearance, RuntimeCharacterGeometry?)>());
            Set("<Draws>k__BackingField", Draws);
            // Readiness queries need only the empty texture accounting, and must never touch a GPU/asset loader.
            var content = (CharacterContent)RuntimeHelpers.GetUninitializedObject(typeof(CharacterContent));
            typeof(CharacterContent).GetField("<Textures>k__BackingField", fields)!.SetValue(content, new WorldTextureCache(null!, null!, "readiness test"));
            Set("content", content);
        }
        public CharacterInstance Item(long key, CharacterAppearance look, RuntimeCharacterGeometry? geometry = null) =>
            new(key, look, Vector3.Zero, 0, []) { Geometry = geometry };
    }
    static CharacterAppearance Look() => new() { Race = null! };

    [Fact]
    public void Actual_runtime_only_appearances_finish_loading_without_a_native_asset_entry()
    {
        var scene = new Fixture(); var look = Look(); var geometry = new RuntimeCharacterGeometry();
        scene.Draws.Items.Add(scene.Item(1, look, geometry));
        Assert.True(scene.Renderer.ContentPending); Assert.Equal(1, scene.Renderer.PendingLoads); Assert.False(scene.Renderer.IsPrepared(look));
        scene.Runtime[(look, geometry)] = (CharacterAsset)RuntimeHelpers.GetUninitializedObject(typeof(CharacterAsset));
        Assert.False(scene.Renderer.ContentPending); Assert.Equal(0, scene.Renderer.PendingLoads); Assert.True(scene.Renderer.IsPrepared(look));
        Assert.Empty(scene.Native);
    }

    [Fact]
    public void One_ready_variant_or_native_appearance_cannot_hide_another_actual_runtime_variant_still_waiting()
    {
        var scene = new Fixture(); var look = Look();
        var first = new RuntimeCharacterGeometry(); var second = new RuntimeCharacterGeometry();
        scene.Native[look] = null;
        scene.Draws.Items.Add(scene.Item(1, look, first)); scene.Draws.Items.Add(scene.Item(2, look, second));
        scene.Draws.Items.Add(scene.Item(3, look, second)); // Repeated actors share one pending upload key.
        Assert.Equal(2, scene.Renderer.PendingLoads); Assert.False(scene.Renderer.IsPrepared(look));
        scene.Runtime[(look, first)] = null; // A reported failure, like a native failed asset, completes exactly this key.
        Assert.True(scene.Renderer.ContentPending); Assert.Equal(1, scene.Renderer.PendingLoads); Assert.False(scene.Renderer.IsPrepared(look));
        scene.Runtime[(look, second)] = null;
        Assert.False(scene.Renderer.ContentPending); Assert.Equal(0, scene.Renderer.PendingLoads); Assert.True(scene.Renderer.IsPrepared(look));
    }

    [Fact]
    public void Speculative_geometry_never_blocks_actual_or_native_completion_and_native_distant_behavior_stays_the_same()
    {
        var scene = new Fixture(); var look = Look(); var visible = new RuntimeCharacterGeometry();
        scene.Draws.Items.Add(scene.Item(1, look, visible)); scene.Runtime[(look, visible)] = null;
        scene.Draws.Preparations.Add(scene.Item(2, look, new RuntimeCharacterGeometry()));
        var speculativeLook = Look(); scene.Draws.Preparations.Add(scene.Item(3, speculativeLook, new RuntimeCharacterGeometry()));
        Assert.True(scene.Renderer.IsPrepared(look)); Assert.False(scene.Renderer.IsPrepared(speculativeLook));
        Assert.False(scene.Renderer.ContentPending); Assert.Equal(0, scene.Renderer.PendingLoads);
        var native = Look(); scene.Draws.Distant.Add(scene.Item(4, native));
        Assert.True(scene.Renderer.ContentPending); Assert.Equal(1, scene.Renderer.PendingLoads); Assert.False(scene.Renderer.IsPrepared(native));
        scene.Native[native] = null;
        Assert.False(scene.Renderer.ContentPending); Assert.Equal(0, scene.Renderer.PendingLoads); Assert.True(scene.Renderer.IsPrepared(native));
    }
}
