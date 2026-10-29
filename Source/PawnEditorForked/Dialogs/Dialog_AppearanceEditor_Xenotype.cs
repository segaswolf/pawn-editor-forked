using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// Everything about xenotypes and genes: the "Cosmetic genes" tab (the xenotype row, filters, gene
// groups, what is allowed). Changing the xenotype itself goes through the Bio tab's shared picker.
public partial class Dialog_AppearanceEditor
{
    private void DoXenotypePicker(Rect inRect)
    {
        DrawXenotypeRow(inRect.TakeTopPart(UIUtility.RegularButtonHeight));
        inRect.yMin += 4f;

        DrawXenotypeFilters(inRect.TakeTopPart(UIUtility.RegularButtonHeight));
        inRect.yMin += 4f;

        // "Ignore xenotype restrictions" lives in the left panel on every tab (see DrawIgnoreXenotypeToggle).
        DoXenotypeOptions(inRect);
    }

    private void DrawXenotypeFilters(Rect row)
    {
        var sourceRect = row.LeftHalf().ContractedBy(1f, 0f);
        var categoryRect = row.RightHalf().ContractedBy(1f, 0f);
        var sourceLabel = "PawnEditor.Source".Translate().CapitalizeFirst() + ": " +
                          (sourceFilter?.Name ?? "PawnEditor.All".Translate().CapitalizeFirst().ToString());
        if (Widgets.ButtonText(sourceRect, sourceLabel))
            Find.WindowStack.Add(new Dialog_ListPicker("PawnEditor.ChooseSource".Translate(),
                Dialog_ListPicker.ModSources(CosmeticGeneDiscovery.GroupGenes.SelectMany(defs => defs.Cast<Def>()), sourceFilter, SetXenotypeSource)));
        TooltipHandler.TipRegion(sourceRect, "PawnEditor.SourceDesc".Translate());

        var categoryLabel = xenotypeCategoryIndex >= 0 && xenotypeCategoryIndex < CosmeticGeneDiscovery.GroupLabels.Count
            ? CosmeticGeneDiscovery.GroupLabels[xenotypeCategoryIndex]
            : "PawnEditor.All".Translate().ToString().CapitalizeFirst();
        if (Widgets.ButtonText(categoryRect, "PawnEditor.Category".Translate().CapitalizeFirst() + ": " + categoryLabel))
        {
            var entries = new List<Dialog_ListPicker.Entry>
            {
                new("PawnEditor.All".Translate().CapitalizeFirst(), () => SetXenotypeCategory(-1), xenotypeCategoryIndex < 0, alwaysShown: true)
            };
            for (var i = 0; i < CosmeticGeneDiscovery.GroupLabels.Count; i++)
            {
                var index = i;
                entries.Add(new Dialog_ListPicker.Entry(CosmeticGeneDiscovery.GroupLabels[index], () => SetXenotypeCategory(index),
                    index == xenotypeCategoryIndex, CosmeticGeneDiscovery.GroupGenes[index].Count(MatchesSource)));
            }
            Find.WindowStack.Add(new Dialog_ListPicker("PawnEditor.ChooseCategory".Translate(), entries));
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
        // try/finally: if anything inside throws, the scroll view must still be closed. Left open, it
        // keeps a "mouse is not over this" entry on RimWorld's scroll-view stack until the next frame,
        // and every Mouse.IsOver check in the next GUI pass fails. That is how a failing gene grid made
        // the "Override unsupported genes" checkbox above it ignore clicks.
        Widgets.BeginScrollView(inRect, ref scrollPos, viewRect);
        try
        {
            for (var i = 0; i < CosmeticGeneDiscovery.GroupLabels.Count; i++)
            {
                if (xenotypeCategoryIndex >= 0 && xenotypeCategoryIndex != i)
                    continue;

                var options = CosmeticGeneDiscovery.GroupGenes[i].Where(MatchesSource).ToList();
                if (xenotypeCategoryIndex >= 0 || options.Count > 0)
                    DoGeneOptions(ref viewRect, CosmeticGeneDiscovery.GroupLabels[i], options, visMin, visMax);
            }
            if (Event.current.type == EventType.Layout) lastXenotypeHeight -= viewRect.height;
        }
        finally
        {
            Widgets.EndScrollView();
        }
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
        try
        {
            DrawGeneGrid(options, itemsPerRow, itemSize, groupTop, visMin, visMax);
        }
        finally
        {
            Widgets.EndGroup(); // always closed, even if a gene's data throws (see DoXenotypeOptions)
        }

        inRect.yMin += 8f;
    }

    private void DrawGeneGrid(List<GeneDef> options, int itemsPerRow, float itemSize, float groupTop, float visMin, float visMax)
    {
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

    /// <summary>
    /// The xenotype row at the top of the Cosmetic genes tab: the current xenotype, and a button that
    /// opens the same xenotype listing as the Bio tab (search, custom xenotypes, xenotype editor). It
    /// used to live in the left panel as an older float menu, apart from the genes it sets.
    /// </summary>
    private void DrawXenotypeRow(Rect row)
    {
        var iconRect = row.TakeLeftPart(row.height).ContractedBy(2f);
        GUI.color = XenotypeDef.IconColor;
        GUI.DrawTexture(iconRect, XenotypeIconUtility.IconFor(pawn));
        GUI.color = Color.white;
        row.xMin += 4f;

        var label = "Xenotype".Translate().CapitalizeFirst() + ": " + pawn.genes.XenotypeLabelCap;
        if (Widgets.ButtonText(row, label))
            TabWorker_Bio_Humanlike.OpenXenotypePicker(pawn);
        if (Find.WindowStack.FloatMenu == null)
            TooltipHandler.TipRegion(row, "XenotypeSelectionDesc".Translate());
    }
}
