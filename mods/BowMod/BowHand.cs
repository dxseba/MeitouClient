using System.Numerics;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Modding;

namespace Meitou.BowMod;

/// <summary>
/// The held bow in the left hand (README "Facts"): the original moves every part a bow holder's <c>hands</c> point attaches (to <c>Bip01 Prop2</c>)
/// to <c>Bip01 L Hand</c>, its offset turned by a fixed rotation and pushed by a fixed shift, when the skeleton has that bone. Done by Harmony as a
/// postfix on <c>Meitou.Game.Rendering.Characters.CharacterContent.AttachToBone</c> for the parts of a bow's drawn set
/// (<see cref="WeaponShown.RangedDrawn"/>): the bone becomes the left hand and the part's offset is followed by the rotation and the shift. Runs when
/// a look's asset is built; a failure logs once and leaves the part at the right hand.
/// </summary>
static class BowHand
{
    const string Hand = "Bip01 L Hand";

    /// <summary>The original's rotation (Ogre w, x, y, z = 0.025419, -0.593545, -0.016245, -0.804235) and shift, applied after the part's offset.</summary>
    static readonly Matrix4x4 ToHand = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new(-0.593545f, -0.016245f, -0.804235f, 0.025419f)))
        * Matrix4x4.CreateTranslation(0.954711f, -1.088662f, -0.654805f);

    static AccessTools.FieldRef<object, int>? s_bone;
    static AccessTools.FieldRef<object, Matrix4x4>? s_offset;
    static ModContext? s_context;
    static int s_failed;

    public static void Patch(ModContext context)
    {
        s_context = context;
        var content = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterContent");
        var part = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.AssetPart");
        var attach = content is null ? null : AccessTools.Method(content, "AttachToBone");
        if (attach is null || part is null || AccessTools.Field(part, "Bone") is null || AccessTools.Field(part, "Offset") is null)
        {
            context.LogError("CharacterContent.AttachToBone or AssetPart not found: bows stay in the right hand");
            return;
        }

        s_bone = AccessTools.FieldRefAccess<int>(part, "Bone");
        s_offset = AccessTools.FieldRefAccess<Matrix4x4>(part, "Offset");
        context.Harmony.Patch(attach, postfix: new HarmonyMethod(typeof(BowHand), nameof(Attached)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Attached(object ap, CharacterPart part, SkeletonRig rig)
    {
        try
        {
            if (part is not { Point: "hands", Shown: WeaponShown.RangedDrawn } || !BowSkill.IsBowRecord(part.Record.StringId)) return;
            if (rig.Handle(Hand) is not int hand || s_bone is null || s_offset is null) return;

            s_bone(ap) = hand;
            s_offset(ap) = s_offset(ap) * ToHand;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"bow hand binding failed ({e.GetType().Name}: {e.Message})");
        }
    }
#pragma warning restore IDE1006
}
