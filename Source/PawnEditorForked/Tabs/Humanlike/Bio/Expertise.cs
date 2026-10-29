using UnityEngine;
using Verse;

namespace PawnEditor;

// Vanilla Skills Expanded expertise: an "Expertise" button in the skills header opens ListingMenu_Expertise.
// A button and not a row under the skills because the column has no room left at common resolutions.
public partial class TabWorker_Bio_Humanlike
{
    private const float ExpertiseButtonWidth = 80f;

    private static bool ShowExpertise => VSECompat.Active && VSECompat.CanEditExpertise;

    private static void DrawExpertiseButton(Rect header, Pawn pawn)
    {
        if (Widgets.ButtonText(header, "VSE.Expertise".Translate()))
            Find.WindowStack.Add(new ListingMenu_Expertise(pawn));
        TooltipHandler.TipRegion(header, () => ExpertiseTooltip(pawn), 81_734);
    }

    private static string ExpertiseTooltip(Pawn pawn)
    {
        var current = VSECompat.CurrentExpertise(pawn);
        if (current.Count == 0) return "PawnEditor.ExpertiseNone".Translate();
        var lines = new System.Text.StringBuilder();
        foreach (var (def, level) in current) lines.AppendLine($"{def.LabelCap}: {level}");
        return lines.ToString().TrimEnd();
    }
}
