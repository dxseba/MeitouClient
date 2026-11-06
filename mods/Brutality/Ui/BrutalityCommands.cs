using System.Globalization;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Brutality.Ui
{
    public static class BrutalityDebugCommands
    {
        public static void Register(ModContext context, bool developerTools)
        {
            context.AddDebugCommand("Brutality level (0-100)", debug =>
            {
                if (debug.Selected.Count == 0) return "Select a character.";
                if (!float.TryParse(debug.Field, NumberStyles.Float, CultureInfo.InvariantCulture, out float level) || !float.IsFinite(level) || level is < 0 or > 100)
                    return "Enter a level from 0 to 100.";
                debug.Enqueue(new SetBrutalityLevel(debug.Selected.ToArray(), level) { Tick = debug.Tick });
                return $"Brutality level {level:0.##} queued for {debug.Selected.Count} character(s).";
            }, hasField: true);
            if (!developerTools) return;
            context.AddDebugCommand("Gore preview (torso, vertical, head, limb, severe, chest, stomach)", debug =>
            {
                if (debug.Selected.Count == 0) return "Select a character.";
                var fields = debug.Field.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var kind = fields.FirstOrDefault()?.ToLowerInvariant() switch
                {
                    "torso" => GorePreviewKind.Torso, "vertical" => GorePreviewKind.VerticalTorso, "head" => GorePreviewKind.Head,
                    "limb" => GorePreviewKind.Limb, "severe" => GorePreviewKind.SevereHead, "chest" => GorePreviewKind.ChestHole,
                    "stomach" => GorePreviewKind.StomachHole, _ => (GorePreviewKind?)null,
                };
                if (kind is null || fields.Length > 2 || fields.Length == 2 && !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    return "Use torso, vertical, head 0-2, limb 0-3, severe 0-5, chest 0-5 or stomach 0-5 (3-5 kill the target).";
                int variant = fields.Length == 2 ? int.Parse(fields[1], CultureInfo.InvariantCulture) : 0;
                if (variant < 0 || variant > MaxVariant(kind.Value))
                    return "The preview variant is outside its range.";
                debug.Enqueue(new PreviewBrutalityCut(debug.Selected.ToArray(), kind.Value, variant) { Tick = debug.Tick });
                return "Permanent gore preview queued for the selection.";
            }, hasField: true);
        }

        static int MaxVariant(GorePreviewKind kind) => kind switch
        {
            GorePreviewKind.Head => 2,
            GorePreviewKind.Limb => 3,
            GorePreviewKind.SevereHead or GorePreviewKind.ChestHole or GorePreviewKind.StomachHole => 5,
            _ => 0,
        };
    }
}

namespace Meitou.Brutality
{
    public sealed record SetBrutalityLevel(IReadOnlyList<CharacterId> Characters, float Level) : SimCommand;
    public sealed record PreviewBrutalityCut(IReadOnlyList<CharacterId> Characters, GorePreviewKind Kind, int Variant = 0) : SimCommand;

    public sealed partial class BrutalityGameplaySystem
    {
        public void Inputs(World world, IReadOnlyList<SimCommand> commands)
        {
            foreach (var command in commands)
                switch (command)
                {
                    case SetBrutalityLevel level when float.IsFinite(level.Level) && level.Level is >= 0 and <= 100:
                        foreach (var target in level.Characters) SetLevel(world, target, level.Level);
                        break;
                    case PreviewBrutalityCut preview:
                        foreach (var target in preview.Characters) ApplyDebugCut(world, target, preview.Kind, preview.Variant);
                        break;
                }
        }
    }
}
