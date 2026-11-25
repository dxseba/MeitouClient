using Meitou.Data.Fcs;
using Meitou.Modding;

namespace Meitou.QuestsContracts;

/// <summary>
/// Rebuilds BFrizz's Extra Extensions variable tests and the Ledger's signal lines on Meitou's dialogue codes, in the data phase:
/// <list type="bullet">
/// <item>a line's <c>variable equals</c> / <c>variable less than</c> / <c>variable greater than</c> reference to a variable record (type 1000) with
/// value0 N becomes a <c>conditions</c> entry: a DIALOG_ACTION of condition code <see cref="VariableCode"/> + the variable's index and compare by
/// 0 / 1 / 2, with value N; the line's <c>score bonus</c> is lowered by as many, so the scores stay the original engine's;</item>
/// <item>each signal line gets an <c>effects</c> entry of action <see cref="SignalCode"/> whose value is the signal's index.</item>
/// </list>
/// </summary>
static class DataBridge
{
    public const int VariableCode = 7300;
    public const int SignalCode = 7290;
    const string Prefix = "dxseba.questscontracts.";

    static readonly (string List, int CompareBy)[] Tests = [("variable equals", 0), ("variable less than", 1), ("variable greater than", 2)];

    public static void Apply(ModDataContext context, LedgerTables tables)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < tables.Variables.Count; i++) index.TryAdd(tables.Variables[i].Record, i);

        var signalOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < tables.Signals.Count; i++) signalOf.TryAdd(tables.Signals[i].Line, i);

        var made = new HashSet<string>(StringComparer.Ordinal);
        int lines = 0;
        foreach (var line in context.Data.OfType(FcsRecordType.DialogueLine).ToList())
        {
            if (Rewrite(context, line, index, signalOf, made)) lines++;
        }

        context.Log($"Extra Extensions bridge: {lines} lines rewritten, {made.Count} variable tests, {tables.Signals.Count} signals");
    }

    static bool Rewrite(ModDataContext context, Data.GameRecord line, Dictionary<string, int> index, Dictionary<string, int> signalOf,
        HashSet<string> made)
    {
        var conditions = new List<FcsReference>();
        foreach (var (list, compareBy) in Tests)
        {
            foreach (var r in line.GetReferences(list))
            {
                if (!index.TryGetValue(r.TargetStringId, out int v)) continue;

                conditions.Add(new FcsReference(TestRecord(context, v, compareBy, made), r.Values.Value0, 0, 0));
            }
        }

        bool signal = signalOf.TryGetValue(line.StringId, out int s);
        if (conditions.Count == 0 && !signal) return false;

        var edit = context.Edit(line.StringId);
        if (conditions.Count > 0)
        {
            edit.References["conditions"] = conditions;
            edit.Ints["score bonus"] = line.GetInt("score bonus") - conditions.Count;
        }

        if (signal) edit.References["effects"] = [new FcsReference(SignalRecord(context, made), s, 0, 0)];
        return true;
    }

    static string TestRecord(ModDataContext context, int variable, int compareBy, HashSet<string> made)
    {
        string id = $"{Prefix}var{variable}.{compareBy}";
        if (!made.Add(id)) return id;

        var r = context.Add(id, FcsRecordType.DialogAction, $"Ledger variable {variable} test {compareBy}");
        r.Ints["condition name"] = VariableCode + variable;
        r.Ints["compare by"] = compareBy;
        r.Ints["action name"] = 0;
        r.Ints["who"] = 0;
        r.Ints["tag"] = -1;
        return id;
    }

    static string SignalRecord(ModDataContext context, HashSet<string> made)
    {
        string id = Prefix + "signal";
        if (!made.Add(id)) return id;

        var r = context.Add(id, FcsRecordType.DialogAction, "Ledger signal");
        r.Ints["action name"] = SignalCode;
        r.Ints["condition name"] = 0;
        r.Ints["tag"] = -1;
        return id;
    }
}
