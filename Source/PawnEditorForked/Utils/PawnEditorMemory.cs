using System;
using Verse;

namespace PawnEditor;

/// <summary>
/// Centralized memory-cleanup helper for heavy, one-off editor operations.
///
/// WHY THIS EXISTS:
/// Operations like duplicating a pawn, loading a blueprint, or (in the future) editing faces
/// generate a burst of short-lived objects — reflection argument arrays, temporary lists, new
/// portrait RenderTextures, etc. On large modlists the heap is already big, so .NET's automatic
/// GC tends to fire a long stop-the-world collection a few seconds LATER, at an unpredictable
/// moment. During that pause Unity can drop the GUI texture atlas, blacking out the whole
/// interface until the next redraw. Forcing a controlled collection right after the heavy op
/// turns that into one short, predictable hitch instead.
///
/// HOW TO USE:
/// Call <see cref="CollectAfterHeavyOp"/> ONCE at the end of a heavy, infrequent operation.
///
/// WHAT NOT TO DO (important — GC.Collect is expensive, it's a full stop-the-world pause):
/// - NEVER call this inside a loop. Group the work, then collect once after the loop.
/// - NEVER call this from per-frame code (DoWindowContents, OnGUI, Tick, Draw, etc.).
/// - NEVER call this for light operations (adding a single trait, toggling a checkbox).
/// It is ONLY for genuinely heavy, user-initiated, infrequent actions.
///
/// As the mod grows (e.g. the facial-animation editor), route those heavy ops through here so
/// the collection policy lives in ONE place and can be tuned globally.
/// </summary>
public static class PawnEditorMemory
{
    /// <summary>
    /// Requests a NON-BLOCKING, optimized garbage collection hint after a heavy, one-off op.
    ///
    /// IMPORTANT HISTORY (measured, don't regress):
    /// - v1 called GC.Collect(MaxGeneration, ...): profiler measured ~4.3s (full-heap sweep).
    /// - v2 changed to GC.Collect(0, Forced, blocking:true) assuming "gen-0 is cheap". The
    ///   profiler (banderita Load.CollectGen0) then measured that at ~3.9s while freeing 0 KB.
    ///   The lesson: blocking:true is stop-the-world REGARDLESS of generation; on a huge modlist
    ///   heap even a gen-0 blocking collect takes seconds, and it reclaimed nothing useful.
    /// - v3 (now): removed the blocking forced collect from the load path entirely (it cost
    ///   seconds and freed nothing). This helper is kept as a NON-BLOCKING hint only, for callers
    ///   that still want to nudge the GC without freezing the main thread. It may do nothing if
    ///   the runtime decides a collection isn't worthwhile — which is fine; the automatic GC will
    ///   reclaim on its own schedule.
    ///
    /// If you need memory reclaimed, prefer reducing allocations over forcing collections. Forcing
    /// a blocking GC to "clean up now" has repeatedly cost multi-second freezes for no measured
    /// benefit on this heap.
    /// </summary>
    /// <param name="reason">Short label for logging if the collection itself throws.</param>
    public static void CollectAfterHeavyOp(string reason = null)
    {
        try
        {
            // NON-BLOCKING + Optimized: a hint, not a stop-the-world. The runtime may skip it if
            // it judges a collection unnecessary. This is deliberately weak: the profiler proved
            // that a BLOCKING collect here costs seconds and frees nothing on large heaps.
            GC.Collect(0, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            // A failed GC is non-fatal — the automatic collector will still run eventually.
            Log.Warning($"[Pawn Editor] Controlled GC failed{(reason != null ? $" ({reason})" : "")}: {ex.Message}");
        }
    }
}
