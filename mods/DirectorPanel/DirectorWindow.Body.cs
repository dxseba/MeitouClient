using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation.Bodies;

namespace Meitou.DirectorPanel;

/// <summary>The Body and Effects tabs.</summary>
internal sealed partial class DirectorWindow
{
    /// <summary>The original's bones for an effect, with its names for them.</summary>
    static readonly (string Name, string Bone)[] Bones =
    [
        ("Head", "Bip01 Head"), ("Chest", "Bip01 Spine2"), ("Stomach", "Bip01 Spine1"), ("Left upper arm", "Bip01 L UpperArm"),
        ("Right upper arm", "Bip01 R UpperArm"), ("Left forearm", "Bip01 L Forearm"), ("Right forearm", "Bip01 R Forearm"),
        ("Left thigh", "Bip01 L Thigh"), ("Right thigh", "Bip01 R Thigh"), ("Left calf", "Bip01 L Calf"), ("Right calf", "Bip01 R Calf"),
    ];

    /// <summary>The Body tab's hunger buttons in HUD units (port choice: the original's numbers are not known; the HUD's full is 300).</summary>
    const float FedFully = 300, Starving = 30, StarvingAlive = 1;

    int _part, _damageKind, _direction, _bone = 1;
    bool _bloodOnly = true, _loop;
    Entry? _effect;

    IEnumerable<UiNode> BodyTab()
    {
        if (Target is not { } target || target.Parts.Count == 0)
        {
            yield return Hint("bo-none", Target is null ? "Nobody selected." : "Character has no body parts.");
            yield break;
        }

        yield return Label("bd-cond", texts["Condition:"] + (target.Dead ? " dead" : target.Unconscious ? " KO" : ""));
        foreach (var n in StrikeRows(target)) yield return n;
        foreach (var n in StateRows()) yield return n;
        foreach (var n in LimbRows(target)) yield return n;
        foreach (var n in HungerRows(target)) yield return n;
    }

    IEnumerable<UiNode> StrikeRows(TargetView target)
    {
        yield return Heading("bd-sh", "Strike a body part:");
        var parts = target.Parts;
        _part = Math.Clamp(_part, 0, parts.Count - 1);
        for (int i = 0; i < parts.Count; i++)
        {
            int k = i;
            var p = parts[i];
            yield return Button("bd-p" + i, $"{p.Name}  {F0(p.Flesh)} / {F0(p.Max)}", () => _part = k, 0, picked: i == _part);
        }

        yield return Choices("bd-kind", ["Cutting", "Blunt"], _damageKind, k => _damageKind = k);
        var line = Row("bd-strike");
        line.Children.Add(Label("bd-dmg-label", texts["Damage:"], width: 100));
        line.Children.Add(Field("bd-dmg", 80, "20"));
        line.Children.Add(Button("bd-go", texts["Strike"], () => StrikeNow(parts[_part].Id), 160));
        yield return line;
    }

    void StrikeNow(string part)
    {
        float damage = Math.Clamp(Number("bd-dmg", 20), 0, 10_000);
        Send(new Strike(_target, part, _damageKind == 0 ? damage : 0, _damageKind == 1 ? damage : 0) { Tick = context.Tick });
    }

    IEnumerable<UiNode> StateRows()
    {
        yield return Heading("bd-st", "States:");
        var ko = Row("bd-ko");
        ko.Children.Add(Field("bd-kosec", 80, "30"));
        ko.Children.Add(Button("bd-kogo", texts["Knock out for seconds"], () => State(BodyState.KnockOut, Number("bd-kosec", 30)), 260));
        yield return ko;

        var more = Row("bd-more");
        more.Children.Add(Button("bd-coma", texts["Coma until healed"], () => State(BodyState.Coma), 200));
        more.Children.Add(Button("bd-near", texts["Near death"], () => State(BodyState.NearDeath), 160));
        more.Children.Add(Button("bd-heal", texts["Heal everything"], () => State(BodyState.Heal), 200));
        yield return more;
    }

    void State(BodyState state, float seconds = 0) => Send(new SetBodyState(_target, state, Math.Clamp(seconds, 0, 100_000)) { Tick = context.Tick });

    IEnumerable<UiNode> LimbRows(TargetView target)
    {
        yield return Heading("bd-lh", "Limbs:");
        yield return Hint("bd-lhint", "Limb loss is stored separately from health. Below is each body part's state as the game sees it.");
        for (int i = 0; i < target.Parts.Count; i++)
        {
            var p = target.Parts[i];
            if (!p.IsLimb) continue;

            var line = Row("bd-l" + i);
            line.Children.Add(Label("bd-ln" + i, $"{p.Name}: {texts[LimbWord(p)]}", width: 380));
            bool lost = p.Limb is LimbState.Stump or LimbState.Crushed;
            line.Children.Add(lost
                ? Button("bd-lr" + i, texts["restore"], () => Send(new SetLimb(_target, p.Id, false) { Tick = context.Tick }), 150)
                : Button("bd-ls" + i, texts["sever"], () => Send(new SetLimb(_target, p.Id, true) { Tick = context.Tick }), 150, p.Limb == LimbState.Original));
            yield return line;
        }
    }

