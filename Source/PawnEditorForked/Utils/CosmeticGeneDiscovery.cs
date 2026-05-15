using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Discovers and groups cosmetic genes from all loaded mods at startup.
/// 
/// RimWorld organizes genes into categories via GeneCategoryDef. Cosmetic genes
/// use categories whose defName contains "Cosmetic" (e.g., Cosmetic, Cosmetic_Body,
/// Cosmetic_Skin, Cosmetic_Hair) plus some mod-specific ones (AG_Cosmetic_Tails, etc.).
/// 
/// Within each category, genes that share an exclusionTag are mutually exclusive
/// (e.g., all "Ears" genes — you can only have one ear type at a time).
/// We group by exclusionTag so the UI can show them together.
/// 
/// VREA (VRE Androids) creates duplicate copies of every cosmetic gene with a "VREA_" prefix
/// for android pawns. These are filtered out to avoid showing duplicates in the editor.
/// Similarly, Alpha Genes "_Astrogene" archite variants are filtered.
/// </summary>
public static class CosmeticGeneDiscovery
{
    /// <summary>Human-readable labels for each gene group (e.g., "Ears", "Tail", "SkinColorOverride").</summary>
    public static readonly List<string> GroupLabels = new();

    /// <summary>Lists of GeneDefs for each group, parallel to GroupLabels.</summary>
    public static readonly List<List<GeneDef>> GroupGenes = new();

    /// <summary>
    /// Called once at startup via [StaticConstructorOnStartup].
    /// Scans all loaded GeneCategoryDefs and GeneDefs to build the cosmetic gene groups.
    /// </summary>
    public static void Initialize()
    {
        GroupLabels.Clear();
        GroupGenes.Clear();

        var cosmeticCategories = DiscoverCosmeticCategories();
        var cosmeticGenes = CollectCosmeticGenes(cosmeticCategories);
        BuildGroups(cosmeticGenes);

        Log.Message($"[Pawn Editor] Cosmetic gene groups: {GroupLabels.Count} groups, " +
                    $"{GroupGenes.Sum(g => g.Count)} total genes " +
                    $"(categories: {string.Join(", ", cosmeticCategories)})");
    }

    /// <summary>
    /// Scans all loaded GeneCategoryDefs for categories containing "Cosmetic" in their defName.
    /// Also includes "Fur" as a special case since some mods define fur as a separate category.
    /// </summary>
    private static HashSet<string> DiscoverCosmeticCategories()
    {
        var categories = new HashSet<string>();

        foreach (var catDef in DefDatabase<GeneCategoryDef>.AllDefsListForReading)
        {
            if (catDef.defName.Contains("Cosmetic") || catDef.defName == "Fur")
                categories.Add(catDef.defName);
        }

        return categories;
    }

    /// <summary>
    /// Collects all GeneDefs that belong to a cosmetic category,
    /// filtering out VREA android duplicates and Astrogene archite variants.
    /// </summary>
    private static List<GeneDef> CollectCosmeticGenes(HashSet<string> cosmeticCategories)
    {
        return DefDatabase<GeneDef>.AllDefsListForReading
            .Where(g => g?.displayCategory != null
                && cosmeticCategories.Contains(g.displayCategory.defName)
                && !g.defName.StartsWith("VREA_")
                && !g.defName.EndsWith("_Astrogene"))
            .ToList();
    }

    /// <summary>
    /// Groups cosmetic genes into display groups:
    /// - Pass 1: Group by first exclusionTag (mutually exclusive genes together)
    /// - Pass 2: Remaining genes grouped by endogeneCategory or into "Other cosmetic"
    /// </summary>
    private static void BuildGroups(List<GeneDef> cosmeticGenes)
    {
        var assigned = new HashSet<GeneDef>();
        var groupsByKey = new Dictionary<string, List<GeneDef>>();
        var groupOrder = new List<string>();

        // Pass 1: Group by first exclusionTag
        foreach (var gene in cosmeticGenes)
        {
            if (assigned.Contains(gene)) continue;
            if (gene.exclusionTags == null || gene.exclusionTags.Count == 0) continue;

            var tag = gene.exclusionTags[0];
            if (!groupsByKey.ContainsKey(tag))
            {
                groupsByKey[tag] = new List<GeneDef>();
                groupOrder.Add(tag);
            }
            groupsByKey[tag].Add(gene);
            assigned.Add(gene);
        }

        // Pass 2: Ungrouped cosmetic genes — group by endogeneCategory
        foreach (var gene in cosmeticGenes)
        {
            if (assigned.Contains(gene)) continue;

            var key = gene.endogeneCategory != EndogeneCategory.None
                ? "_cat_" + gene.endogeneCategory
                : "_ungrouped";

            if (!groupsByKey.ContainsKey(key))
            {
                groupsByKey[key] = new List<GeneDef>();
                groupOrder.Add(key);
            }
            groupsByKey[key].Add(gene);
            assigned.Add(gene);
        }

        // Convert to final display lists with human-readable labels
        foreach (var key in groupOrder)
        {
            var genes = groupsByKey[key];
            if (genes.Count == 0) continue;

            GroupLabels.Add(GenerateLabel(key));
            GroupGenes.Add(genes);
        }
    }

    /// <summary>
    /// Generates a human-readable label from a group key.
    /// Keys starting with "_cat_" are endogeneCategory names.
    /// Keys starting with "_ungrouped" get the "Other cosmetic" translation.
    /// All other keys are exclusionTag names, formatted as "Skin Color Override" etc.
    /// </summary>
    private static string GenerateLabel(string key)
    {
        if (key.StartsWith("_cat_"))
            return key.Substring(5).CapitalizeFirst();

        if (key == "_ungrouped")
            return "PawnEditor.OtherCosmetic".Translate();

        // ExclusionTag name — replace underscores with spaces and capitalize
        return key.Replace("_", " ").CapitalizeFirst();
    }
}
