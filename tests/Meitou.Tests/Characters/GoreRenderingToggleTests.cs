using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Meitou.Data.Characters;
using Meitou.Rendering;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public class GoreRenderingToggleTests
{
    const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void Off_ignores_loaded_cut_and_wound_data_without_allocating_or_reading_optional_assets()
    {
        // No GPU, rig or asset locator is present: accidentally preparing wounds or anatomy must fail this test.
        var renderer = (CharacterRenderer)RuntimeHelpers.GetUninitializedObject(typeof(CharacterRenderer));
        var content = (CharacterContent)RuntimeHelpers.GetUninitializedObject(typeof(CharacterContent));
        Field(renderer, "content", content);
        Assert.False(renderer.GoreEnabled);
        var asset = new CharacterAsset { Appearance = null!, Rig = null! };
        var part = new AssetPart
        {
            Label = "native attachment", Mesh = null!, Shared = false, Bone = 0,
            Material = new PartMaterial(), BoundsCentre = default, BoundsRadius = 1,
        };
        asset.Parts.Add(part);
        var live = Activator.CreateInstance(typeof(CharacterRenderer).GetNestedType("Live", BindingFlags.NonPublic)!)!;
        Field(live, "Asset", asset);
        Field(live, "Blood", 0x12345678UL);
        foreach (string name in new[] { "WoundMask", "LostGroups", "LimbCuts", "PartOnly" }) Field(live, name, uint.MaxValue);
        Field(live, "LimbPlanes", new LimbCutPlanes(Matrix4x4.Identity, 15));
        Field(live, "HasWounds", true);
        Field(live, "HeadReliefReady", true);
        Field(live, "HeadFrame", Matrix4x4.Identity);
        Field(live, "TorsoFrame", Matrix4x4.Identity);
        Field(live, "TorsoCut", new CharacterTorsoCut(new(0, 1, 0, -10), 1));
        Field(live, "WoundSpheres", new Vector4[1]);
        Field(live, "WoundCuts", new Vector4[1]);
        var instance = new CharacterInstance(1, null!, default, 0, [])
        {
            TorsoCut = new(new(0, 1, 0, -10), -1), LostGroups = 255, LimbCuts = uint.MaxValue, PartOnly = 511,
            Wounds = [new("pelvis", "head", .5f, Vector3.UnitZ, 1, 0, 1, 1)],
            HeadWoundPlacement = new(default, Vector3.UnitZ, Vector3.UnitX),
            LimbSeverances = [new LimbCutState(0, .5f, new(.2f, -.1f))],
        };
        object?[] parameters = [live, instance];
        typeof(CharacterRenderer).GetMethod("ConfigureWounds", InstanceFields)!.Invoke(renderer, parameters);
        live = parameters[0]!;

        foreach (string name in new[] { "WoundMask", "LostGroups", "LimbCuts", "PartOnly" }) Assert.Equal(0u, Field<uint>(live, name));
        Assert.False(Field<bool>(live, "HasWounds"));
        Assert.False(Field<bool>(live, "HeadReliefReady"));
        Assert.Equal(default, Field<LimbCutPlanes>(live, "LimbPlanes"));
        Assert.Equal(default, Field<Matrix4x4>(live, "HeadFrame"));
        Assert.Equal(default, Field<Matrix4x4>(live, "TorsoFrame"));
        Assert.Null(Field<object?>(live, "TorsoCut"));
        Assert.Null(Field<object?>(live, "WoundSpheres"));
        Assert.Null(Field<object?>(live, "WoundCuts"));
        Assert.Same(asset.Parts, Field<object>(live, "DrawParts"));
        Assert.Equal(0x12345678UL, Field<ulong>(live, "Blood"));
        Assert.True((bool)typeof(CharacterRenderer).GetMethod("PartShown", InstanceFields)!.Invoke(renderer, [live, part])!);
        Assert.Null(Field<object?>(renderer, "allocatedWoundMasks"));
        foreach (string name in new[] { "allocatedHeadWoundPool", "allocatedTorsoCutPool", "allocatedLimbCutPool" })
            Assert.Null(Field<object?>(content, name));
        // Presentation ignored the durable input rather than clearing it.
        Assert.NotNull(instance.TorsoCut);
        Assert.Single(instance.LimbSeverances);
        Assert.Single(instance.Wounds);
    }

    static void Field(object value, string name, object? field) => value.GetType().GetField(name, InstanceFields)!.SetValue(value, field);
    static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, InstanceFields)!.GetValue(value)!;
}
