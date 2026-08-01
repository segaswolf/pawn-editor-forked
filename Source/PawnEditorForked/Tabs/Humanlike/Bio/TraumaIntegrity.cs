using UnityEngine;
using Verse;

namespace PawnEditor;

public partial class TabWorker_Bio_Humanlike
{
    private Pawn traumaIntegrityBufferPawn;
    private string traumaBuffer;
    private string integrityBuffer;
    private float traumaValue;
    private float integrityValue;

    private void DrawTraumaIntegrityControls(ref Rect rect, Pawn pawn)
    {
        SyncTraumaIntegrityBuffers(pawn);

        Widgets.Label(
            rect.TakeTopPart(Text.LineHeight),
            "PawnEditor.Development.TraumaIntegrity".Translate().Colorize(ColoredText.TipSectionTitleColor));
        rect.yMin += 4f;

        var traumaLabel = "PawnEditor.Development.TraumaPercent".Translate().ToString();
        var stateLabel = "PawnEditor.Development.TraumaState".Translate().ToString();
        var integrityLabel = "PawnEditor.Development.IntegrityPercent".Translate().ToString();
        var betrayerLabel = "PawnEditor.Development.Betrayer".Translate().ToString();
        var labelWidth = UIUtility.ColumnWidth(4f, traumaLabel, stateLabel, integrityLabel, betrayerLabel);

        DrawTraumaControl(ref rect, pawn, traumaLabel, labelWidth);
        DrawTraumaState(ref rect, pawn, stateLabel, labelWidth);
        DrawIntegrityControl(ref rect, pawn, integrityLabel, labelWidth);
        DrawBetrayerControl(ref rect, pawn, betrayerLabel);
    }

    private void DrawTraumaControl(ref Rect rect, Pawn pawn, string label, float labelWidth)
    {
        var row = rect.TakeTopPart(30f);
        var tooltipRect = row;
        var applyRect = row.TakeRightPart(58f);
        var percentRect = row.TakeRightPart(18f);
        var valueRect = row.TakeRightPart(58f);

        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(row.TakeLeftPart(labelWidth), label);
        Widgets.TextFieldNumeric(valueRect, ref traumaValue, ref traumaBuffer, 0f, 100f);
        using (new TextBlock(TextAnchor.MiddleCenter))
            Widgets.Label(percentRect, "%");

        if (Widgets.ButtonText(applyRect, "PawnEditor.Apply".Translate())
            && TraumaIntegrityCompat.SetTrauma(pawn, traumaValue / 100f))
        {
            SyncTraumaIntegrityBuffers(pawn, true);
            PawnEditor.Notify_PointsUsed();
        }
        TooltipHandler.TipRegion(tooltipRect, GetTraumaTooltip());
    }

    private static void DrawTraumaState(ref Rect rect, Pawn pawn, string label, float labelWidth)
    {
        var trauma = Mathf.Clamp01(TraumaIntegrityCompat.GetTrauma(pawn));
        var state = TraumaIntegrityCompat.IsTempered(pawn)
            ? "PawnEditor.Development.Tempered".Translate()
            : trauma > 0.5f
                ? "PawnEditor.Development.Disturbed".Translate()
                : "PawnEditor.Development.Innocent".Translate();
        var row = rect.TakeTopPart(30f);
        var tooltipRect = row;
        using (new TextBlock(TextAnchor.MiddleLeft))
        {
            Widgets.Label(row.TakeLeftPart(labelWidth), label);
            Widgets.Label(row, state);
        }
        TooltipHandler.TipRegion(tooltipRect, "PawnEditor.Development.TraumaApplyDesc".Translate());
    }

    private void DrawIntegrityControl(ref Rect rect, Pawn pawn, string label, float labelWidth)
    {
        var row = rect.TakeTopPart(30f);
        var tooltipRect = row;
        var applyRect = row.TakeRightPart(58f);
        var percentRect = row.TakeRightPart(18f);
        var valueRect = row.TakeRightPart(58f);

        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(row.TakeLeftPart(labelWidth), label);
        Widgets.TextFieldNumeric(valueRect, ref integrityValue, ref integrityBuffer, 0f, 100f);
        using (new TextBlock(TextAnchor.MiddleCenter))
            Widgets.Label(percentRect, "%");

        if (Widgets.ButtonText(applyRect, "PawnEditor.Apply".Translate())
            && TraumaIntegrityCompat.SetIntegrity(pawn, integrityValue / 100f))
        {
            SyncTraumaIntegrityBuffers(pawn, true);
            PawnEditor.Notify_PointsUsed();
        }
        TooltipHandler.TipRegion(tooltipRect, GetIntegrityTooltip(pawn));
    }

    private static void DrawBetrayerControl(ref Rect rect, Pawn pawn, string label)
    {
        var betrayer = TraumaIntegrityCompat.IsBetrayer(pawn);
        var row = rect.TakeTopPart(30f);
        Widgets.CheckboxLabeled(row, label, ref betrayer, placeCheckboxNearText: true);
        if (betrayer != TraumaIntegrityCompat.IsBetrayer(pawn)
            && TraumaIntegrityCompat.SetBetrayer(pawn, betrayer))
            PawnEditor.Notify_PointsUsed();
        TooltipHandler.TipRegion(row, "PawnEditor.Development.BetrayerDesc".Translate());
    }

    private void SyncTraumaIntegrityBuffers(Pawn pawn, bool force = false)
    {
        if (!force && traumaIntegrityBufferPawn == pawn)
            return;

        traumaIntegrityBufferPawn = pawn;
        traumaValue = Mathf.Clamp01(TraumaIntegrityCompat.GetTrauma(pawn)) * 100f;
        var integrity = TraumaIntegrityCompat.GetIntegrity(pawn);
        integrityValue = integrity < 0f ? 0f : Mathf.Clamp01(integrity) * 100f;
        traumaBuffer = traumaValue.ToString("0.#");
        integrityBuffer = integrityValue.ToString("0.#");
    }

    private static string GetTraumaTooltip()
    {
        var tooltip = "PawnEditor.Development.TraumaApplyDesc".Translate().ToString();
        if (!TraumaIntegrityCompat.TraumaEnabled)
            tooltip += "\n\n" + "PawnEditor.Development.TraumaDisabled".Translate();
        return tooltip;
    }

    private static string GetIntegrityTooltip(Pawn pawn)
    {
        var tooltip = "PawnEditor.Development.IntegrityApplyDesc".Translate().ToString();
        if (!TraumaIntegrityCompat.IntegrityEnabled)
            tooltip += "\n\n" + "PawnEditor.Development.IntegrityDisabled".Translate();
        if (TraumaIntegrityCompat.IntegrityHidden)
            tooltip += "\n\n" + "PawnEditor.Development.IntegrityHidden".Translate();
        if (TraumaIntegrityCompat.GetIntegrity(pawn) < 0f)
            tooltip += "\n\n" + "PawnEditor.Development.IntegrityUninitialized".Translate();
        return tooltip;
    }
}
