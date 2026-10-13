using System;
using System.Linq;
using Verse;
using UnityEngine;

namespace PawnEditor;

public sealed class PawnEditorSettingsDrawer
{
    private const float MinPointLimit = 100f;
    private const float MaxTypedPointLimit = 1000000000f;
    private const float MaxSliderPointLimit = 10000000f;
    private const float PointLimitFieldPct = 0.35f;

    private readonly PawnEditorHotkeyService _hotkeyService;

    public PawnEditorSettingsDrawer(PawnEditorHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
    }

    public void Draw(Rect inRect, PawnEditorSettings settings)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        DrawOpeningToggles(listing, settings);
        DrawPointLimit(listing, settings);
        DrawPointToggles(listing, settings);
        DrawEditorToggles(listing, settings);
        DrawHediffLocationButton(listing, settings);
        DrawResetConfirmationsButton(listing, settings);
        DrawBehaviorToggles(listing, settings);
        _hotkeyService.DrawHotkeyPicker(listing, settings);
        if (Prefs.DevMode) DrawProfilerControls(listing, settings);

        listing.End();
    }

    private static void DrawOpeningToggles(Listing_Standard listing, PawnEditorSettings settings)
    {
        // The key's typo is the one defined in Settings.xml; renaming it would break translations.
        listing.CheckboxLabeled("PawnEdtior.OverrideVanilla".Translate(), ref settings.OverrideVanilla,
            "PawnEditor.OverrideVanilla.Desc".Translate());
        listing.CheckboxLabeled("PawnEditor.InGameDevButton".Translate(), ref settings.InGameDevButton,
            "PawnEditor.InGameDevButton.Desc".Translate());
    }

    /// <summary>Text box for an exact value plus a slider for quick changes. Both write the same setting.</summary>
    private static void DrawPointLimit(Listing_Standard listing, PawnEditorSettings settings)
    {
        listing.Label("PawnEditor.PointLimit".Translate() + ": " + settings.PointLimit.ToStringMoney());
        var fieldRow = listing.GetRect(Text.LineHeight);
        var buffer = settings.PointLimit.ToString("0");
        Widgets.TextFieldNumeric(fieldRow.LeftPart(PointLimitFieldPct), ref settings.PointLimit, ref buffer, MinPointLimit, MaxTypedPointLimit);
        settings.PointLimit = listing.Slider(settings.PointLimit, MinPointLimit, MaxSliderPointLimit);
    }

    private static void DrawPointToggles(Listing_Standard listing, PawnEditorSettings settings)
    {
        listing.CheckboxLabeled("PawnEditor.UseSilver".Translate(), ref settings.UseSilver,
            "PawnEditor.UseSilver.Desc".Translate());
        listing.CheckboxLabeled("PawnEditor.CountNPCs".Translate(), ref settings.CountNPCs,
            "PawnEditor.CountNPCs.Desc".Translate());
    }

    private static void DrawEditorToggles(Listing_Standard listing, PawnEditorSettings settings)
    {
        listing.CheckboxLabeled("PawnEditor.ShowEditButton".Translate(), ref settings.ShowOpenButton,
            "PawnEditor.ShowEditButton.Desc".Translate());
        listing.CheckboxLabeled("PawnEditor.PauseWhileEditorOpen".Translate(), ref settings.PauseWhileEditorOpen,
            "PawnEditor.PauseWhileEditorOpen.Desc".Translate());
    }

    private static void DrawHediffLocationButton(Listing_Standard listing, PawnEditorSettings settings)
    {
        if (listing.ButtonTextLabeled("PawnEditor.HediffLocation".Translate(),
                ("PawnEditor.HediffLocation." + settings.HediffLocationLimit).Translate()))
            Find.WindowStack.Add(CreateHediffLocationMenu(settings));
    }

    private static void DrawResetConfirmationsButton(Listing_Standard listing, PawnEditorSettings settings)
    {
        if (settings.DontShowAgain.Count > 0 && listing.ButtonText("PawnEditor.ResetConfirmation".Translate()))
            settings.DontShowAgain.Clear();
    }

    private static void DrawBehaviorToggles(Listing_Standard listing, PawnEditorSettings settings)
    {
        listing.CheckboxLabeled("PawnEditor.EnforceHARRestrictions".Translate(), ref HARCompat.EnforceRestrictions,
            "PawnEditor.EnforceHARRestrictions.Desc".Translate());
        listing.CheckboxLabeled("PawnEditor.HideRandomFactions".Translate(), ref settings.HideFactions,
            "PawnEditor.HideRandomFactions.Desc".Translate());
        listing.CheckboxLabeled("PawnEditor.AllowPolygamyOnLoad".Translate(), ref settings.AllowPolygamyOnLoad,
            "PawnEditor.AllowPolygamyOnLoad.Desc".Translate());
        listing.CheckboxLabeled("PawnEditor.Animations".Translate(), ref settings.Animations,
            "PawnEditor.Animations.Desc".Translate());
        listing.CheckboxLabeled("PawnEditor.RememberWindowPositions".Translate(), ref settings.RememberWindowPositions,
            "PawnEditor.RememberWindowPositions.Desc".Translate());
        listing.CheckboxLabeled("PawnEditor.AllowIllegalPlacements".Translate(), ref settings.AllowIllegalPlacements,
            "PawnEditor.AllowIllegalPlacements.Desc".Translate());
    }

    /// <summary>Dev Mode only, so plain English text instead of translation keys.</summary>
    private static void DrawProfilerControls(Listing_Standard listing, PawnEditorSettings settings)
    {
        listing.GapLine();
        listing.CheckboxLabeled("Enable performance profiler (debug)", ref settings.ProfilingEnabled,
            "Logs timing and memory-allocation 'flags' for editor events to help diagnose lag and " +
            "black-screen issues. Dev-Mode only. Use the buttons below to reset and dump the " +
            "collected stats while diagnosing.");
        if (!settings.ProfilingEnabled) return;

        var buttonRow = listing.GetRect(Text.LineHeight + 4f);
        if (Widgets.ButtonText(buttonRow.LeftHalf().ContractedBy(2f, 0f), "Reset profiler stats"))
            PawnEditorProfiler.Reset();
        if (Widgets.ButtonText(buttonRow.RightHalf().ContractedBy(2f, 0f), "Dump profiler summary to log"))
            PawnEditorProfiler.DumpSummary();
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
