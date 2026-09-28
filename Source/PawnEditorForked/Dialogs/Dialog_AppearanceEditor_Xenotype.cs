using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// Everything about xenotypes and genes: the "Cosmetic genes" tab (filters, gene groups, what is
// allowed) and applying a xenotype or a custom xenotype to the pawn.
public partial class Dialog_AppearanceEditor
{
    private void DoXenotypePicker(Rect inRect)
    {
        DrawXenotypeFilters(inRect.TakeTopPart(UIUtility.RegularButtonHeight));
        inRect.yMin += 4f;

        var overrideRect = inRect.TakeTopPart(30f);
        Widgets.CheckboxLabeled(overrideRect, "PawnEditor.OverrideUnsupportedGenes".Translate(), ref ignoreXenotype);
        TooltipHandler.TipRegion(overrideRect, "PawnEditor.OverrideUnsupportedGenes.Desc".Translate());
        inRect.yMin += 4f;

        DoXenotypeOptions(inRect);
    }

    private void DrawXenotypeFilters(Rect row)
    {
        var sourceRect = row.LeftHalf().ContractedBy(1f, 0f);
        var categoryRect = row.RightHalf().ContractedBy(1f, 0f);
        var sourceLabel = "PawnEditor.Source".Translate().CapitalizeFirst() + ": " +
                          (sourceFilter?.Name ?? "PawnEditor.All".Translate().CapitalizeFirst().ToString());
        if (Widgets.ButtonText(sourceRect, sourceLabel))
        {
            var sourceDefs = CosmeticGeneDiscovery.GroupGenes.SelectMany(defs => defs.Cast<Def>());
            var options = LoadedModManager.RunningMods
                .Intersect(sourceDefs.Select(def => def.modContentPack).Where(mod => mod != null).Distinct())
                .Select(mod => new FloatMenuOption(mod.Name, () => SetXenotypeSource(mod)))
                .Prepend(new FloatMenuOption("PawnEditor.All".Translate().CapitalizeFirst(), () => SetXenotypeSource(null)))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }
        TooltipHandler.TipRegion(sourceRect, "PawnEditor.SourceDesc".Translate());

        var categoryLabel = xenotypeCategoryIndex >= 0 && xenotypeCategoryIndex < CosmeticGeneDiscovery.GroupLabels.Count
            ? CosmeticGeneDiscovery.GroupLabels[xenotypeCategoryIndex]
            : "PawnEditor.All".Translate().ToString().CapitalizeFirst();
        if (Widgets.ButtonText(categoryRect, "PawnEditor.Category".Translate().CapitalizeFirst() + ": " + categoryLabel))
        {
            var options = new List<FloatMenuOption>
            {
                new("PawnEditor.All".Translate().CapitalizeFirst(), () => SetXenotypeCategory(-1))
            };
            for (var i = 0; i < CosmeticGeneDiscovery.GroupLabels.Count; i++)
            {
                var index = i;
                options.Add(new FloatMenuOption(CosmeticGeneDiscovery.GroupLabels[index], () => SetXenotypeCategory(index)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        TooltipHandler.TipRegion(categoryRect, "PawnEditor.XenotypeCategoryDesc".Translate());
    }

    private void SetXenotypeSource(ModContentPack source)
    {
        sourceFilter = source;
        scrollPos = Vector2.zero;
        ForgetCachedOptions();
    }

    private void SetXenotypeCategory(int index)
    {
        xenotypeCategoryIndex = index;
        scrollPos = Vector2.zero;
    }

    private void DoXenotypeOptions(Rect inRect)
    {
        if (Event.current.type == EventType.Layout) lastXenotypeHeight = 9999;
        var viewRect = new Rect(0, 0, Mathf.Max(1f, inRect.width - 20f), lastXenotypeHeight);
        // Visible content band, passed to DoGeneOptions so it can cull off-screen gene rows/groups.
        var visMin = scrollPos.y;
        var visMax = scrollPos.y + inRect.height;
        Widgets.BeginScrollView(inRect, ref scrollPos, viewRect);
        for (var i = 0; i < CosmeticGeneDiscovery.GroupLabels.Count; i++)
        {
            if (xenotypeCategoryIndex >= 0 && xenotypeCategoryIndex != i)
                continue;

            var options = CosmeticGeneDiscovery.GroupGenes[i].Where(MatchesSource).ToList();
            if (xenotypeCategoryIndex >= 0 || options.Count > 0)
                DoGeneOptions(ref viewRect, CosmeticGeneDiscovery.GroupLabels[i], options, visMin, visMax);
        }
        if (Event.current.type == EventType.Layout) lastXenotypeHeight -= viewRect.height;
        Widgets.EndScrollView();
    }

    private void DoGeneOptions(ref Rect inRect, string label, List<GeneDef> options, float visMin, float visMax)
    {
        Widgets.Label(inRect.TakeTopPart(Text.LineHeight), label);
        if (options.Count == 0)
        {
            Widgets.Label(inRect.TakeTopPart(Text.LineHeight).RightPart(0.9f), "PawnEditor.NoOptions".Translate().Colorize(ColoredText.SubtleGrayColor));
            return;
        }

        var itemsPerRow = 9;
        var itemSize = (inRect.width - 20) / itemsPerRow;
        while (itemSize > 192)
        {
            itemsPerRow++;
            itemSize = (inRect.width - 20) / itemsPerRow;
        }

        while (itemSize < 48)
        {
            itemsPerRow--;
            itemSize = (inRect.width - 20) / itemsPerRow;
        }

        var gridHeight = Mathf.Ceil((float)options.Count / itemsPerRow) * itemSize;
        var groupTop = inRect.yMin;
        Widgets.BeginGroup(inRect.TakeTopPart(gridHeight));

        // Cull gene rows outside the visible band (also skips whole groups scrolled off-screen). The
        // layout above still consumes the full height, so scrolling stays correct.
        var firstRow = Mathf.Max(0, Mathf.FloorToInt((visMin - groupTop) / itemSize) - 1);
        var lastRow = Mathf.FloorToInt((visMax - groupTop) / itemSize) + 1;
        var start = firstRow * itemsPerRow;
        var end = Mathf.Min(options.Count, (lastRow + 1) * itemsPerRow);
        for (var i = start; i < end; i++)
        {
            var option = options[i];
            var rect = new Rect(i % itemsPerRow * itemSize, Mathf.Floor((float)i / itemsPerRow) * itemSize, itemSize, itemSize).ContractedBy(2);
            bool enabled = GeneIsAllowed(option);
            Widgets.DrawHighlight(rect);
            if (pawn.genes.HasActiveGene(option))
            {
                Widgets.DrawBox(rect);
            }
            if (enabled && Widgets.ButtonInvisible(rect))
            {
                // Guard the whole add/remove against a gene from a removed mod (null/bad def):
                // without this, an exception here mid-render can hard-crash the game.
                try
                {
                    if (pawn.genes.HasActiveGene(option))
                    {
                        pawn.genes.RemoveGene(pawn.genes.GetGene(option));
                    }
                    else
                    {
                        // Genes in this group are mutually exclusive (same exclusionTag), so
                        // remove any the pawn already has from the group, THEN add the chosen one
                        // once. AddGene must be OUTSIDE the loop — inside it re-added the same gene
                        // once per group member (harmless but wasteful and confusing).
                        foreach (var geneDef in options)
                        {
                            if (pawn.genes.GetGene(geneDef) is { } gene) pawn.genes.RemoveGene(gene);
                        }

                        pawn.genes.AddGene(option, false);
                    }

                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Pawn Editor] Failed to toggle gene '{option?.defName ?? "null"}': {ex.Message}");
                }
            }

            GUI.color = enabled ? Color.white : Color.gray;
            // ToDo: Apply correct gene background texture according to gene category.
            GUI.DrawTexture(rect.ContractedBy(4), GeneUIUtility.GeneBackground_Endogene.Texture);
            GUI.color *= option.IconColor;
            SafeDrawIcon(rect.ContractedBy(4), option.Icon, option);
            GUI.color = Color.white;

            TooltipHandler.TipRegion(rect, option.LabelCap + (enabled ? TaggedString.Empty : "\n\n" + "PawnEditor.XenotypeForbbiden".Translate()));
        }

        inRect.yMin += 8f;
        Widgets.EndGroup();
    }

    private bool GeneIsAllowed(GeneDef option)
    {
        if (ignoreXenotype) return true;
        if (HARCompat.Active && HARCompat.EnforceRestrictions && HARCompat.CanHaveGene(option, pawn) is false)
        {
            return false;
        }
        else if (pawn.IsBaseliner())
        {
            // For baseliners, allow all cosmetic genes (they're in the appearance editor for a reason)
            return true;
        }
        else if (pawn.genes.Xenotype != null)
        {
            return pawn.genes.Xenotype.AllGenes.Contains(option);
        }
        else if (pawn.genes.CustomXenotype != null)
        {
            return pawn.genes.CustomXenotype.genes.Contains(option);
        }

        return false;
    }

    private void SetXenotype(XenotypeDef xenotype)
    {
        for (int num = pawn.genes.endogenes.Count - 1; num >= 0; num--)
        {
            pawn.genes.RemoveGene(pawn.genes.endogenes[num]);
        }

        pawn.genes.ClearXenogenes();
        PawnGenerator.GenerateGenes(pawn, xenotype, default);
        ForgetCachedOptions(); // gene-locked heads may have become allowed or forbidden
    }

    /// <summary>
    /// Applies a custom xenotype to the pawn. Guards against gene defs that are null — which
    /// happens when a saved custom xenotype references genes from a mod that is no longer
    /// installed. Without this guard, AddGene(null, ...) throws and (in game, mid-render) can
    /// hard-crash. Each gene is added in isolation so one missing gene can't abort the rest.
    /// </summary>
    private void ApplyCustomXenotype(CustomXenotype customXenotype)
    {
        if (customXenotype == null) return;
        try
        {
            if (!pawn.IsBaseliner()) pawn.genes.SetXenotype(XenotypeDefOf.Baseliner);
            pawn.genes.xenotypeName = customXenotype.name;
            pawn.genes.iconDef = customXenotype.IconDef;

            if (customXenotype.genes != null)
            {
                foreach (var geneDef in customXenotype.genes)
                {
                    if (geneDef == null) continue; // gene from a removed mod — skip instead of crashing
                    try { pawn.genes.AddGene(geneDef, !customXenotype.inheritable); }
                    catch (Exception ex) { Log.Warning($"[Pawn Editor] Skipped a gene while applying custom xenotype '{customXenotype.name}': {ex.Message}"); }
                }
            }

            TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
            ForgetCachedOptions(); // gene-locked heads may have become allowed or forbidden
        }
        catch (Exception ex)
        {
            Log.Error($"[Pawn Editor] Failed to apply custom xenotype '{customXenotype.name}': {ex.Message}");
        }
    }
}
