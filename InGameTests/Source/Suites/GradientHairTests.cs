using System.IO;
using System.Linq;
using PawnEditor;
using UnityEngine;
using Verse;
using EditorApi = global::PawnEditor.PawnEditor;

namespace PawnEditorInGameTests;

/// <summary>
/// Gradient Hair: enabled flag, second color and pattern (mask) must survive duplicating and blueprints.
/// Also answers whether Gradient Hair loads at all on the running game version.
/// Skipped when the mod is not loaded.
/// </summary>
public static class GradientHairTests
{
    private static readonly Color SecondColor = new(0.2f, 0.6f, 0.9f);

    [InGameTest]
    public static void Patterns_AreAvailable()
    {
        Check.Requires(GradientHairCompat.Active, "Gradient Hair not loaded");
        Check.That(GradientHairCompat.PatternsAvailable, "Gradient Hair is loaded but its pattern fields were not found");
        Check.That(GradientHairCompat.Patterns.Count > 1, $"only {GradientHairCompat.Patterns.Count} patterns found");
    }

    [InGameTest]
    public static void Duplicate_KeepsGradient()
    {
        var original = GradientColonist();

        var duplicate = EditorApi.CreateStableDuplicateOrSelf(original);
        Check.That(!ReferenceEquals(duplicate, original), "duplication fell back to returning the original pawn");

        Check.Equal(Signature(original), Signature(duplicate), "gradient");
    }

    [InGameTest]
    public static void RoundTrip_KeepsGradient()
    {
        var original = GradientColonist();
        var path = TestPawns.TempFile(nameof(RoundTrip_KeepsGradient) + ".xml");

        PawnBlueprintSaveLoad.SaveBlueprint(original, path);
        Check.That(File.Exists(path), "SaveBlueprint did not write the file");
        var loaded = PawnBlueprintSaveLoad.LoadBlueprint(path);
        Check.NotNull(loaded, "LoadBlueprint");

        Check.Equal(Signature(original), Signature(loaded), "gradient");
    }

    /// <summary>A colonist with the gradient on, a known second color and a pattern other than the first.</summary>
    private static Pawn GradientColonist()
    {
        Check.Requires(GradientHairCompat.PatternsAvailable, "Gradient Hair patterns not available");
        var pawn = TestPawns.Colonist();
        Check.That(GradientHairCompat.TryGet(pawn, out _, out _), "a fresh colonist has no Gradient Hair data");

        GradientHairCompat.Set(pawn, true, SecondColor);
        var pattern = GradientHairCompat.Patterns.Last();
        GradientHairCompat.SetPattern(pawn, pattern);
        Check.Equal(pattern, GradientHairCompat.CurrentPattern(pawn), "pattern right after SetPattern");
        return pawn;
    }

    private static string Signature(Pawn pawn)
    {
        if (!GradientHairCompat.TryGet(pawn, out var enabled, out var color)) return "no Gradient Hair data";
        return $"enabled {enabled}, color {ColorUtility.ToHtmlStringRGB(color)}, " +
               $"pattern {GradientHairCompat.CurrentPattern(pawn)?.defName ?? "none"}";
    }
}
