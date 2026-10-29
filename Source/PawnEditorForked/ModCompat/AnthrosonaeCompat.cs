using System;
using System.Linq;
using HarmonyLib;
using Verse;

namespace PawnEditor;

/// <summary>
/// Restores Anthrosonae's "Change fur" button inside our appearance editor.
///
/// Anthrosonae (ATK.Anthrosonae) ships a Pawn Editor integration, but it gates on
/// `ModsConfig.IsActive("ISOREX.PawnEditor")` — the ORIGINAL Pawn Editor's packageId. Our fork has a
/// different id, so their postfix never runs and the button that colours antro fur simply vanished.
/// Several users reported it missing. Rather than wait on them to add our id, we detect the mod on our
/// side and open their own colour window ourselves.
///
/// Verified against their shipped source (1.6/Source/):
///   - fur gene:   pawn.genes.GenesListForReading.OfType&lt;Anthrosonae.FurGene&gt;().FirstOrDefault(Active)
///   - the window: new Anthrosonae.Window_ColorPicker(furGene)
///   - button key: "ColorPicker.ChangeFur" (their translation key, so wording matches their mod)
/// All reflection by type name; inert when Anthrosonae isn't installed.
/// </summary>
public static class AnthrosonaeCompat
{
    private static readonly Type FurGeneType = AccessTools.TypeByName("Anthrosonae.FurGene");
    private static readonly Type WindowType = AccessTools.TypeByName("Anthrosonae.Window_ColorPicker");

    public static bool Active => FurGeneType != null && WindowType != null;

    /// <summary>The pawn's active fur gene, or null. Null means: don't show the button for this pawn.</summary>
    public static object GetFurGene(Pawn pawn)
    {
        if (!Active || pawn?.genes == null) return null;
        try
        {
            return pawn.genes.GenesListForReading
                .FirstOrDefault(g => g != null && FurGeneType.IsInstanceOfType(g) && g.Active);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Anthrosonae fur gene lookup failed: {ex.Message}");
            return null;
        }
    }

    public static bool HasFur(Pawn pawn) => GetFurGene(pawn) != null;

    /// <summary>Opens Anthrosonae's own fur colour picker for this pawn's fur gene.</summary>
    public static void OpenFurPicker(Pawn pawn)
    {
        var gene = GetFurGene(pawn);
        if (gene == null) return;
        try
        {
            if (Activator.CreateInstance(WindowType, gene) is Window window)
                Find.WindowStack.Add(window);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Could not open Anthrosonae fur picker: {ex.Message}");
        }
    }
}
