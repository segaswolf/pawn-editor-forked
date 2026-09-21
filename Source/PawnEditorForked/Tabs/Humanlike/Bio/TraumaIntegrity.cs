using UnityEngine;
using Verse;

namespace PawnEditor;

public partial class TabWorker_Bio_Humanlike
{
    private Pawn traumaIntegrityBufferPawn;
    private string traumaBuffer;
    private string integrityBuffer;
    private string graceDaysBuffer;
    private float traumaValue;
    private float integrityValue;
    private float graceDaysValue;

    private const float TraumaRowHeight = 30f;

    /// <summary>
    /// Upper bound for the countdown field, in in-game days.
    ///
    /// Taken from Trauma and Integrity's own RollBetrayal, which schedules betrayal at
    /// <c>TicksGame + Rand.Range(0, 36000000)</c>. At 60,000 ticks per day that is 600 days, so this
    /// covers everything the mod itself can roll.
    /// </summary>
    private const float MaxGraceDays = 600f;

    private void DrawTraumaIntegrityControls(ref Rect rect, Pawn pawn)
    {
        SyncTraumaIntegrityBuffers(pawn);

        // Rect.TakeTopPart hands back a full-height row even when the rect has less room left than that,
        // so the row simply hangs past the bottom of the panel. This section is the LAST thing in the
        // Groups column, so when space ran out the Betrayer row — and the tooltip region registered for
        // it — ended up sitting on top of the bottom buttons. Players reported the "hidden gameplay
        // state / eligible for betrayal" tooltip popping up over the Start button on the character
        // creation screen. Every row below is now drawn only if it actually fits.
        if (!Layout.TryTakeTop(ref rect, Text.LineHeight, out var headerRow)) return;

        Widgets.Label(headerRow,
            "PawnEditor.Development.TraumaIntegrity".Translate().Colorize(ColoredText.TipSectionTitleColor));
        rect.yMin += 4f;

        var traumaLabel = "PawnEditor.Development.TraumaPercent".Translate().ToString();
        var stateLabel = "PawnEditor.Development.TraumaState".Translate().ToString();
        var integrityLabel = "PawnEditor.Development.IntegrityPercent".Translate().ToString();
        var betrayerLabel = "PawnEditor.Development.Betrayer".Translate().ToString();
        var graceLabel = "PawnEditor.Development.BetrayalIn".Translate().ToString();
        var labelWidth = UIUtility.ColumnWidth(4f, traumaLabel, stateLabel, integrityLabel, betrayerLabel, graceLabel);

        // Layout.TryTakeTop refuses to hand back a row that doesn't fit, which is what kept the
        // Betrayer checkbox from being drawn on top of the bottom buttons.
        if (Layout.TryTakeTop(ref rect, TraumaRowHeight, out var traumaRow)) DrawTraumaControl(traumaRow, pawn, traumaLabel, labelWidth);
        if (Layout.TryTakeTop(ref rect, TraumaRowHeight, out var stateRow)) DrawTraumaState(stateRow, pawn, stateLabel, labelWidth);
        if (Layout.TryTakeTop(ref rect, TraumaRowHeight, out var integrityRow)) DrawIntegrityControl(integrityRow, pawn, integrityLabel, labelWidth);
        if (Layout.TryTakeTop(ref rect, TraumaRowHeight, out var betrayerRow)) DrawBetrayerControl(betrayerRow, pawn, betrayerLabel);

        // Only meaningful for a pawn already flagged as a betrayer: the mod stores WHEN the betrayal
        // fires, not just whether it will. Without this the player could see that a pawn will turn but
        // had no way to see or change when.
        if (!TraumaIntegrityCompat.IsBetrayer(pawn)) return;
        if (Layout.TryTakeTop(ref rect, TraumaRowHeight, out var graceRow)) DrawGraceDaysControl(graceRow, pawn, graceLabel, labelWidth);
    }

    private void DrawTraumaControl(Rect row, Pawn pawn, string label, float labelWidth)
    {
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

    private static void DrawTraumaState(Rect row, Pawn pawn, string label, float labelWidth)
    {
        var trauma = Mathf.Clamp01(TraumaIntegrityCompat.GetTrauma(pawn));
        var state = TraumaIntegrityCompat.IsTempered(pawn)
            ? "PawnEditor.Development.Tempered".Translate()
            : trauma > 0.5f
                ? "PawnEditor.Development.Disturbed".Translate()
                : "PawnEditor.Development.Innocent".Translate();
        var tooltipRect = row;
        using (new TextBlock(TextAnchor.MiddleLeft))
        {
            Widgets.Label(row.TakeLeftPart(labelWidth), label);
            Widgets.Label(row, state);
        }
        TooltipHandler.TipRegion(tooltipRect, "PawnEditor.Development.TraumaApplyDesc".Translate());
    }

    private void DrawIntegrityControl(Rect row, Pawn pawn, string label, float labelWidth)
    {
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

    /// <summary>
    /// Days of grace left before a flagged betrayer turns, editable. Mirrors the trauma and integrity
    /// rows: a number field plus Apply, so nothing is committed by simply typing.
    /// </summary>
    private void DrawGraceDaysControl(Rect row, Pawn pawn, string label, float labelWidth)
    {
        var tooltipRect = row;
        var applyRect = row.TakeRightPart(58f);
        var unitRect = row.TakeRightPart(18f);
        var valueRect = row.TakeRightPart(58f);

        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(row.TakeLeftPart(labelWidth), label);
        Widgets.TextFieldNumeric(valueRect, ref graceDaysValue, ref graceDaysBuffer, 0f, MaxGraceDays);
        using (new TextBlock(TextAnchor.MiddleCenter))
            Widgets.Label(unitRect, "PawnEditor.Development.DaysShort".Translate());

        if (Widgets.ButtonText(applyRect, "PawnEditor.Apply".Translate())
            && TraumaIntegrityCompat.SetGraceDaysRemaining(pawn, graceDaysValue))
        {
            SyncTraumaIntegrityBuffers(pawn, true);
            PawnEditor.Notify_PointsUsed();
        }

        TooltipHandler.TipRegion(tooltipRect, "PawnEditor.Development.BetrayalInDesc".Translate());
    }

    private void DrawBetrayerControl(Rect row, Pawn pawn, string label)
    {
        var betrayer = TraumaIntegrityCompat.IsBetrayer(pawn);
        Widgets.CheckboxLabeled(row, label, ref betrayer, placeCheckboxNearText: true);
        if (betrayer != TraumaIntegrityCompat.IsBetrayer(pawn)
            && TraumaIntegrityCompat.SetBetrayer(pawn, betrayer))
        {
            // Flipping this rewrites the betrayal tick, so the countdown row below has to be re-read
            // rather than keep showing the value from before the toggle.
            SyncTraumaIntegrityBuffers(pawn, true);
            PawnEditor.Notify_PointsUsed();
        }

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
        graceDaysValue = Mathf.Clamp(TraumaIntegrityCompat.GetGraceDaysRemaining(pawn), 0f, MaxGraceDays);
        traumaBuffer = traumaValue.ToString("0.#");
        integrityBuffer = integrityValue.ToString("0.#");
        graceDaysBuffer = graceDaysValue.ToString("0.#");
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
