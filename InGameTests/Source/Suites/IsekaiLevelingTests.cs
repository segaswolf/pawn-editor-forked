using System.IO;
using System.Linq;
using System.Reflection;
using PawnEditor;
using RimWorld;
using Verse;
using EditorApi = global::PawnEditor.PawnEditor;

namespace PawnEditorInGameTests;

/// <summary>
/// Isekai RPG Leveling keeps level, stats and constellation in a ThingComp, and derives the rank trait
/// from the level. Duplicating, blueprints and the trait reroll must carry or respect all of it.
/// Skipped when the mod is not loaded (run with -Mods Compat or Modpack).
/// </summary>
public static class IsekaiLevelingTests
{
    private const string RankPrefix = "Isekai_Rank_";

    [InGameTest]
    public static void Duplicate_KeepsIsekaiState()
    {
        var original = AuthoredColonist();

        var duplicate = EditorApi.CreateStableDuplicateOrSelf(original);
        Check.That(!ReferenceEquals(duplicate, original), "duplication fell back to returning the original pawn");

        Check.Equal(Signature(original), Signature(duplicate), "Isekai state");
        Check.Equal(RankTrait(original), RankTrait(duplicate), "rank trait");
    }

    [InGameTest]
    public static void RoundTrip_KeepsIsekaiState()
    {
        var original = AuthoredColonist();
        var path = TestPawns.TempFile(nameof(RoundTrip_KeepsIsekaiState) + ".xml");

        PawnBlueprintSaveLoad.SaveBlueprint(original, path);
        Check.That(File.Exists(path), "SaveBlueprint did not write the file");
        var loaded = PawnBlueprintSaveLoad.LoadBlueprint(path);
        Check.NotNull(loaded, "LoadBlueprint");

        Check.Equal(Signature(original), Signature(loaded), "Isekai state");
        Check.Equal(RankTrait(original), RankTrait(loaded), "rank trait");
    }

    [InGameTest]
    public static void RandomizeTraits_KeepsRankTrait()
    {
        var pawn = AuthoredColonist();
        var before = RankTrait(pawn);
        Check.NotNull(before, "rank trait before the reroll");

        var reroll = typeof(TabWorker_Bio_Humanlike).GetMethod("RandomizeTraits", BindingFlags.NonPublic | BindingFlags.Static);
        Check.NotNull(reroll, "TabWorker_Bio_Humanlike.RandomizeTraits (renamed?)");
        reroll.Invoke(null, new object[] { pawn, false });

        Check.Equal(before, RankTrait(pawn), "rank trait after Randomize > Traits");
    }

    /// <summary>A colonist with a known, non-default Isekai state, set through the compat itself.</summary>
    private static Pawn AuthoredColonist()
    {
        Check.Requires(IsekaiLevelingCompat.Available, "Isekai RPG Leveling not loaded");
        var pawn = TestPawns.Colonist();
        var state = IsekaiLevelingCompat.Capture(pawn);
        Check.NotNull(state, "Isekai state of a fresh colonist");

        state.Level = 120;
        state.Xp = 33;
        int[] stats = { 40, 30, 20, 10, 15, 5 };
        stats.CopyTo(state.Stats, 0);
        state.StatPoints = 7;
        state.TreePoints = 3;
        state.Locked = false;
        IsekaiLevelingCompat.Apply(pawn, state);
        return pawn;
    }

    private static string Signature(Pawn pawn)
    {
        var s = IsekaiLevelingCompat.Capture(pawn);
        if (s == null) return "no Isekai component";
        return $"level {s.Level}, xp {s.Xp}, stats [{string.Join(",", s.Stats)}], points {s.StatPoints}, " +
               $"tree '{s.Tree}' ({s.Nodes.Count} nodes, {s.TreePoints} points), locked {s.Locked}";
    }

    private static string RankTrait(Pawn pawn) =>
        pawn.story?.traits?.allTraits
            .Select(trait => trait.def.defName)
            .FirstOrDefault(defName => defName.StartsWith(RankPrefix));
}
