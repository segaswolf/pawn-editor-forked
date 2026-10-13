using System.Linq;
using RimWorld;
using Verse;
using AppearanceEditor = global::PawnEditor.Dialog_AppearanceEditor;
using BodyTypeRules = global::PawnEditor.BodyTypeRules;

namespace PawnEditorInGameTests;

/// <summary>
/// One set of body type rules for every picker, and a data-driven notion of "special" body types.
/// </summary>
/// <remarks>
/// A false positive would label an ordinary body type as a mod oddity and hide it from Randomize,
/// so the first test checks every body type from official content. The profiles have no mod that adds
/// a special one, so the second test builds an unreferenced def by hand.
/// </remarks>
public static class BodyTypeRulesTests
{
    [InGameTest]
    public static void OfficialBodyTypes_AreNeverSpecial()
    {
        var flagged = DefDatabase<BodyTypeDef>.AllDefs
            .Where(bodyType => bodyType.modContentPack?.IsOfficialMod == true && BodyTypeRules.IsSpecial(bodyType))
            .Select(bodyType => bodyType.defName)
            .ToList();

        Check.That(flagged.Count == 0, $"official body types flagged as mod special: {string.Join(", ", flagged)}");
    }

    [InGameTest]
    public static void BodyTypeNoBackstoryUses_IsSpecial()
    {
        var unreferenced = new BodyTypeDef { defName = "PawnEditorTests_UnreferencedBody" };
        Check.That(BodyTypeRules.IsSpecial(unreferenced), "a body type no backstory names was not flagged as special");
    }

    /// <summary>The Bio tab's Shape button and the appearance editor's Body grid offer the same set.</summary>
    [InGameTest]
    public static void ShapeButtonAndBodyGrid_OfferTheSameBodyTypes()
    {
        var pawn = TestPawns.Colonist();
        var shapeButton = BodyTypeRules.For(pawn).ToHashSet();
        var bodyGrid = new AppearanceEditor(pawn).BodyTypeOptions().ToHashSet();

        Check.That(shapeButton.SetEquals(bodyGrid),
            $"Shape button offers [{string.Join(", ", shapeButton.Select(b => b.defName))}] " +
            $"but the Body grid offers [{string.Join(", ", bodyGrid.Select(b => b.defName))}]");
    }
}
