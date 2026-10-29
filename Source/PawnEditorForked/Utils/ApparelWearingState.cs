using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// The part of wearing an item that RimWorld forgets when the item comes off: whether it was
/// <b>locked</b>, and whether it was <b>forced</b> (the player told the pawn to keep it on).
///
/// WHY THIS EXISTS
/// <c>Pawn_ApparelTracker.Remove</c> clears the forced flag, and <c>Wear</c> puts nothing back unless
/// told to. Every path in this mod that takes apparel off and puts it (or a copy of it) back on has to
/// carry that state across by hand — and each path that did it by hand got it wrong at least once:
///
///   - selecting an item in the Gear tab un-forced it (fixed in v3.2.1),
///   - editing an item un-forced it again, through a different path (reported after that fix),
///   - duplicating a pawn and pasting apparel both copied "locked" but silently dropped "forced",
///   - blueprints saved "locked" and never "forced" at all.
///
/// Five paths, one bug. See BUG_LEDGER.md, class A: state not restored.
///
/// THE RULE
/// Whenever apparel stands in for a piece the pawn wore before, capture the state with
/// <see cref="Capture"/> <b>before</b> anything is removed, and put it on with <see cref="WearOn"/>.
/// That keeps one place in the code that knows what "the same wearing state" means, so a future flag
/// only has to be added here.
///
/// Capture MUST happen before removal: once <c>Remove</c> has run, the forced flag is already gone.
/// </summary>
public readonly struct ApparelWearingState
{
    public bool Locked { get; }
    public bool Forced { get; }

    public ApparelWearingState(bool locked, bool forced)
    {
        Locked = locked;
        Forced = forced;
    }

    /// <summary>Neither locked nor forced: the state of apparel the pawn never wore before.</summary>
    public static ApparelWearingState None => new(false, false);

    /// <summary>
    /// Reads the current state of <paramref name="apparel"/> on <paramref name="pawn"/>. Call this
    /// before removing the item, never after.
    /// </summary>
    public static ApparelWearingState Capture(Pawn pawn, Apparel apparel)
    {
        if (pawn?.apparel == null || apparel == null) return None;

        var forced = pawn.outfits?.forcedHandler?.IsForced(apparel) == true;
        return new ApparelWearingState(pawn.apparel.IsLocked(apparel), forced);
    }

    /// <summary>
    /// Puts <paramref name="apparel"/> on <paramref name="pawn"/> with this state. Never drops what it
    /// replaces: callers in this mod have already decided what happens to the previous item.
    /// </summary>
    public void WearOn(Pawn pawn, Apparel apparel)
    {
        if (pawn?.apparel == null || apparel == null) return;

        pawn.apparel.Wear(apparel, dropReplacedApparel: false, locked: Locked);

        // Pawns without an outfit tracker (some modded races, animals in apparel mods) simply have
        // nowhere to record "forced"; there is nothing to lose in that case.
        if (Forced) pawn.outfits?.forcedHandler?.SetForced(apparel, true);
    }
}
