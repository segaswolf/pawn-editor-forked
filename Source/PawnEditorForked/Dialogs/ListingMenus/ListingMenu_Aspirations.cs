using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Multi-select listing menu for editing VAspirE aspirations.
/// Lets the user keep between 4 and 5 aspirations selected, with rollback on Cancel.
/// </summary>
public class ListingMenu_Aspirations : ListingMenu<Def>
{
    private const int MinAspirations = 4;
    private const int MaxAspirations = 5;

    private readonly Need fulfillmentNeed;

    public ListingMenu_Aspirations(Pawn pawn, Need fulfillmentNeed) : base(
        VAspirECompat.GetAvailableAspirations(pawn, fulfillmentNeed),
        d => d.LabelCap,
        selected => ApplySelection(selected, fulfillmentNeed),
        "PawnEditor.EditAspirations".Translate(),
        new IntRange(MinAspirations, MaxAspirations),
        d => GetTooltip(d, pawn),
        DrawAspirationIcon,
        null,
        pawn,
        "OK".Translate(),
        "Cancel".Translate())
    {
        this.fulfillmentNeed = fulfillmentNeed;

        var currentAspirations = VAspirECompat.GetAspirations(fulfillmentNeed);
        foreach (var aspiration in currentAspirations)
        {
            if (aspiration != null && !Listing.MultiSelected.Contains(aspiration))
                Listing.MultiSelected.Add(aspiration);
        }
    }

    protected override void DrawSelected(ref Rect inRect)
    {
        var count = Listing.MultiSelected?.Count ?? 0;

        string countText = $"Selected: {count}/{MaxAspirations}";
        Color oldColor = GUI.color;

        if (count < MinAspirations || count > MaxAspirations)
            GUI.color = Color.red;
        else
            GUI.color = Color.green;

        Widgets.Label(inRect, countText);
        GUI.color = oldColor;
    }

    private static AddResult ApplySelection(List<Def> selected, Need fulfillmentNeed)
    {
        if (selected == null)
            return false;

        if (selected.Count < MinAspirations)
            return "Select at least 4 aspirations.";

        if (selected.Count > MaxAspirations)
            return "You can select at most 5 aspirations.";

        var current = VAspirECompat.GetAspirations(fulfillmentNeed);

        return new SuccessInfo(() =>
        {
            foreach (var aspiration in current.ToList())
            {
                if (!selected.Contains(aspiration))
                    VAspirECompat.RemoveAspiration(fulfillmentNeed, aspiration);
            }

            foreach (var aspiration in selected)
            {
                if (!current.Contains(aspiration))
                    VAspirECompat.AddAspiration(fulfillmentNeed, aspiration);
            }

            VAspirECompat.SetAspirationCount(fulfillmentNeed, selected.Count);
            VAspirECompat.CheckCompletion(fulfillmentNeed);
        });
    }

    private static void DrawAspirationIcon(Def aspirationDef, Rect rect)
    {
        var icon = VAspirECompat.GetIcon(aspirationDef);
        if (icon != null)
            GUI.DrawTexture(rect, icon);
        else
            GUI.DrawTexture(rect, Widgets.PlaceholderIconTex);
    }

    private static string GetTooltip(Def aspirationDef, Pawn pawn)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(aspirationDef.LabelCap.AsTipTitle());
        sb.AppendLine();

        if (!aspirationDef.description.NullOrEmpty())
            sb.AppendLine(aspirationDef.description.Formatted(pawn.NameShortColored));

        if (!VAspirECompat.IsValidOn(aspirationDef, pawn))
        {
            sb.AppendLine();
            sb.AppendLine("PawnEditor.AspirationNotValid".Translate().Colorize(Color.red));
        }

        return sb.ToString();
    }
}