using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnEditor;

/// <summary>
/// Blueprints and duplicating both start from a GENERATED pawn and swap its genes for the saved ones.
/// Two rules the game imposes, learned from the in-game tests (GeneFidelityTests, 13 Nov):
/// <list type="bullet">
/// <item>Take the generated genes out with RemoveGene, not List.Clear(). Only RemoveGene takes back what a gene
/// gave: its abilities, forced traits and its own PostRemove cleanup. A sanguophage blueprint without the
/// xenogerm reimplanter gene still came back able to reimplant xenogerms.</item>
/// <item>Set "which gene overrides which" only after EVERY gene is in. Each AddGene and RemoveGene re-resolves
/// all conflicts (Pawn_GeneTracker.CheckForOverrides) and wipes overrides set before it.</item>
/// </list>
/// </summary>
public static class GeneSwapUtility
{
    /// <summary>Removes every endogene or xenogene the way the game does, one by one.</summary>
    public static void RemoveAll(Pawn pawn, bool xenogenes)
    {
        var list = xenogenes ? pawn.genes.Xenogenes : pawn.genes.Endogenes;
        foreach (var gene in list.ToList())
        {
            try { pawn.genes.RemoveGene(gene); }
            catch (Exception ex)
            {
                // Another mod's PostRemove can throw. Drop that gene anyway rather than abort the whole load.
                Log.Warning($"[Pawn Editor] Removing gene '{gene?.def?.defName}' from the generated pawn failed: {ex.Message}");
                list.Remove(gene);
            }
        }
    }

    /// <summary>
    /// Copies the override relations from <paramref name="source"/> genes to their copies, using a map from
    /// each source gene to its copy. Call after all genes are added. A gene whose overrider wasn't copied is
    /// left to the game.
    /// </summary>
    public static void CopyOverrides(IEnumerable<Gene> sourceGenes, IDictionary<Gene, Gene> copyOf)
    {
        foreach (var src in sourceGenes)
        {
            if (!copyOf.TryGetValue(src, out var dst)) continue;
            var by = src.Overridden ? src.overriddenByGene : null;
            if (by == null) dst.overriddenByGene = null;
            else if (copyOf.TryGetValue(by, out var byCopy)) dst.overriddenByGene = byCopy;
        }
    }
}
