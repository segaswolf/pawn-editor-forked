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
    /// Performs a CHEAP, gen-0-only collection after a heavy, one-off operation, to sweep the
    /// short-lived garbage that op just produced (reflection arg arrays, temp lists) before it
    /// gets promoted to older generations.
    ///
    /// IMPORTANT HISTORY (don't regress this): the original version called
    /// GC.Collect(MaxGeneration, ...), a FULL-heap collection. The profiler measured that at
    /// ~4.3 SECONDS on a large modlist (it was 94% of a 4.5s blueprint load), because collecting
    /// the whole heap is expensive no matter the mode — Optimized/non-blocking does NOT save you
    /// when the heap is huge. So we now collect ONLY gen 0: it reclaims the recent burst of
    /// garbage (the actual reason this helper exists) without the multi-second full-heap sweep.
    /// Gen 0 collections are designed to be fast and frequent.
    ///
    /// We intentionally do NOT force gen 1/2 here. The automatic GC handles older generations on
    /// its own schedule; forcing a full collection bought us nothing but a freeze.
    /// </summary>
    /// <param name="reason">
    /// Short label for logging if the collection itself throws (it normally won't). Helps trace
    /// which operation triggered it without adding noise on the success path.
    /// </param>
    public static void CollectAfterHeavyOp(string reason = null)
    {
        try
        {
            // Generation 0 only: cheap sweep of the just-created short-lived objects. Forced (not
            // Optimized) because we DO want gen 0 cleared now; gen 0 is small so this stays fast.
            GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        }
        catch (Exception ex)
        {
            // A failed GC is non-fatal — the automatic collector will still run eventually.
            Log.Warning($"[Pawn Editor] Controlled GC failed{(reason != null ? $" ({reason})" : "")}: {ex.Message}");
        }
    }
}
