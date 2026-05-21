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

        listing.Label("PawnEditor.PointLimit".Translate() + ": " + settings.PointLimit.ToStringMoney());
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

        _hotkeyService.DrawHotkeyPicker(listing, settings);

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