using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Discovers and groups cosmetic genes from all loaded mods at startup.
///
/// REWORK (v2.4.9): classification is now STRUCTURAL, not name-based.
/// Previously we only accepted genes whose GeneCategoryDef name contained "Cosmetic".
/// That missed mods using their own category names (e.g. EyeGenes2 -> "EyeGenes2_Eyes")
/// and split categories that mixed pure-cosmetic and minor-effect genes.
///
/// Now we classify each gene by WHAT IT DOES, using structure the game forces:
///   AXIS 1 - "where it affects" (for GROUPING): RimWorld only has ONE region-specific render
///            node subclass that matters for cosmetics: PawnRenderNodeProperties_Eye (confirmed
///            by decompile; the rest are internal like _Swaddle/_Carried/_Tattoo/_Overlay). So
///            eyes are detected by render node class, and every OTHER region (hair, skin, ears,
///            body, etc.) is detected via endogeneCategory first, then a vanilla defName-prefix
///            fallback. A modder can rename their category freely, but cannot change how the game
///            draws an eye -> the eye render node class is reliable where names aren't. Modded
///            genes that match none of these fall into "Other cosmetic" (safe, never broken).
///   AXIS 2 - "cosmetic vs mechanical" (for FILTERING): a gene is shown only if it is PURELY
///            cosmetic (no stat offsets/factors, no capacity mods, no abilities, no aptitudes,
///            no forced/suppressed traits, no work disables, zero biostats). Anything that
///            gives or takes a stat/ability is left out of the cosmetic tab.
///
/// This runs ONCE at startup ([StaticConstructorOnStartup]); it is not a per-frame or hot-path
/// operation, so it has no bearing on the runtime GC pressure that must stay controlled
/// elsewhere. It still avoids needless allocations.
///
/// VREA (VRE Androids) creates duplicate copies of every cosmetic gene with a "VREA_" prefix;
/// Alpha Genes "_Astrogene" archite variants are duplicates too. Both are filtered out.
/// </summary>
public static class CosmeticGeneDiscovery
{
    /// <summary>Human-readable labels for each gene group (e.g., "Eyes", "Hair", "Ears").</summary>
    public static readonly List<string> GroupLabels = new();

    /// <summary>Lists of GeneDefs for each group, parallel to GroupLabels.</summary>
    public static readonly List<List<GeneDef>> GroupGenes = new();

