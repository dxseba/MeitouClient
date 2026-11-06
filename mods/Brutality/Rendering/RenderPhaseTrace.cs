using System.Diagnostics;

namespace Meitou.Brutality.Rendering;

internal enum RenderPhase { Geometry, Wounds, Masks, Weapons, Effects, Impacts, SplinterPrep, Bowels }

/// <summary>Opt-in CPU timings for the mod's draw-list phases. Effects includes impacts and bowels.</summary>
internal static class RenderPhaseTrace
{
    static readonly bool enabled = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_PHASES") == "1";
    [ThreadStatic] static long[]? elapsed;

    internal static long Begin()
    {
        if (!enabled) return 0;
        Array.Clear(elapsed ??= new long[(int)RenderPhase.Bowels + 1]);
        return Stopwatch.GetTimestamp();
    }

    internal static long Start() => enabled ? Stopwatch.GetTimestamp() : 0;

    internal static void End(RenderPhase phase, long start)
    {
        if (start != 0 && elapsed is not null) elapsed[(int)phase] += Stopwatch.GetTimestamp() - start;
    }

    internal static void Report(long tick, long start)
    {
        if (start == 0 || elapsed is not { } timings) return;
        double total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (total < 10) return;
        double Ms(RenderPhase phase) => timings[(int)phase] * 1000d / Stopwatch.Frequency;
        Console.WriteLine($"Brutality render phases: tick={tick} total={total:F2} geometry={Ms(RenderPhase.Geometry):F2} wounds={Ms(RenderPhase.Wounds):F2} masks={Ms(RenderPhase.Masks):F2} weapons={Ms(RenderPhase.Weapons):F2} effects={Ms(RenderPhase.Effects):F2} impacts={Ms(RenderPhase.Impacts):F2} splinterPrep={Ms(RenderPhase.SplinterPrep):F2} bowels={Ms(RenderPhase.Bowels):F2} ms");
    }
}
