using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnEditor;

/// <summary>Bulk gene changes that leave genes granted by other mods in place.</summary>
/// <remarks>
/// Character Development quirks add xenogenes and track them by object reference
/// (<see cref="CharacterDevelopmentCompat.GetGrantedGenes"/>). Randomize, undo and xenotype resets go
/// through here so those genes keep their identity and their quirk keeps working.
/// </remarks>
public static class GrantedGenes
{
    /// <summary>Removes every gene the pawn has, except granted ones.</summary>
    public static void RemoveAllExceptGranted(Pawn pawn)
    {
        var granted = CharacterDevelopmentCompat.GetGrantedGenes(pawn);
        foreach (var gene in pawn.genes.GenesListForReading.ToList())
        {
            if (!granted.Contains(gene))
            {
                pawn.genes.RemoveGene(gene);
            }
        }
    }

    /// <summary>The defs of the pawn's endogenes or xenogenes, skipping granted genes.</summary>
    public static List<GeneDef> OwnGeneDefs(Pawn pawn, bool xenogenes)
    {
        var granted = CharacterDevelopmentCompat.GetGrantedGenes(pawn);
        var genes = xenogenes ? pawn.genes.Xenogenes : pawn.genes.Endogenes;
        return genes.Where(gene => !granted.Contains(gene)).Select(gene => gene.def).ToList();
    }

    /// <summary>The pawn's genes from the list, skipping granted ones.</summary>
    public static List<Gene> WithoutGranted(Pawn pawn, IEnumerable<Gene> genes)
    {
        var granted = CharacterDevelopmentCompat.GetGrantedGenes(pawn);
        return genes.Where(gene => !granted.Contains(gene)).ToList();
    }
}
