using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

// Applying xenotypes to a pawn's genes: swap (keeps unrelated genes) or full reset.
public partial class TabWorker_Bio_Humanlike
{
    /// <summary>Swaps xenotype: removes the old xenotype's genes, adds the new ones, keeps any other gene.</summary>
    public static void SetXenotype(Pawn pawn, XenotypeDef xenotype)
    {
        if (pawn.genes == null) return;
        ClearXenotype(pawn);
        foreach (var gene in xenotype.genes)
            pawn.genes.AddGene(gene, !xenotype.inheritable);
        pawn.genes.SetXenotypeDirect(xenotype);
    }

    /// <summary>Swaps to a custom xenotype the same way: only the old xenotype's genes are removed.</summary>
    public static void SetXenotype(Pawn pawn, CustomXenotype xenotype)
    {
        if (pawn.genes == null || xenotype == null) return;
        ClearXenotype(pawn);
        pawn.genes.xenotypeName = xenotype.name;
        pawn.genes.iconDef = xenotype.IconDef;
        foreach (var geneDef in xenotype.genes)
            pawn.genes.AddGene(geneDef, !xenotype.inheritable);
    }

    /// <summary>
    /// Removes EVERY gene, then applies only the new xenotype's. For switching between very different
    /// xenotypes (human to android) without leftovers.
    /// </summary>
    public static void ResetAndSetXenotype(Pawn pawn, XenotypeDef xenotype)
    {
        if (pawn.genes == null) return;
        RemoveAllGenes(pawn, pawn.genes.Endogenes, "endogene");
        RemoveAllGenes(pawn, pawn.genes.Xenogenes, "xenogene");
        foreach (var gene in xenotype.genes)
            pawn.genes.AddGene(gene, !xenotype.inheritable);
        pawn.genes.SetXenotypeDirect(xenotype);
        PawnEditor.Notify_PointsUsed();
    }

    /// <summary>Removes the genes of the pawn's current xenotype (vanilla or custom), nothing else.</summary>
    private static void ClearXenotype(Pawn pawn)
    {
        if (pawn.genes == null) return;
        if (pawn.genes.xenotype != null)
            RemoveXenotypeGenes(pawn, pawn.genes.xenotype.genes, pawn.genes.xenotype.inheritable);
        if (pawn.genes.CustomXenotype is { } customXenotype)
            RemoveXenotypeGenes(pawn, customXenotype.genes, customXenotype.inheritable);
    }

    /// <summary>Removes the pawn's gene for each def, looking in endogenes or xenogenes as the xenotype says.</summary>
    private static void RemoveXenotypeGenes(Pawn pawn, List<GeneDef> geneDefs, bool inheritable)
    {
        foreach (var geneDef in geneDefs.ToList())
        {
            var gene = (inheritable ? pawn.genes.Endogenes : pawn.genes.Xenogenes)?.FirstOrDefault(g => g.def == geneDef);
            if (gene != null) pawn.genes.RemoveGene(gene);
        }
    }

    /// <summary>Removes every gene in the list; one that fails is logged and the rest still go.</summary>
    private static void RemoveAllGenes(Pawn pawn, List<Gene> genes, string kind)
    {
        foreach (var gene in genes.ToList())
        {
            try { pawn.genes.RemoveGene(gene); }
            catch (System.Exception ex) { Log.Warning($"[Pawn Editor] Failed to remove {kind} {gene.def.defName}: {ex.Message}"); }
        }
    }
}
