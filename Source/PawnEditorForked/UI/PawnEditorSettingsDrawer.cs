using System;
using System.Linq;
using Verse;
using UnityEngine;

namespace PawnEditor;

public sealed class PawnEditorSettingsDrawer
{
    private readonly PawnEditorHotkeyService _hotkeyService;

    public PawnEditorSettingsDrawer(PawnEditorHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
    }

    public void Draw(Rect inRect, PawnEditorSettings settings)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        listing.CheckboxLabeled(
            "PawnEdtior.OverrideVanilla".Translate(),
            ref settings.OverrideVanilla,
            "PawnEditor.OverrideVanilla.Desc".Translate());

        listing.CheckboxLabeled(
            "PawnEditor.InGameDevButton".Translate(),
            ref settings.InGameDevButton,
            "PawnEditor.InGameDevButton.Desc".Translate());

        // Point limit: editable text box + slider. The text box lets the user type an exact
        // value (requested over the slider-only approach, which was imprecise for large numbers),
        // while the slider stays for quick coarse adjustment. Both write the same setting.
        listing.Label("PawnEditor.PointLimit".Translate() + ": " + settings.PointLimit.ToStringMoney());
        var pointLimitRow = listing.GetRect(Text.LineHeight);
        var pointLimitBuffer = settings.PointLimit.ToString("0");
        Widgets.TextFieldNumeric(pointLimitRow.LeftPart(0.35f), ref settings.PointLimit, ref pointLimitBuffer, 100f, 1000000000f);
        settings.PointLimit = listing.Slider(settings.PointLimit, 100f, 10000000f);

        listing.CheckboxLabeled(
            "PawnEditor.UseSilver".Translate(),
            ref settings.UseSilver,
            "PawnEditor.UseSilver.Desc".Translate());

        listing.CheckboxLabeled(
            "PawnEditor.CountNPCs".Translate(),
            ref settings.CountNPCs,
            "PawnEditor.CountNPCs.Desc".Translate());

        listing.CheckboxLabeled(
            "PawnEditor.ShowEditButton".Translate(),
            ref settings.ShowOpenButton,
            "PawnEditor.ShowEditButton.Desc".Translate());

        if (listing.ButtonTextLabeled(
                "PawnEditor.HediffLocation".Translate(),
                ("PawnEditor.HediffLocation." + settings.HediffLocationLimit).Translate()))
        {
            Find.WindowStack.Add(CreateHediffLocationMenu(settings));
        }

        if (settings.DontShowAgain.Count > 0 && listing.ButtonText("PawnEditor.ResetConfirmation".Translate()))
        {
            settings.DontShowAgain.Clear();
        }

        listing.CheckboxLabeled(
            "PawnEditor.EnforceHARRestrictions".Translate(),
            ref HARCompat.EnforceRestrictions,
            "PawnEditor.EnforceHARRestrictions.Desc".Translate());

        listing.CheckboxLabeled(
            "PawnEditor.HideRandomFactions".Translate(),
            ref settings.HideFactions,
            "PawnEditor.HideRandomFactions.Desc".Translate());

        listing.CheckboxLabeled(
            "PawnEditor.AllowPolygamyOnLoad".Translate(),
            ref settings.AllowPolygamyOnLoad,
            "PawnEditor.AllowPolygamyOnLoad.Desc".Translate());

        listing.CheckboxLabeled(
            "PawnEditor.RememberWindowPositions".Translate(),
            ref settings.RememberWindowPositions,
            "PawnEditor.RememberWindowPositions.Desc".Translate());

        listing.CheckboxLabeled(
            "PawnEditor.AllowIllegalPlacements".Translate(),
            ref settings.AllowIllegalPlacements,
            "PawnEditor.AllowIllegalPlacements.Desc".Translate());

        _hotkeyService.DrawHotkeyPicker(listing, settings);

        // ── Debug: profiler ("banderitas") ──
        // Opt-in instrumentation for diagnosing memory/GC pressure. Gated behind Dev Mode so regular
        // players never see it (and it never runs for them — see PawnEditorProfiler.Enabled). Plain
        // text labels (no translation keys) since this is a developer/diagnostic tool, not player UI.
        if (Prefs.DevMode)
        {
            listing.GapLine();
            listing.CheckboxLabeled(
                "Enable performance profiler (debug)",
                ref settings.ProfilingEnabled,
                "Logs timing and memory-allocation 'flags' for editor events to help diagnose lag and " +
                "black-screen issues. Dev-Mode only. Use the buttons below to reset and dump the " +
                "collected stats while diagnosing.");

            if (settings.ProfilingEnabled)
            {
                var profRow = listing.GetRect(Text.LineHeight + 4f);
                if (Widgets.ButtonText(profRow.LeftHalf().ContractedBy(2f, 0f), "Reset profiler stats"))
                    PawnEditorProfiler.Reset();
                if (Widgets.ButtonText(profRow.RightHalf().ContractedBy(2f, 0f), "Dump profiler summary to log"))
                    PawnEditorProfiler.DumpSummary();
            }
        }

        listing.End();
    }

    private static FloatMenu CreateHediffLocationMenu(PawnEditorSettings settings)
    {
        var options = Enum.GetValues(typeof(PawnEditorSettings.HediffLocation))
            .Cast<PawnEditorSettings.HediffLocation>()
            .Select(loc => new FloatMenuOption(
                ("PawnEditor.HediffLocation." + loc).Translate(),
                delegate { settings.HediffLocationLimit = loc; }))
            .ToList();

        return new FloatMenu(options);
    }
}