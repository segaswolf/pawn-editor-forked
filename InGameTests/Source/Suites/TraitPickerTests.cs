using System.Linq;
using PawnEditor;
using RimWorld;
using Verse;

namespace PawnEditorInGameTests;

/// <summary>The trait picker shows what a trait changes, like the Bio tooltip (request: xWOLFxz, 28 Oct).</summary>
public static class TraitPickerTests
{
    [InGameTest]
    public static void Description_ListsStatChanges()
    {
        var pawn = TestPawns.Colonist();
        var def = DefDatabase<TraitDef>.AllDefs.FirstOrDefault(t => t.degreeDatas.Any(d => !d.statOffsets.NullOrEmpty()));
        Check.NotNull(def, "a trait with stat offsets");
        var degree = def.degreeDatas.First(d => !d.statOffsets.NullOrEmpty());

        var text = ListingMenu_Trait.Describe(new ListingMenu_Trait.TraitInfo(def, degree), pawn);
        var stat = degree.statOffsets[0].stat.LabelCap.ToString();
        Check.That(text.Contains(stat), $"{def.defName} ({degree.label}): the picker text does not mention {stat}");
    }
}
