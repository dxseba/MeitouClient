using System.Globalization;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.Brutality;

/// <summary>Developer-only preview of stock cut and wound modes on the selected characters.</summary>
internal sealed class StockDebugPreview(BodyOptions options) : ITickSystem
{
    sealed record Preview(CharacterId[] Targets, string Kind, int Variant) : SimCommand;
    const string Usage = "Use torso, vertical, head 0–2, burst, limb 0–3, severe 0–2, chest 0–5 or stomach 0–5. Torso, vertical, head, burst and holes 3–5 kill the target.";

    internal static void Install(ModContext context)
    {
        var options = context.Population.BodyOptions;
        context.AddSystem("developer-preview", _ => new StockDebugPreview(options), SystemAnchor.Before(StandardParts.Combat));
        context.AddDebugCommand("Gore preview (selected)", debug =>
        {
            if (debug.Selected.Count == 0) return "Select a living humanoid.";
            var fields = debug.Field.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            int variant = 0;
            string kind = fields.FirstOrDefault()?.ToLowerInvariant() ?? "";
            int maximum = kind switch
            {
                "torso" or "vertical" or "burst" => 0,
                "head" or "severe" => 2,
                "limb" => 3,
                "chest" or "stomach" => 5,
                _ => -1,
            };
            if (fields.Length > 2 || fields.Length == 2 && !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out variant)
                || variant > maximum) return Usage;
            debug.Enqueue(new Preview(debug.Selected.ToArray(), kind, variant) { Tick = debug.Tick });
            return "Permanent gore preview queued for the selection.";
        }, hasField: true);
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var preview in commands.OfType<Preview>())
        {
            int applied = 0;
            foreach (var id in preview.Targets)
            {
                if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not
                    { Race: not null, Fighter.IsAnimal: false, Medical: { Dead: false } medical } actor) continue;
                bool queued = preview.Kind switch
                {
                    "torso" or "vertical" => StockTorsoRules.QueuePreview(world, id, slot, actor, preview.Kind == "vertical"),
                    "head" or "burst" => options.Decapitation && !medical.Decapitated && StockHeadRules.QueuePreview(world, id, slot, actor,
                        preview.Kind == "burst" ? "burst" : preview.Variant switch { 0 => "oblique", 1 => "split", _ => "neck" }),
                    "limb" => options.AdvancedRagdolls && options.Dismemberment != Dismemberment.Never
                        && StockLimbRules.QueuePreview(world, id, slot, actor, preview.Variant),
                    _ => StockWoundRules.QueuePreview(world, id, slot, actor, preview.Kind, preview.Variant),
                };
                if (queued) applied++;
            }
            if (applied == 0) world.Post("No selected character supports this preview with the current gameplay settings.");
        }
    }
}
