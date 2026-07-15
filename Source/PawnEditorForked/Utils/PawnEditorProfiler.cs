using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace PawnEditor;

/// <summary>
/// Lightweight, opt-in profiler for Pawn Editor ("the flags / banderitas").
///
/// WHY THIS EXISTS:
/// The editor's worst problem is not slow methods, it is GARBAGE: code that re-allocates lists,
/// HashSets, strings and runs reflection every single frame. That garbage piles up until .NET's
/// GC fires a multi-second stop-the-world collection, which drops the GUI texture atlas and
/// blacks out the editor. A plain stopwatch hides this — a method can finish in 1ms yet allocate
/// megabytes per frame. So this profiler measures BOTH time AND bytes allocated, and separates
/// "once" / "per-frame" / "per-action" events, so we can see exactly what generates pressure and
/// where, with numbers instead of guesses.
///
/// DESIGN:
/// - OFF by default. Every entry point early-outs when PawnEditorMod.Settings.ProfilingEnabled is
///   false, so it costs nothing during normal play. Turn it on in mod settings only while
///   diagnosing.
/// - Accumulates per-event stats (call count, total/peak time, total/peak bytes) instead of
///   spamming a line per call. Dump a sorted summary on demand with <see cref="DumpSummary"/>.
/// - Per-frame events are flagged so the summary can show "this runs every frame" — the single
///   most useful signal for finding the garbage source.
///
/// This is the first of Pawn Editor's own internal helper libraries. Keep it self-contained and
/// dependency-free (only Verse for logging) so it can profile anything in the mod.
/// </summary>
public static class PawnEditorProfiler
{
    /// <summary>Master switch. Requires BOTH Dev Mode and the opt-in setting, so it can never run for
    /// regular players (the setting UI is Dev-Mode-only too). Defaults to false if settings aren't ready.</summary>
    public static bool Enabled => Prefs.DevMode && (PawnEditorMod.Settings?.ProfilingEnabled ?? false);

    /// <summary>How often an event is expected to fire. Drives interpretation of the numbers.</summary>
    public enum Cadence
    {
        /// <summary>Runs once (startup, opening a window). A few ms here is fine.</summary>
        Once,
        /// <summary>Runs on a discrete user action (click, select pawn). Should be cheap.</summary>
        PerAction,
        /// <summary>Runs every frame while something is on screen. MUST allocate near-zero.</summary>
        PerFrame
    }

    private sealed class Stat
    {
        public string Name;
        public Cadence Cadence;
        public long Calls;
        public double TotalMs;
        public double PeakMs;
        public long TotalBytes;
        public long PeakBytes;
    }

    private static readonly Dictionary<string, Stat> Stats = new();

    /// <summary>
    /// Measures one execution of <paramref name="action"/>, recording elapsed time and bytes
    /// allocated under <paramref name="name"/>. No-op when profiling is disabled.
    ///
    /// Use a stable, descriptive name (e.g. "Proficiencies.GetLearnable") so repeated calls
    /// accumulate into the same bucket. Mark the cadence so the summary can flag per-frame
    /// allocators, which are the ones that cause the black screen.
    /// </summary>
    public static void Measure(string name, Cadence cadence, Action action)
    {
        if (!Enabled)
        {
            action();
            return;
        }

        // GetTotalMemory(false): cheap read of the allocated heap size without forcing a GC.
        // The delta before/after approximates how much this action allocated. It can be noisy
        // if a GC happens to run mid-measure (delta could go negative); we clamp at zero.
        var memBefore = GC.GetTotalMemory(false);
        var sw = Stopwatch.StartNew();
        try
        {
            action();
        }
        finally
        {
            sw.Stop();
            var memAfter = GC.GetTotalMemory(false);
            Record(name, cadence, sw.Elapsed.TotalMilliseconds, Math.Max(0, memAfter - memBefore));
        }
    }

    /// <summary>
    /// Same as <see cref="Measure(string,Cadence,Action)"/> but for a function returning a value.
    /// </summary>
    public static T Measure<T>(string name, Cadence cadence, Func<T> func)
    {
        if (!Enabled) return func();

        var memBefore = GC.GetTotalMemory(false);
        var sw = Stopwatch.StartNew();
        try
        {
            return func();
        }
        finally
        {
            sw.Stop();
            var memAfter = GC.GetTotalMemory(false);
            Record(name, cadence, sw.Elapsed.TotalMilliseconds, Math.Max(0, memAfter - memBefore));
        }
    }

    private static void Record(string name, Cadence cadence, double ms, long bytes)
    {
        if (!Stats.TryGetValue(name, out var s))
        {
            s = new Stat { Name = name, Cadence = cadence };
            Stats[name] = s;
        }

        s.Calls++;
        s.TotalMs += ms;
        if (ms > s.PeakMs) s.PeakMs = ms;
        s.TotalBytes += bytes;
        if (bytes > s.PeakBytes) s.PeakBytes = bytes;
    }

    /// <summary>Clears all accumulated stats. Call before a fresh measurement run.</summary>
    public static void Reset()
    {
        Stats.Clear();
        Log.Message("[PE-PROF] stats reset");
    }

    /// <summary>
    /// Logs a summary of all recorded events, sorted by total bytes allocated (the metric that
    /// matters most for the black-screen problem). Per-frame events that allocate are flagged
    /// loudly because those are the ones to fix first. No-op when disabled or empty.
    /// </summary>
    public static void DumpSummary()
    {
        if (!Enabled) return;
        if (Stats.Count == 0)
        {
            Log.Message("[PE-PROF] no stats recorded yet");
            return;
        }

        var ordered = Stats.Values.OrderByDescending(s => s.TotalBytes).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[PE-PROF] ===== Pawn Editor profiler summary (sorted by total bytes) =====");
        sb.AppendLine("[PE-PROF] event | cadence | calls | totalMs | peakMs | totalKB | peakKB | KB/call");
        foreach (var s in ordered)
        {
            var kbTotal = s.TotalBytes / 1024.0;
            var kbPeak = s.PeakBytes / 1024.0;
            var kbPerCall = s.Calls > 0 ? kbTotal / s.Calls : 0;

            // Loud flag: a per-frame event that allocates is the prime suspect for GC pressure.
            var flag = s.Cadence == Cadence.PerFrame && s.TotalBytes > 0 ? "  <-- PER-FRAME ALLOCATOR" : "";

            sb.AppendLine($"[PE-PROF] {s.Name} | {s.Cadence} | {s.Calls} | " +
                          $"{s.TotalMs:F1} | {s.PeakMs:F1} | {kbTotal:F1} | {kbPeak:F1} | {kbPerCall:F2}{flag}");
        }
        sb.AppendLine("[PE-PROF] ================================================================");
        Log.Message(sb.ToString());
    }
}
