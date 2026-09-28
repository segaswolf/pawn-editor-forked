using System.Linq;
using Verse;
using EditorApi = global::PawnEditor.PawnEditor;

namespace PawnEditorInGameTests;

/// <summary>
/// Contract of <c>PawnEditor.CreateStableDuplicateOrSelf</c>.
///
/// Note the "OrSelf": when duplication fails it returns the ORIGINAL pawn instead of throwing. A test
/// that only compared the result with the source would then compare the pawn with itself and pass
/// every time. <see cref="DuplicateOf"/> refuses that case, so no test here can pass by accident.
/// </summary>
public static class DuplicationTests
{
    [InGameTest]
    public static void Duplicate_OfNull_ReturnsNull()
    {
        Check.That(EditorApi.CreateStableDuplicateOrSelf(null) == null, "duplicating nothing produced a pawn");
    }

    [InGameTest]
    public static void Duplicate_ReturnsAnotherPawn_NotTheOriginal()
    {
        DuplicateOf(TestPawns.Colonist());
    }

    [InGameTest]
    public static void Duplicate_KeepsTraits()
    {
        var original = TestPawns.Colonist();

        var duplicate = DuplicateOf(original);

        Check.Equal(TraitSignature(original), TraitSignature(duplicate), "traits");
    }

    [InGameTest]
    public static void Duplicate_KeepsForcedApparel()
    {
        var (original, forced) = TestPawns.ColonistWithForcedApparel();

        var duplicate = DuplicateOf(original);

        var match = duplicate.apparel?.WornApparel.FirstOrDefault(worn => worn.def == forced.def);
        Check.NotNull(match, $"the duplicate is not wearing {forced.def.defName}");
        Check.That(TestPawns.IsForced(duplicate, match), "the duplicate's copy lost the forced flag");
    }

    private static Pawn DuplicateOf(Pawn original)
    {
        var duplicate = EditorApi.CreateStableDuplicateOrSelf(original);
        Check.NotNull(duplicate, "duplicate");
        Check.That(!ReferenceEquals(duplicate, original),
            "duplication failed and fell back to returning the original pawn");
        return duplicate;
    }

    /// <summary>Order-independent text form of a pawn's traits, so two pawns compare by content.</summary>
    internal static string TraitSignature(Pawn pawn) =>
        string.Join(", ", pawn.story.traits.allTraits
            .Select(trait => $"{trait.def.defName}:{trait.Degree}")
            .OrderBy(entry => entry));
}
