using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.ArcaneMagic;

/// <summary>
/// The interface (README "Facts"): the original's Mana HUD and spell selector as two lines of the HUD's character panel (mana with its bar;
/// the spell, a press cycling to the next one, Shift + press locking or unlocking it, the original's Shift + right-click), a key action cycling
/// the selected characters' spell, and the host's frame drawing the spells' flashes and showing the unlock notices.
/// </summary>
static class ArcaneUi
{
    /// <summary>Shift held at the last frame (the panel line's press carries no modifiers).</summary>
    static volatile bool s_shift;

    public static void Install(ModContext context)
    {
        context.AddUi(new Panel());
        context.AddKeyAction("cycle_spell", "Next Arcane spell", key =>
        {
            if (key.Selected.Count > 0) key.Enqueue(new CycleSpell([.. key.Selected]) { Tick = key.Tick });
        });
        context.AddFrame(frame => Frame(context, frame));
    }

    static void Frame(ModContext context, ModFrame frame)
    {
        s_shift = frame.Shift;
        if (ArcaneSystem.Running is not { } system) return;

        while (system.Visuals.TryDequeue(out var visual))
        {
            if (visual.Notice is { } notice)
            {
                frame.Notify(notice);
                continue;
            }

            var anchor = visual.On.IsNone ? EffectAnchor.At(visual.At) : EffectAnchor.Bone(visual.On, "Bip01 L Hand");
            context.PlayEffect(visual.Effect, anchor.Scaled(visual.Size));
        }
    }

    sealed class Panel : IUiExtension
    {
        public string Id => "dxseba.arcanemagic.ui";

        public void Register(UiRegistration registration) => registration.AddCharacterPanelLines(Lines);

        static IEnumerable<UiCharacterLine> Lines(UiCharacterContext context)
        {
            if (ArcaneSystem.Running?.View.TryGetValue(context.Id, out var view) != true) return [];

            string mana = string.Create(CultureInfo.InvariantCulture, $"{MathF.Floor(view!.Mana):0}/{view.Max:0}");
            string spell = ArcaneRules.Caption(view.Spell) + (view.Locked ? " [LOCKED]" : "");
            var id = context.Id;
            long tick = context.Tick;
            return
            [
                new UiCharacterLine("Mana", mana, view.Max > 0 ? view.Mana / view.Max : 0, $"Arcane {view.Level:0}"),
                new UiCharacterLine("Spell", spell, Tooltip: "Click: next spell. Shift + click: lock or unlock it.", Press: () =>
                {
                    SimCommand command = s_shift ? new ToggleSpellLock(view.Spell) { Tick = tick } : new CycleSpell([id]) { Tick = tick };
                    context.Enqueue(command);
                }),
            ];
        }
    }
}
