using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Listing menu for selecting xenotypes. Replaces the massive FloatMenu dropdown
/// with a searchable, scrollable listing with icons and descriptions.
/// Includes both XenotypeDefs (mod/vanilla) and custom user-created xenotypes.
/// </summary>
public class ListingMenu_Xenotypes : ListingMenu<XenotypeDef>
{
    private readonly Pawn _pawn;
    private readonly Action<XenotypeDef> _onSelected;
    private Vector2 _customScrollPos;

    public ListingMenu_Xenotypes(Pawn pawn, Action<XenotypeDef> onSelected) : base(
        GetXenotypeList(pawn),
        x => x.LabelCap,
        x => TryApply(x, pawn, onSelected),
        "PawnEditor.Choose".Translate() + " " + "Xenotype".Translate().ToLower(),
        x => GetTooltip(x),
        DrawXenotypeIcon,
        null,
        pawn)
    {
        _pawn = pawn;
        _onSelected = onSelected;
        // Force cache load so custom xenotypes always appear
        _ = CharacterCardUtility.CustomXenotypes;
    }

    protected override void DrawFooter(ref Rect inRect)
    {
        // Custom xenotypes section — shown below the main listing
        var customXenotypes = CharacterCardUtility.CustomXenotypes;
        if (customXenotypes != null && customXenotypes.Count > 0)
        {
            // Allocate space: up to 150px for the scrollable list + header
            var customListHeight = Mathf.Min(customXenotypes.Count * 30f, 150f);
            var totalHeight = customListHeight + Text.LineHeightOf(GameFont.Small) + 8f;
            var sectionRect = inRect.TakeBottomPart(totalHeight);

            using (new TextBlock(TextAnchor.MiddleLeft))
                Widgets.Label(sectionRect.TakeTopPart(Text.LineHeightOf(GameFont.Small) + 4f),
                    ("Custom".Translate() + " " + "Xenotype".Translate().ToLower() + ":").Colorize(ColoredText.TipSectionTitleColor));

            var viewRect = new Rect(0, 0, sectionRect.width - 16f, customXenotypes.Count * 30f);
            Widgets.BeginScrollView(sectionRect, ref _customScrollPos, viewRect);
            for (var i = 0; i < customXenotypes.Count; i++)
            {
                var custom = customXenotypes[i];
                var rowRect = new Rect(0, i * 30f, viewRect.width, 28f);
                rowRect.yMin += 2f;

                // Icon
                var iconRect = rowRect.TakeLeftPart(24f);
                if (custom.IconDef?.Icon != null)
                {
                    GUI.color = XenotypeDef.IconColor;
                    GUI.DrawTexture(iconRect.ContractedBy(2f), custom.IconDef.Icon);
                    GUI.color = Color.white;
                }

                rowRect.xMin += 4f;

                // Highlight + tooltip
                if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawHighlight(rowRect);
                    var inheritableText = custom.inheritable ? "Inheritable".Translate() : "PawnEditor.NotInheritable".Translate();
                    var tip = custom.name.CapitalizeFirst() + "\n\n"
                        + inheritableText.Colorize(ColoredText.SubtleGrayColor)
                        + "\n" + ("Genes".Translate() + ": " + custom.genes.Count).Colorize(ColoredText.SubtleGrayColor);
                    TooltipHandler.TipRegion(rowRect, tip);
                }

                // Label button
                var label = custom.name.CapitalizeFirst() + " (" + "Custom".Translate() + ")";
                if (Widgets.ButtonText(rowRect, label, drawBackground: false, overrideTextAnchor: TextAnchor.MiddleLeft))
                {
                    TabWorker_Bio_Humanlike.SetXenotype(_pawn, custom);
                    TabWorker_Bio_Humanlike.RecacheGraphics(_pawn);
                    PawnEditor.Notify_PointsUsed();
                    Close();
                }
            }
            Widgets.EndScrollView();
        }

        // Xenotype Editor button
        var buttonRect = inRect.TakeBottomPart(UIUtility.RegularButtonHeight + 8f);
        buttonRect = buttonRect.ContractedBy(4f);
        if (Widgets.ButtonText(buttonRect, "XenotypeEditor".Translate() + "..."))
        {
            try
            {
                // In-game: pass -1 to avoid StartingPawnUtility.GetGenerationRequest NullRef
                // Pre-colony: pass pawn index for proper xenotype assignment
                var index = PawnEditor.Pregame
                    ? StartingPawnUtility.PawnIndex(_pawn)
                    : -1;
                Find.WindowStack.Add(new Dialog_CreateXenotype(index, delegate
                {
                    CharacterCardUtility.cachedCustomXenotypes = null;
                }));
            }
            catch (System.Exception ex)
            {
                Log.Error($"[Pawn Editor] Failed to open Xenotype Editor: {ex.Message}");
                Messages.Message("Pawn Editor: Xenotype editor failed to open. This may be a vanilla limitation in-game.",
                    MessageTypeDefOf.RejectInput, false);
            }
        }

        // VRE Android Editor button — only shown when VRE Androids is active
        if (VREAndroidCompat.Active)
        {
            var androidRect = inRect.TakeBottomPart(UIUtility.RegularButtonHeight + 4f);
            androidRect = androidRect.ContractedBy(4f);
            if (Widgets.ButtonText(androidRect, "VREA.AndroidEditor".Translate() + "..."))
            {
                VREAndroidCompat.OpenAndroidEditor(_pawn);
            }
        }
    }

    private static List<XenotypeDef> GetXenotypeList(Pawn pawn)
    {
        var list = DefDatabase<XenotypeDef>.AllDefs
            .Where(x => x != null)
            .OrderByDescending(x => x.displayPriority)
            .ToList();

        // Apply HAR restrictions if active
        if (HARCompat.Active && HARCompat.EnforceRestrictions)
            list = list.Where(x => HARCompat.CanUseXenotype(x, pawn)).ToList();

        return list;
    }

    private static AddResult TryApply(XenotypeDef xenotype, Pawn pawn, Action<XenotypeDef> onSelected)
    {
        if (xenotype == null)
            return false;

        return new SuccessInfo(() =>
        {
            onSelected(xenotype);
            PawnEditor.Notify_PointsUsed();
        });
    }

    private static void DrawXenotypeIcon(XenotypeDef xenotype, Rect rect)
    {
        if (xenotype?.Icon != null)
        {
            GUI.color = XenotypeDef.IconColor;
            GUI.DrawTexture(rect, xenotype.Icon);
            GUI.color = Color.white;
        }
        else
        {
            GUI.DrawTexture(rect, Widgets.PlaceholderIconTex);
        }
    }

    private static string GetTooltip(XenotypeDef xenotype)
    {
        if (xenotype == null) return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(xenotype.LabelCap.AsTipTitle());
        sb.AppendLine();

        if (!xenotype.descriptionShort.NullOrEmpty())
            sb.AppendLine(xenotype.descriptionShort);
        else if (!xenotype.description.NullOrEmpty())
            sb.AppendLine(xenotype.description);

        if (xenotype.inheritable)
        {
            sb.AppendLine();
            sb.AppendLine("Inheritable".Translate().Colorize(ColoredText.SubtleGrayColor));
        }

        if (xenotype.genes != null && xenotype.genes.Count > 0)
        {
            sb.AppendLine();
            sb.Append(("Genes".Translate() + ": " + xenotype.genes.Count).Colorize(ColoredText.SubtleGrayColor));
        }

        return sb.ToString();
    }
}