    static string LimbWord(PartView p) => p.Limb switch
    {
        LimbState.Stump => "severed",
        LimbState.Crushed => "crushed",
        LimbState.Replaced => "prosthetic",
        _ => "original",
    };

    IEnumerable<UiNode> HungerRows(TargetView target)
    {
        yield return Heading("bd-hh", "Hunger:");
        yield return Hint("bd-hhint", "Higher means more fed. Zero means death by starvation, so the button sets a value close to zero, not zero itself.");
        yield return Label("bd-hnow", texts.Format("Now: %.0f", target.HungerHud));
        var set = Row("bd-hset");
        set.Children.Add(Field("bd-hval", 80, "300"));
        set.Children.Add(Button("bd-hgo", texts["Apply"], () => Send(new SetHunger(_target, Number("bd-hval", FedFully)) { Tick = context.Tick }), 120));
        yield return set;

        var presets = Row("bd-hpre");
        presets.Children.Add(Button("bd-hfed", texts["Feed fully"], () => Send(new SetHunger(_target, FedFully) { Tick = context.Tick }), 180));
        presets.Children.Add(Button("bd-hst", texts["Starving"], () => Send(new SetHunger(_target, Starving) { Tick = context.Tick }), 180));
        presets.Children.Add(Button("bd-hsa", texts["Starving but alive"], () => Send(new SetHunger(_target, StarvingAlive) { Tick = context.Tick }), 220));
        yield return presets;
    }

    IEnumerable<UiNode> EffectsTab()
    {
        if (_target.IsNone)
        {
            yield return Hint("ef-none", "Nobody selected.");
            yield break;
        }

        yield return Heading("ef-gh", "Blood splatter on the ground:");
        yield return Hint("ef-ghint", "Lands around the character. Health is untouched.");
        yield return Label("ef-dir", texts["Hit came from:"]);
        yield return Choices("ef-dirs1", DirectorSystem.SplashDirections[..3], _direction, d => _direction = d);
        yield return Choices("ef-dirs2", DirectorSystem.SplashDirections[3..], _direction - 3, d => _direction = 3 + d);
        foreach (var n in BloodRows()) yield return n;
        yield return LabelledField("ef-power", "Power:", "50");
        yield return LabelledField("ef-times", "Times:", "3");
        yield return Button("ef-splat", texts["Splatter"], Splat, 160);
        foreach (var n in EffectRows()) yield return n;
    }

    void Splat() =>
        Send(new Splatter(_target, _direction, Math.Clamp(Number("ef-power", 50), 1, 500), Math.Clamp((int)Number("ef-times", 3), 1, 50), _cut)
            { Tick = context.Tick });

    IEnumerable<UiNode> EffectRows()
    {
        yield return Heading("ef-eh", "Effect on character:");
        yield return Hint("ef-ehint", "Attaches an effect record to a bone, the way the game attaches sparks in combat. Health is not touched.");
        yield return Tick("ef-blood", "Blood only", _bloodOnly, v => _bloodOnly = v);
        var effects = catalog.Effects.Where(e => !_bloodOnly || e.Group == "blood");
        foreach (var n in PickList("ef", effects, _effect?.Id, e => _effect = e, rows: 8)) yield return n;

        yield return Label("ef-bone", texts["Bone:"]);
        for (int row = 0; row < Bones.Length; row += 4)
        {
            var names = Bones.Skip(row).Take(4).Select(b => b.Name).ToArray();
            int first = row;
            yield return Choices("ef-bones" + row, names, _bone - first, b => _bone = first + b);
        }

        yield return LabelledField("ef-size", "Size:", "1");
        yield return Tick("ef-loop", "Loop", _loop, v => _loop = v);
        yield return Button("ef-go", texts["Attach effect"], AttachEffect, 200, _effect is not null);
        if (_looped.Count > 0) yield return Label("ef-lh", texts["Looped on this character:"]);
        for (int i = 0; i < _looped.Count; i++)
        {
            var looped = _looped[i];
            yield return Button("ef-un" + i, looped.Effect + "  X", () => Unloop(looped), 0);
        }
    }

    /// <summary>Plays the effect on the bone for a few seconds, or keeps it up (renewed every frame while looped) as the original's loop.</summary>
    void AttachEffect()
    {
        if (_effect is not { } effect) return;

        var anchor = EffectAnchor.Bone(_target, Bones[_bone].Bone, Vector3.Zero).Scaled(Math.Clamp(Number("ef-size", 1), 0.01f, 100));
        bool ok = _loop ? context.KeepEffect(effect.Id, anchor, 30) : context.PlayEffect(effect.Id, anchor, 3);
        if (ok && _loop) _looped.Add((effect.Id, anchor));
        _note = ok ? texts.Format("Effect %s attached to '%s'", effect.Name, Target?.Name ?? "") : texts["No matching effect records."];
    }

    /// <summary>Looped effects, renewed while the panel lives; "Loop" off and a new attach stop none (the original's unloop is per row).</summary>
    readonly List<(string Effect, EffectAnchor Anchor)> _looped = [];

    void Unloop((string Effect, EffectAnchor Anchor) looped)
    {
        context.StopEffect(looped.Effect, looped.Anchor);
        _looped.Remove(looped);
    }

    void RenewLooped()
    {
        foreach (var (effect, anchor) in _looped) context.KeepEffect(effect, anchor, 30);
    }
}
