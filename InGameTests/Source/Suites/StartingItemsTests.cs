using System.Linq;
using RimWorld;
using Verse;
using ItemsMenu = global::PawnEditor.ListingMenu_Items;

namespace PawnEditorInGameTests;

/// <summary>
/// The starting resources picker reflects the defs as they are when it opens, not as they were at
/// startup.
/// </summary>
/// <remarks>
/// Reported with Combat Extended ammo: CE turns off <c>destroyOnDrop</c> on its ammo after our static
/// constructors have run, so a list cached at startup never offered it. These tests flip that same
/// flag on a vanilla item and check the picker follows, which needs no CE to reproduce.
/// </remarks>
public static class StartingItemsTests
{
    [InGameTest]
    public static void StartingPicker_FollowsDestroyOnDropChangedAfterStartup()
    {
        var steel = ThingDefOf.Steel;
        var original = steel.destroyOnDrop;
        try
        {
            steel.destroyOnDrop = true;
            Check.That(!ItemsMenu.StartingCandidates().Contains(steel),
                "steel with destroyOnDrop on was still offered as a starting resource");

            steel.destroyOnDrop = false;
            Check.That(ItemsMenu.StartingCandidates().Contains(steel),
                "steel became eligible after startup, but the starting picker did not offer it");
        }
        finally
        {
            steel.destroyOnDrop = original;
        }
    }

    [InGameTest]
    public static void StartingPicker_OffersCommonVanillaResources()
    {
        var offered = ItemsMenu.StartingCandidates();
        foreach (var def in new[] { ThingDefOf.Steel, ThingDefOf.ComponentIndustrial, ThingDefOf.MedicineIndustrial })
            Check.That(offered.Contains(def), $"{def.defName} is not offered as a starting resource");
    }
}
