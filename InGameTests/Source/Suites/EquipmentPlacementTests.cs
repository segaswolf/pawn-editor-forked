using RimWorld;
using Verse;
using EquipmentPlacement = global::PawnEditor.EquipmentPlacement;

namespace PawnEditorInGameTests;

/// <summary>
/// The single path every editor feature uses to put equipment on a pawn.
/// </summary>
/// <remarks>
/// Only the ordinary case is covered here: the profiles have no mod that refuses equipment, and faking
/// one would need a Harmony patch inside the tests. The refusal path (Progression: Education) is
/// checked by hand.
/// </remarks>
public static class EquipmentPlacementTests
{
    [InGameTest]
    public static void Equip_AnOrdinaryWeapon_EndsUpEquipped()
    {
        var pawn = TestPawns.Colonist();
        pawn.equipment.DestroyAllEquipment();
        var knife = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife"), ThingDefOf.Steel);

        var outcome = EquipmentPlacement.Equip(pawn, knife);

        Check.Equal(EquipmentPlacement.Outcome.Equipped, outcome, "outcome");
        Check.That(pawn.equipment.Primary == knife, "the knife is not the pawn's primary weapon");
    }

    [InGameTest]
    public static void Equip_WithNothingToEquip_ReportsLostWithoutThrowing()
    {
        var pawn = TestPawns.Colonist();
        Check.DoesNotThrow(() => Check.Equal(EquipmentPlacement.Outcome.Lost, EquipmentPlacement.Equip(pawn, null), "outcome"),
            "equipping nothing");
    }
}
