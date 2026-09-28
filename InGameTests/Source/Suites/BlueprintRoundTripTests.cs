using System;
using System.IO;
using System.Linq;
using PawnEditor;
using Verse;

namespace PawnEditorInGameTests;

/// <summary>
/// Contract of blueprint save and load: what goes into a file comes back out the same, and a file
/// that is missing or broken never produces a half-built pawn.
///
/// Round trips are the class of test that would have caught most of the saved-state bugs in
/// BUG_LEDGER.md before any player did.
/// </summary>
public static class BlueprintRoundTripTests
{
    [InGameTest]
    public static void RoundTrip_KeepsTraits()
    {
        var original = TestPawns.Colonist();

        var loaded = RoundTrip(original, nameof(RoundTrip_KeepsTraits));

        Check.Equal(DuplicationTests.TraitSignature(original), DuplicationTests.TraitSignature(loaded), "traits");
    }

    [InGameTest]
    public static void RoundTrip_KeepsSkillLevels()
    {
        var original = TestPawns.Colonist();

        var loaded = RoundTrip(original, nameof(RoundTrip_KeepsSkillLevels));

        Check.Equal(SkillSignature(original), SkillSignature(loaded), "skill levels");
    }

    [InGameTest]
    public static void RoundTrip_KeepsForcedApparel()
    {
        var (original, forced) = TestPawns.ColonistWithForcedApparel();

        var loaded = RoundTrip(original, nameof(RoundTrip_KeepsForcedApparel));

        var match = loaded.apparel?.WornApparel.FirstOrDefault(worn => worn.def == forced.def);
        Check.NotNull(match, $"the loaded pawn is not wearing {forced.def.defName}");
        // Separate check so the report says WHICH problem it is: no tracker means "forced" cannot even
        // be represented on this pawn, which is a different bug from "it was there and got lost".
        Check.NotNull(loaded.outfits?.forcedHandler, "the loaded pawn has no outfit tracker, so forced cannot be stored");
        Check.That(TestPawns.IsForced(loaded, match), "the forced flag did not survive the blueprint");
    }

    [InGameTest]
    public static void Load_MissingFile_DoesNotProduceAPawn()
    {
        AssertLoadingFailsCleanly(TestPawns.TempFile("does-not-exist.xml"), "a missing file");
    }

    [InGameTest]
    public static void Load_CorruptFile_DoesNotProduceAPawn()
    {
        var path = TestPawns.TempFile("corrupt.xml");
        File.WriteAllText(path, "this is <<< not a blueprint");

        AssertLoadingFailsCleanly(path, "a corrupt file");
    }

    private static Pawn RoundTrip(Pawn pawn, string fileName)
    {
        var path = TestPawns.TempFile(fileName + ".xml");
        PawnBlueprintSaveLoad.SaveBlueprint(pawn, path);
        Check.That(File.Exists(path), "SaveBlueprint did not write the file");

        var loaded = PawnBlueprintSaveLoad.LoadBlueprint(path);
        Check.NotNull(loaded, "LoadBlueprint returned nothing for a file it had just written");
        return loaded;
    }

    /// <summary>
    /// Current contract: either a null result or an exception is acceptable, as long as no pawn comes
    /// out. Whether the exception should reach the caller is an open decision; this pins what matters
    /// today, which is never handing back a half-built pawn.
    /// </summary>
    private static void AssertLoadingFailsCleanly(string path, string what)
    {
        Pawn result;
        try
        {
            result = PawnBlueprintSaveLoad.LoadBlueprint(path);
        }
        catch (Exception)
        {
            return;
        }

        Check.That(result == null, $"loading {what} produced a pawn");
    }

    private static string SkillSignature(Pawn pawn) =>
        string.Join(", ", pawn.skills.skills
            .Select(skill => $"{skill.def.defName}:{skill.Level}")
            .OrderBy(entry => entry));
}
