using System;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Centralized pawn-graphics refresh for the editor.
///
/// WHY THIS EXISTS:
/// Before this helper, the "mark a pawn's graphics dirty so the portrait and the map sprite
/// re-render after an edit" sequence was copy-pasted in 8 different places (duplication,
/// blueprint load, AddPawn, the randomizers, RecacheGraphics, AppearanceInfo.CopyTo...) and they
/// had DRIFTED apart:
///   - Some called GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty, some didn't (so the pawn's
///     MAP sprite could keep the old look after a randomize, even though the portrait updated).
///   - Some ran immediately, one (RecacheGraphics) deferred via LongEventHandler.
///   - Some gated PortraitsCache.SetDirty on IsColonist, some didn't.
/// That inconsistency is exactly the kind of "why didn't the portrait update?" bug that's hard to
/// trace. Routing every refresh through ONE method means one place to fix and one behavior.
///
/// CANON (decided 2026-06-20):
///   - DEFERRED via LongEventHandler.ExecuteWhenFinished. SetAllGraphicsDirty mid-OnGUI (while the
///     renderer may be in use that frame) is technically risky; deferring to end-of-frame is the
///     safe pattern RecacheGraphics already used. The visible cost is the refresh landing ONE
///     frame later (1/60 s) — imperceptible.
///   - SetAllGraphicsDirty() ALWAYS (re-bakes the pawn's render data).
///   - PortraitsCache.SetDirty(pawn) ALWAYS (not gated on IsColonist: the editor edits pawns that
///     may not be colonists yet, and SetDirty on a pawn with no cached portrait costs ~nothing).
///   - GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn) ALWAYS (so the MAP sprite updates
///     too, fixing the randomizers that used to omit it).
///   - Everything in try/catch (a refresh failure must never break an edit).
/// </summary>
public static partial class PawnEditor
{
    /// <summary>
    /// Marks <paramref name="pawn"/>'s graphics dirty so its portrait and map sprite re-render
    /// after a visual edit (genes, body type, hair, tattoos, appearance load, duplication...).
    /// Deferred to end-of-frame and fully guarded. Call this instead of poking SetAllGraphicsDirty
    /// / PortraitsCache / GlobalTextureAtlasManager by hand.
    /// </summary>
    public static void RefreshPawnGraphics(Pawn pawn)
    {
        if (pawn == null) return;

        // PerAction banderita: a visual edit is a discrete user action, so this should fire once
        // per edit. If the profiler ever shows this as a per-frame allocator or with a wildly high
        // call count, we accidentally wired it into a draw/loop path — that's a bug to hunt.
        PawnEditorProfiler.Measure("Graphics.RefreshPawn", PawnEditorProfiler.Cadence.PerAction, () =>
        {
            // Deferred: run the actual dirtying at end-of-frame, never mid-render.
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                    PortraitsCache.SetDirty(pawn);
                    GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Pawn Editor] RefreshPawnGraphics failed for {pawn?.LabelShortCap}: {ex.Message}");
                }
            });
        });
    }
}
