using System.Linq;
using RimWorld;
using Verse;
using AppearanceEditor = global::PawnEditor.Dialog_AppearanceEditor;

namespace PawnEditorInGameTests;

/// <summary>
/// The appearance editor's Randomize button only lands on what the pickers offer.
/// </summary>
/// <remarks>
/// Randomness is checked as a property: every roll, many times over, must be a value the matching
/// picker lists. One lucky roll proves nothing; thirty rolls that all stay in bounds make an
/// out-of-bounds bug very unlikely to hide.
/// </remarks>
public static class AppearanceRandomizerTests
{
    private const int Rolls = 30;

    [InGameTest]
    public static void RandomBodyType_IsAlwaysOneThePickerOffers()
    {
        var pawn = TestPawns.Colonist();
        var editor = new AppearanceEditor(pawn);
        var offered = editor.BodyTypeOptions().ToList();

        for (var roll = 0; roll < Rolls; roll++)
        {
            Check.That(editor.RandomizeBodyType(), "randomizing the body type reported nothing to pick");
            Check.That(offered.Contains(pawn.story.bodyType),
                $"randomize gave {pawn.story.bodyType?.defName}, which the body picker does not offer");
        }
    }

    [InGameTest]
    public static void RandomHeadType_IsAlwaysOneThePickerOffers()
    {
        var pawn = TestPawns.Colonist();
        var editor = new AppearanceEditor(pawn);
        var offered = editor.HeadTypeOptions().ToList();

        for (var roll = 0; roll < Rolls; roll++)
        {
            Check.That(editor.RandomizeHeadType(), "randomizing the head type reported nothing to pick");
            Check.That(offered.Contains(pawn.story.headType),
                $"randomize gave {pawn.story.headType?.defName}, which the head picker does not offer");
        }
    }

    /// <summary>The bug this suite was written for: a body tattoo landing on the face, or the reverse.</summary>
    [InGameTest]
    public static void RandomTattoos_EachSlotGetsItsOwnType()
    {
        if (!ModsConfig.IdeologyActive) return; // tattoos are an Ideology feature

        var pawn = TestPawns.Colonist();
        var editor = new AppearanceEditor(pawn);

        for (var roll = 0; roll < Rolls; roll++)
        {
            Check.That(editor.RandomizeTattoos(), "randomizing tattoos reported nothing to pick");
            Check.Equal(TattooType.Face, pawn.style.FaceTattoo.tattooType, $"face slot got '{pawn.style.FaceTattoo.defName}'");
            Check.Equal(TattooType.Body, pawn.style.BodyTattoo.tattooType, $"body slot got '{pawn.style.BodyTattoo.defName}'");
        }
    }
}
