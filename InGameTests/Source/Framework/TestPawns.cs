using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditorInGameTests;

/// <summary>
/// Builds the pawns and items the tests act on. Every test gets fresh ones, so no test can pass or
/// fail because of what another test did before it.
/// </summary>
public static class TestPawns
{
    private const string ShirtDefName = "Apparel_BasicShirt";

    public static Pawn Colonist() =>
        PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            forceGenerateNewPawn: true));

    /// <summary>A pawn with no apparel tracker at all: the "missing input" case for anything about clothes.</summary>
    public static Pawn Animal() =>
        PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.AllDefs.First(kind => kind.RaceProps?.Animal == true));

    /// <summary>A colonist wearing at least one item, with that item forced.</summary>
    public static (Pawn pawn, Apparel forced) ColonistWithForcedApparel()
    {
        var pawn = Colonist();
        var apparel = pawn.apparel.WornApparel.FirstOrDefault() ?? PutOn(pawn, NewShirt());
        pawn.outfits.forcedHandler.SetForced(apparel, true);
        return (pawn, apparel);
    }

    public static Apparel NewShirt() =>
        (Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(ShirtDefName), ThingDefOf.Cloth);

    public static string TempFile(string fileName) => Path.Combine(TestRunner.WorkFolder, fileName);

    public static bool IsForced(Pawn pawn, Apparel apparel) =>
        pawn?.outfits?.forcedHandler?.IsForced(apparel) == true;

    private static Apparel PutOn(Pawn pawn, Apparel apparel)
    {
        pawn.apparel.Wear(apparel, dropReplacedApparel: false);
        return apparel;
    }
}