    /// <summary>
    /// Called once at startup via [StaticConstructorOnStartup].
    /// Scans all loaded GeneDefs and builds the cosmetic gene groups by structural region.
    /// </summary>
    public static void Initialize()
    {
        GroupLabels.Clear();
        GroupGenes.Clear();

        var cosmeticGenes = CollectCosmeticGenes();
        BuildGroups(cosmeticGenes);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AXIS 2 — cosmetic vs mechanical (filter)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects every gene that is PURELY cosmetic, regardless of which category its mod put it in.
    /// Filters out VREA android duplicates and Astrogene archite variants.
    /// </summary>
    private static List<GeneDef> CollectCosmeticGenes()
    {
        return DefDatabase<GeneDef>.AllDefsListForReading
            .Where(g => g != null
                && !g.defName.StartsWith("VREA_")
                && !g.defName.EndsWith("_Astrogene")
                && IsPurelyCosmetic(g))
            .ToList();
    }

    /// <summary>
    /// True when a gene has NO mechanical effect on the pawn: zero biostats, and none of the
    /// common effect fields. Such a gene only changes appearance. Genes with even a minor effect
    /// (e.g. +2 Beauty) are intentionally excluded from the cosmetic tab (design decision).
    /// </summary>
    private static bool IsPurelyCosmetic(GeneDef g)
    {
        // Any biostat cost/benefit means it is not "free" cosmetic.
        if (g.biostatCpx != 0 || g.biostatMet != 0 || g.biostatArc != 0) return false;

        // Must have at least one appearance signal, otherwise it isn't cosmetic at all.
        if (!HasAppearanceSignal(g)) return false;

        // Reject anything that gives or takes a mechanical effect.
        var hasMechanical = !g.statOffsets.NullOrEmpty()
            || !g.statFactors.NullOrEmpty()
            || !g.capMods.NullOrEmpty()
            || !g.conditionalStatAffecters.NullOrEmpty()
            || !g.abilities.NullOrEmpty()
            || !g.aptitudes.NullOrEmpty()
            || !g.forcedTraits.NullOrEmpty()
            || !g.suppressedTraits.NullOrEmpty()
            || g.disabledWorkTags != WorkTags.None
            || g.passionMod != null
            || g.makeImmuneTo != null && g.makeImmuneTo.Count > 0;
        return !hasMechanical;
    }

    /// <summary>True when a gene has any signal that it changes appearance.</summary>
    private static bool HasAppearanceSignal(GeneDef g)
    {
        return !g.renderNodeProperties.NullOrEmpty()
            || g.hairColorOverride.HasValue
            || g.skinColorBase.HasValue
            || g.skinColorOverride.HasValue
            || g.skinIsHairColor
            || g.fur != null
            || !g.forcedHeadTypes.NullOrEmpty()
            || g.bodyType != null
            || g.hairTagFilter != null
            || g.beardTagFilter != null
            || g.endogeneCategory != EndogeneCategory.None
            // A known appearance exclusionTag (e.g. EyeColor) is itself an appearance signal.
            // This catches genes whose visual is handled by ANOTHER mod and therefore have no
            // render node of their own: e.g. EyeGenes2 with Facial Animation strips the eye
            // render nodes (its own system draws them), leaving only exclusionTag=EyeColor.
            // Without this, those purely-cosmetic eye genes would be invisible in the editor.
            || HasKnownAppearanceTag(g);
    }

    /// <summary>
    /// True if the gene carries a known appearance-region exclusionTag. These tags are used
    /// consistently across mods (confirmed by scanning EyeGenes2, Cyanobot's, Alpha Genes,
    /// ReSplice, VRE, etc.), so they are a reliable structural signal independent of render nodes.
    /// </summary>
    private static bool HasKnownAppearanceTag(GeneDef g)
    {
        if (g.exclusionTags.NullOrEmpty()) return false;
        foreach (var tag in g.exclusionTags)
            if (AppearanceExclusionTags.Contains(tag))
                return true;
        return false;
    }

    /// <summary>
    /// ExclusionTags that denote an appearance region. A modder chooses these freely, but in
    /// practice the vanilla/common ones below are used consistently across mods, which is why
    /// they double as both an appearance signal (filter) and a grouping key (Pass 1 below).
    /// Conservative list: only well-established cosmetic-region tags, so we don't mis-include
    /// a mechanical gene that happens to share a tag.
    /// </summary>
    private static readonly HashSet<string> AppearanceExclusionTags = new()
    {
        "EyeColor", "HairColor", "SkinColorOverride", "SkinColor",
        "Ears", "Tail", "Nose", "Headbone", "Beard", "Fur",
        "InsectorEye", "Antennae", "Antennas",
    };

    // ─────────────────────────────────────────────────────────────────────────
    // AXIS 1 — where it affects (group)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Groups cosmetic genes by body region, most-reliable signal first:
    /// - Pass 1: by render node region (PawnRenderNodeProperties_Eye -> "Eye"). The render node
    ///   is structural and cannot be faked, so it wins over exclusionTags. This fixes genes whose
    ///   FIRST exclusionTag is mechanical (e.g. AG_FeraliskEyes tagged ShootingAccuracy,EyeColor
    ///   would otherwise land in a "ShootingAccuracy" group instead of with the eyes).
    /// - Pass 2: by the first KNOWN appearance exclusionTag (EyeColor, Ears, etc.). Catches genes
    ///   with no render node of their own (e.g. EyeGenes2 + Facial Animation) using the tag the
    ///   modder set, which is consistent across mods for these regions.
    /// - Pass 3: remaining genes by structural region (endogeneCategory, then name prefix).
    /// </summary>
    private static void BuildGroups(List<GeneDef> cosmeticGenes)
    {
        var assigned = new HashSet<GeneDef>();
        var groupsByKey = new Dictionary<string, List<GeneDef>>();
        var groupOrder = new List<string>();

        void AddTo(string key, GeneDef gene)
        {
            if (!groupsByKey.TryGetValue(key, out var list))
            {
                list = new List<GeneDef>();
                groupsByKey[key] = list;
                groupOrder.Add(key);
            }
            list.Add(gene);
            assigned.Add(gene);
        }

        // Pass 1: by render node region (structural, cannot be faked, wins over tags).
        foreach (var gene in cosmeticGenes)
        {
            if (assigned.Contains(gene)) continue;
            var region = RegionFromRenderNodes(gene);
            if (region != null) AddTo(region, gene);
        }

        // Pass 2: by the first KNOWN appearance exclusionTag (consistent across mods).
        foreach (var gene in cosmeticGenes)
        {
            if (assigned.Contains(gene)) continue;
            if (gene.exclusionTags.NullOrEmpty()) continue;
            var tag = gene.exclusionTags.FirstOrDefault(t => AppearanceExclusionTags.Contains(t));
            if (tag != null) AddTo(tag, gene);
        }

        // Pass 3: remaining genes by other structure (endogeneCategory, name prefix, or Other).
        foreach (var gene in cosmeticGenes)
        {
            if (assigned.Contains(gene)) continue;
            AddTo(RegionKeyFor(gene), gene);
        }

        foreach (var key in groupOrder)
        {
            var genes = groupsByKey[key];
            if (genes.Count == 0) continue;
            GroupLabels.Add(GenerateLabel(key));
            GroupGenes.Add(genes);
        }
    }

    /// <summary>Returns the region key from a gene's render nodes, or null if none apply.</summary>
    private static string RegionFromRenderNodes(GeneDef g)
    {
        if (g.renderNodeProperties.NullOrEmpty()) return null;
        foreach (var rnp in g.renderNodeProperties)
        {
            var region = RegionFromRenderNodeClass(rnp?.GetType()?.Name);
            if (region != null) return region;
        }
        return null;
    }

    /// <summary>
    /// Determines a gene's body-region group key from its structure, most-reliable signal first:
    /// 1. The render node class — only PawnRenderNodeProperties_Eye exists as a region subclass
    ///    (decompile-confirmed), so in practice this catches eyes.
    /// 2. endogeneCategory (RimWorld's own grouping for skin/hair color, etc.).
    /// 3. A vanilla defName prefix fallback (Hair_, Ears_, Voice_, etc.).
    /// 4. "_ungrouped" -> "Other cosmetic" (modded genes matching nothing land here, safely).
    /// </summary>
    private static string RegionKeyFor(GeneDef g)
    {
        // 1. Render node class.
        if (!g.renderNodeProperties.NullOrEmpty())
        {
            foreach (var rnp in g.renderNodeProperties)
            {
                var region = RegionFromRenderNodeClass(rnp?.GetType()?.Name);
                if (region != null) return region;
            }
        }

        // 2. endogeneCategory (skin/hair color etc.).
        if (g.endogeneCategory != EndogeneCategory.None)
            return "_cat_" + g.endogeneCategory;

        // 3. defName prefix fallback (covers non-visual appearance regions like Voice).
        var byName = RegionFromNamePrefix(g.defName);
        if (byName != null) return byName;

        // 4. Could not classify — Other cosmetic.
        return "_ungrouped";
    }

    /// <summary>
    /// Maps a PawnRenderNodeProperties subclass name to a region group key.
    /// Returns null if the class is the generic base or unrecognized (caller falls through).
    /// </summary>
    private static string RegionFromRenderNodeClass(string className)
    {
        if (className.NullOrEmpty()) return null;
        // Subclasses follow the pattern "PawnRenderNodeProperties_<Region>".
        const string prefix = "PawnRenderNodeProperties_";
        if (!className.StartsWith(prefix)) return null;
        var region = className.Substring(prefix.Length);
        return region.NullOrEmpty() ? null : region; // e.g. "Eye", "Head", "Body"
    }

    /// <summary>
    /// Last-resort region detection from the defName prefix, for appearance genes that have no
    /// visual render node (e.g. Voice_*) or whose render node was generic. Conservative: only
    /// well-known vanilla prefixes, so we don't mis-bucket modded genes.
    /// </summary>
    private static string RegionFromNamePrefix(string defName)
    {
        if (defName.NullOrEmpty()) return null;
        foreach (var kv in NamePrefixRegions)
            if (defName.StartsWith(kv.Key)) return kv.Value;
        return null;
    }

    /// <summary>
    /// Known vanilla appearance prefixes -> region. Derived from the vanilla gene list
    /// (see XENOTYPE_REWORK_PLAN.md). Only used as a fallback when structure is ambiguous.
    /// </summary>
    private static readonly List<KeyValuePair<string, string>> NamePrefixRegions = new()
    {
        new("Voice", "Voice"),
        new("Hair", "Hair"),
        new("Beard", "Beard"),
        new("Skin", "Skin"),
        new("Eyes", "Eyes"),
        new("Tail", "Tail"),
        new("Ears", "Ears"),
        new("Nose", "Nose"),
        new("Jaw", "Jaw"),
        new("Head", "Head"),
        new("Body", "Body"),
        new("Hands", "Hands"),
    };

    /// <summary>
    /// Generates a human-readable label from a group key.
    /// "_cat_" prefix -> endogeneCategory name. "_ungrouped" -> "Other cosmetic" translation.
    /// "Eye" -> "Eyes" friendly plural. Otherwise the key, underscores to spaces, capitalized.
    /// </summary>
    private static string GenerateLabel(string key)
    {
        if (key.StartsWith("_cat_"))
            return key.Substring(5).CapitalizeFirst();

        if (key == "_ungrouped")
            return "PawnEditor.OtherCosmetic".Translate();

        // Friendly plurals for the singular render-node region names.
        switch (key)
        {
            case "Eye": return "Eyes";
            case "Hand": return "Hands";
        }

        return key.Replace("_", " ").CapitalizeFirst();
    }
}
