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
    /// Forces a controlled garbage collection after a heavy, one-off operation, so the cleanup
    /// happens now (one short hitch) instead of as an unpredictable long pause seconds later.
    ///
    /// Uses an optimized, non-blocking collection where the runtime allows it, falling back to
    /// a standard collection. Safe to call even if Life Lessons or other mods aren't present —
    /// it only touches the runtime GC, never mod state.
    /// </summary>
    /// <param name="reason">
    /// Short label for logging if the collection itself throws (it normally won't). Helps trace
    /// which operation triggered it without adding noise on the success path.
    /// </param>
    public static void CollectAfterHeavyOp(string reason = null)
    {
        try
        {
            // GCCollectionMode.Optimized lets the runtime skip the collection if it judges it
            // unnecessary, avoiding a needless pause; it still collects when there's real pressure.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
        }
        catch (Exception ex)
        {
            // A failed GC is non-fatal — the automatic collector will still run eventually.
            Log.Warning($"[Pawn Editor] Controlled GC failed{(reason != null ? $" ({reason})" : "")}: {ex.Message}");
        }
    }
}
