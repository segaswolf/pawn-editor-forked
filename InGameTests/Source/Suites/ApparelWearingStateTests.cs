using PawnEditor;
using RimWorld;
using Verse;

namespace PawnEditorInGameTests;

/// <summary>
/// Contract of <see cref="ApparelWearingState"/>: capture before removal, one way to put it back on.
/// It exists because five separate code paths lost the forced flag (BUG_LEDGER, class A).
/// </summary>
public static class ApparelWearingStateTests
{
    [InGameTest]
    public static void Premise_RemovingApparelClearsForced()
    {
        // Not a test of our code: a test of the assumption our code is built on. If RimWorld ever stops
        // clearing "forced" on removal, ApparelWearingState is solving a problem that no longer exists,
        // and this is where we would find out.
        var (pawn, apparel) = TestPawns.ColonistWithForcedApparel();

        pawn.apparel.Remove(apparel);

        Check.That(!TestPawns.IsForced(pawn, apparel),
            "RimWorld no longer clears forced on removal - the premise of ApparelWearingState changed");
    }

    [InGameTest]
    public static void Capture_OfForcedApparel_ReportsItForced()
    {
        var (pawn, apparel) = TestPawns.ColonistWithForcedApparel();

        var state = ApparelWearingState.Capture(pawn, apparel);

        Check.That(state.Forced, "a forced item was captured as not forced");
    }

    [InGameTest]
    public static void Capture_WithMissingInputs_ReturnsNothing()
    {
        var (pawn, apparel) = TestPawns.ColonistWithForcedApparel();

        Check.That(!ApparelWearingState.Capture(null, apparel).Forced, "a null pawn captured some state");
        Check.That(!ApparelWearingState.Capture(pawn, null).Forced, "a null item captured some state");
    }

    [InGameTest]
    public static void WearOn_ACopyOfAForcedItem_KeepsItForced()
    {
        // The exact shape of the editing bug: capture from the original, remove it, wear a COPY.
        var (pawn, apparel) = TestPawns.ColonistWithForcedApparel();
        var state = ApparelWearingState.Capture(pawn, apparel);
        pawn.apparel.Remove(apparel);

        var copy = (Apparel)ThingMaker.MakeThing(apparel.def, apparel.Stuff);
        state.WearOn(pawn, copy);

        Check.That(pawn.apparel.Contains(copy), "the copy was not put on");
        Check.That(TestPawns.IsForced(pawn, copy), "the copy lost the forced flag");
    }

    [InGameTest]
    public static void WearOn_PawnWithoutApparelTracker_DoesNothingAndDoesNotThrow()
    {
        var animal = TestPawns.Animal();
        Check.That(animal.apparel == null, "precondition: expected a pawn without an apparel tracker");

        Check.DoesNotThrow(() => new ApparelWearingState(locked: true, forced: true).WearOn(animal, TestPawns.NewShirt()),
            "wearing apparel on a pawn that cannot wear apparel");
    }
}
