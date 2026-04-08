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
        // Custom xenotypes button — opens a dedicated listing
        var customXenotypes = CharacterCardUtility.CustomXenotypes;
        if (customXenotypes != null && customXenotypes.Count > 0)
        {
            var customBtnRect = inRect.TakeBottomPart(UIUtility.RegularButtonHeight + 4f);
            customBtnRect = customBtnRect.ContractedBy(4f);
            if (Widgets.ButtonText(customBtnRect, "PawnEditor.CustomXenotypes".Translate() + " (" + customXenotypes.Count + ")..."))
            {
                Find.WindowStack.Add(new ListingMenu_CustomXenotypes(_pawn));
            }
        }

        // Xenotype Editor button
        var buttonRect = inRect.TakeBottomPart(UIUtility.RegularButtonHeight + 4f);
        buttonRect = buttonRect.ContractedBy(4f);
        if (Widgets.ButtonText(buttonRect, "XenotypeEditor".Translate() + "..."))
        {
            try
            {
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
