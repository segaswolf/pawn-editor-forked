using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// The Facial Animation tab (optional mod): face part options and their colours.
public partial class Dialog_AppearanceEditor
{
    private void DoFacialAnimationOptions(Rect inRect)
    {
        var parts = FacialAnimCompat.GetFaceParts().ToList();
        if (parts.Count == 0)
            return;

        if (facialAnimationPart == null || !parts.Contains(facialAnimationPart))
            facialAnimationPart = parts[0];

        subTabs.Clear();
        foreach (var part in parts)
        {
            subTabs.Add(new TabRecord(part.Label, () =>
            {
                facialAnimationPart = part;
                scrollPos = Vector2.zero;
            }, facialAnimationPart == part));
        }

        TabDrawer.DrawTabs(inRect, subTabs, 400f);
        inRect.yMin += TabDrawer.TabHeight + 6f;

        if (facialAnimationPart.Key == "eyes")
        {
            DrawFacialAnimationColor(inRect.TakeTopPart(36f).ContractedBy(2f), "PawnEditor.FA.EyeColor".Translate(),
                FacialAnimCompat.GetEyeColor(pawn), color => FacialAnimCompat.SetEyeColor(pawn, color));
            inRect.yMin += 4f;
            DrawFacialAnimationColor(inRect.TakeTopPart(36f).ContractedBy(2f), "PawnEditor.FA.EyeSecondColor".Translate(),
                FacialAnimCompat.GetSecondEyeColor(pawn), color => FacialAnimCompat.SetSecondEyeColor(pawn, color));
            inRect.yMin += 8f;
        }

        var options = FacialAnimCompat.GetApplicableFaceTypeDefs(pawn, facialAnimationPart)
            .Where(MatchesSource)
            .ToList();
        DoIconOptions(inRect, options, def => FacialAnimCompat.SetFaceType(pawn, facialAnimationPart, def),
            def => FacialAnimCompat.GetFaceTypeIcon(def, pawn),
            def => FacialAnimCompat.GetFaceType(pawn, facialAnimationPart) == def,
            0, Array.Empty<Color>(), null, ColorType.Misc, null, 5,
            facialAnimationPart.Key == "eyes" ? new Rect(0.34f, 0.315f, 0.32f, 0.27f) : null);
    }

    private static void DrawFacialAnimationColor(Rect row, string label, Color? current, Action<Color> onSelected)
    {
        var labelRect = row.TakeLeftPart(Mathf.Min(150f, row.width * 0.36f));
        Widgets.Label(labelRect, label);
        row.xMin += 8f;
        var color = current ?? Color.white;
        var swatch = row.TakeRightPart(36f).ContractedBy(4f);
        Widgets.DrawBoxSolid(swatch, color);
        Widgets.DrawBox(swatch);

        if (Widgets.ButtonText(row, "PawnEditor.PickColor".Translate()))
            Find.WindowStack.Add(new Dialog_ColorPicker(onSelected, DefDatabase<ColorDef>.AllDefs.Select(def => def.color).ToList(), color));
    }
}
