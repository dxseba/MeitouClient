using HarmonyLib;
using Meitou.Data.Gameplay.Construction;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Items;
using Meitou.Simulation.Production;

namespace Meitou.DesolationSystem;

/// <summary>
/// Heavenly Artifice (README "Facts"), with the smith's or builder's artifice factor f (0 at a stat sum of 300, 1 at 600): at a bench
/// (<c>ProductionSystem.CraftStep</c> and <c>Finish</c>) the craft runs (1 + 2f) times as fast, the smith's skill counts 15 f higher (at most 100),
/// the critical chance is 0.3 f higher (<c>CraftBench.CriticalChance</c> through <c>CraftStep</c>), a weapon has a 6 f % chance to come out as the
/// bench's best model, and each finished item gets floor(2f) copies in the output; on a building site (<c>ConstructionSystem.BuildStep</c>) the
/// work goes (1 + 5f) times as fast.
/// </summary>
static partial class DesoPatches
{
    /// <summary>The artifice factor of the craft step running on this thread, for the critical chance.</summary>
    [ThreadStatic]
    static float s_artifice;

    static void InstallCraft(ModContext context)
    {
        Patch(typeof(ProductionSystem), "CraftStep", prefix: nameof(CraftStepPrefix), postfix: nameof(CraftStepPostfix));
        Patch(typeof(ProductionSystem), "Finish", prefix: nameof(FinishPrefix), postfix: nameof(FinishPostfix));
        Prefix(typeof(ConstructionSystem), "BuildStep", nameof(BuildStepPrefix));
        Calls.Swap(context, AccessTools.Method(typeof(CraftBench), nameof(CraftBench.CriticalChance)), Method(nameof(CriticalChance)),
            AccessTools.Method(typeof(ProductionSystem), "CraftStep"));
    }

    static float ArtificeOf(World world, CharacterId who) =>
        world.Characters.TryResolveNext(who, out int slot) ? TraitTable.Of(world.Characters.Cold(slot)).Artifice : 0;

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void CraftStepPrefix(World world, Machine m, ref float skill, ref float s)
    {
        s_artifice = 0;
        try
        {
            float f = ArtificeOf(world, m.Smith);
            if (f <= 0) return;

            s_artifice = f;
            s *= 1 + 2 * f;
            skill = Math.Max(skill, Math.Min(100, skill + 15 * f));
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void CraftStepPostfix() => s_artifice = 0;

    static void FinishPrefix(CraftState state, out int[] __state) => __state = [.. state.Done.Select(d => d.Quantity)];

    static void FinishPostfix(World world, Machine m, CraftState state, CraftJob job, int[] __state)
    {
        try
        {
            float f = ArtificeOf(world, m.Smith);
            if (f <= 0 || Made(state, __state) is not { } made) return;

            if (job.Option.Kind == CraftKind.Weapon && state.Done.Count > __state.Length) Meitou(world, m, made, f);
            Copies(state, made, (int)MathF.Floor(2 * f + 1e-4f));
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void BuildStepPrefix(World world, CharacterCold cold, ref float dt)
    {
        try
        {
            float f = TraitTable.Of(cold).Artifice;
            if (f > 0) dt *= 1 + 5 * f;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006

    /// <summary><c>CraftStep</c>'s critical chance: +0.3 f, at most 1.</summary>
    static float CriticalChance(float skill, int stat, int level) => Math.Min(1, CraftBench.CriticalChance(skill, stat, level) + 0.3f * s_artifice);

    /// <summary>6 f % of weapons come out as the bench's best model of that weapon (a roll of the craft's bench and count).</summary>
    static void Meitou(World world, Machine m, ItemInstance made, float f)
    {
        float roll = Rng.Float(Rng.Hash(world.Seed, Rng.StableHash(m.Site), DesolationSystemMod.Purpose, (ulong)(uint)(m.Craft?.Rolls ?? 0) ^ 0x4D45));
        if (roll >= 0.06f * f || m.Data.Craft is not { } bench) return;

        var best = bench.Options.Where(o => o.Kind == CraftKind.Weapon && o.Item == made.Record).OrderByDescending(o => o.Level).FirstOrDefault();
        if (best is null) return;

        made.Level = best.Level;
        made.MaterialId = best.Model ?? made.MaterialId;
    }

    static void Copies(CraftState state, ItemInstance made, int copies)
    {
        for (int k = 0; k < copies; k++)
        {
            if (made.Quantity < made.StackLimit) made.Quantity++;
            else
            {
                var copy = made.Copy();
                copy.Quantity = 1;
                state.Done.Add(copy);
                made = copy;
            }
        }
    }

    /// <summary>The output stack the finish just added to: a new one, or one whose count grew.</summary>
    static ItemInstance? Made(CraftState state, int[] before)
    {
        if (state.Done.Count > before.Length) return state.Done[^1];

        for (int i = 0; i < before.Length && i < state.Done.Count; i++)
        {
            if (state.Done[i].Quantity > before[i]) return state.Done[i];
        }

        return null;
    }
}
