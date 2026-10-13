using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PawnEditor;

// The left panel: the interactive pawn preview, compat buttons (face editor, fur), the sex /
// developmental stage buttons, the source filter and the display toggles.
public partial class Dialog_AppearanceEditor
{
    private const float PreviewTopNudge = 2f;
    private const float MinPreviewSide = 150f;
    private const float PreviewSnapStep = 24f;
    private const float CompatButtonHeight = 28f;
    private const float CompatButtonGap = 2f;
    private const float GapAboveButtonsBlock = 8f;
    private const float GapBelowButtonsBlock = 6f;
    private const float ButtonsBlockHeight = 60f;
    private const float ButtonsBlockPadding = 4f;
    private const float ButtonCellPadding = 2f;
    private const float SourceButtonHeight = 30f;
    private const float SourceRowHeight = SourceButtonHeight + 4f;
    private const float ToggleRowHeight = 30f;
    private const float IgnoreXenotypeRowHeight = 50f;
    private const float CheckboxSize = 24f;
    private const float CheckboxGap = 4f;
    private static readonly Vector2 ButtonIconSize = new(22f, 22f);

    private static float CompatRowHeight => CompatButtonGap + CompatButtonHeight;

    private static float TogglesHeight =>
        ToggleRowHeight * 2f + (ModsConfig.BiotechActive ? IgnoreXenotypeRowHeight : 0f);

    private static float BottomBlockHeight => Text.LineHeight + SourceRowHeight + TogglesHeight;

    /// <summary>
    /// Width of the preview panel as a fraction of the window, clamped so it never dominates a huge
    /// window or starves the options on a small one.
    /// </summary>
    private static float ProportionalLeftWidth(float windowWidth)
    {
        var min = windowWidth <= NarrowBreakpoint ? 240f : 280f;
        var max = windowWidth >= WideBreakpoint ? 480f : 400f;
        return Layout.Proportional(windowWidth, 0.32f, min, max);
    }

    private void DoLeftSection(Rect inRect)
    {
        // Keeps the preview where it was when a lock button sat above it.
        inRect.yMin -= PreviewTopNudge;

        // Reserve the bottom block first: the preview grows with the panel width and would eat it.
        var bottomHeight = Mathf.Min(BottomBlockHeight, inRect.height * 0.5f);
        if (!Layout.TryTakeBottom(ref inRect, bottomHeight, out var bottomBlock)) return;

        DrawPreview(ref inRect);
        DrawCompatButtons(ref inRect);
        DrawSexAndStageRow(inRect);
        DrawSourceAndToggles(bottomBlock);
    }

    private void DrawPreview(ref Rect rect)
    {
        var side = PreviewSide(rect);
        if (!Layout.TryTakeTop(ref rect, side, out var slot)) return;

        var square = new Rect(slot.x + (slot.width - side) / 2f, slot.y, side, side);
        PawnEditor.DrawInteractivePawnPreview(square, pawn, ref draggingPreview, ref pickerPreviewRotation,
            ref pickerPreviewCameraOffset, ref pickerPreviewZoom);
    }

    /// <summary>Square preview side: as wide as the panel, leaving room for the rows below it.</summary>
    private float PreviewSide(Rect rect)
    {
        var neededBelow = GapAboveButtonsBlock + ButtonsBlockHeight + GapBelowButtonsBlock
                          + CompatButtonCount() * CompatRowHeight;
        var side = Mathf.Clamp(rect.width, MinPreviewSide, Mathf.Max(MinPreviewSide, rect.height - neededBelow));

        // PortraitsCache keys by size: snapping stops a new portrait texture on every pixel of a resize.
        return Mathf.Round(side / PreviewSnapStep) * PreviewSnapStep;
    }

    private int CompatButtonCount() =>
        (FacialAnimCompat.CanEditFace(pawn) ? 1 : 0) + (AnthrosonaeCompat.HasFur(pawn) ? 1 : 0);

    private void DrawCompatButtons(ref Rect rect)
    {
        if (FacialAnimCompat.CanEditFace(pawn) && TryTakeCompatButton(ref rect, out var faceButton)
            && Widgets.ButtonText(faceButton, "PawnEditor.CustomizeFace".Translate()))
            FacialAnimCompat.OpenFaceEditor(pawn);

        // Anthrosonae's own patch skips our fork, so we draw its button, with its own translation key.
        if (AnthrosonaeCompat.HasFur(pawn) && TryTakeCompatButton(ref rect, out var furButton)
            && Widgets.ButtonText(furButton, "ColorPicker.ChangeFur".Translate()))
            AnthrosonaeCompat.OpenFurPicker(pawn);
    }

    private static bool TryTakeCompatButton(ref Rect rect, out Rect button)
    {
        if (!Layout.TryTakeTop(ref rect, CompatRowHeight, out button)) return false;

        button.yMin += CompatButtonGap;
        return true;
    }

    private void DrawSexAndStageRow(Rect rect)
    {
        rect.yMin += GapAboveButtonsBlock;
        if (!Layout.TryTakeTop(ref rect, ButtonsBlockHeight, out var row)) return;

        Widgets.DrawHighlight(row);
        row = row.ContractedBy(ButtonsBlockPadding);

        using (new TextBlock(TextAnchor.MiddleCenter))
        {
            DrawSexButton(row.LeftHalf().ContractedBy(ButtonCellPadding));
            if (ModsConfig.BiotechActive)
                DrawDevStageButton(row.RightHalf().ContractedBy(ButtonCellPadding));
        }
    }

    private void DrawSexButton(Rect cell)
    {
        Widgets.DrawHighlightIfMouseover(cell);

        var canChange = pawn.kindDef.fixedGender == null && pawn.RaceProps.hasGenders;
        if (Layout.TryTakeTop(ref cell, UIUtility.RegularButtonHeight, out var button)
            && Widgets.ButtonImageWithBG(button, pawn.gender.GetIcon(), ButtonIconSize) && canChange)
            Find.WindowStack.Add(new FloatMenu(GenderOptions()));

        Widgets.Label(cell, "PawnEditor.Sex".Translate());
    }

    private List<FloatMenuOption> GenderOptions() => new()
    {
        new("Female".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetGender(pawn, Gender.Female),
            GenderUtility.FemaleIcon, Color.white),
        new("Male".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetGender(pawn, Gender.Male),
            GenderUtility.MaleIcon, Color.white)
    };

    private void DrawDevStageButton(Rect cell)
    {
        var label = pawn.DevelopmentalStage.ToString().Translate().CapitalizeFirst();
        if (Mouse.IsOver(cell))
        {
            Widgets.DrawHighlight(cell);
            if (Find.WindowStack.FloatMenu == null)
                TooltipHandler.TipRegion(cell,
                    label.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + "DevelopmentalAgeSelectionDesc".Translate());
        }

        if (Layout.TryTakeTop(ref cell, UIUtility.RegularButtonHeight, out var button)
            && Widgets.ButtonImageWithBG(button, pawn.DevelopmentalStage.Icon().Texture, ButtonIconSize))
            Find.WindowStack.Add(new FloatMenu(DevStageOptions()));

        Widgets.Label(cell, label);
    }

    private List<FloatMenuOption> DevStageOptions() => new()
    {
        DevStageOption("Adult", DevelopmentalStage.Adult, DevelopmentalStageExtensions.AdultTex.Texture),
        DevStageOption("Child", DevelopmentalStage.Child, DevelopmentalStageExtensions.ChildTex.Texture),
        DevStageOption("Baby", DevelopmentalStage.Baby, DevelopmentalStageExtensions.BabyTex.Texture)
    };

    private FloatMenuOption DevStageOption(string key, DevelopmentalStage stage, Texture2D icon) =>
        new(key.Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetDevStage(pawn, stage), icon, Color.white);

    private void DrawSourceAndToggles(Rect block)
    {
        if (!Layout.TryTakeBottom(ref block, TogglesHeight, out var toggles)) return;

        DrawSourceFilter(block);
        DrawDisplayToggles(toggles);
    }

    private void DrawSourceFilter(Rect rect)
    {
        using (new TextBlock(GameFont.Tiny))
        {
            if (Layout.TryTakeTop(ref rect, Text.LineHeight, out var labelRow))
                Widgets.Label(labelRow, "Source".Translate().CapitalizeFirst());
        }

        if (mainTab == MainTab.HAR || !Layout.TryTakeTop(ref rect, SourceButtonHeight, out var buttonRow)) return;

        var label = sourceFilter?.Name ?? "PawnEditor.All".Translate().CapitalizeFirst();
        if (Widgets.ButtonText(buttonRow.ContractedBy(3), label))
            Find.WindowStack.Add(new Dialog_ListPicker("PawnEditor.ChooseSource".Translate(),
                Dialog_ListPicker.ModSources(SourceCandidatesForCurrentTab(), sourceFilter, mod => sourceFilter = mod)));
    }

    /// <summary>Stacked from the bottom up; shown on every tab, Cosmetic genes included.</summary>
    private void DrawDisplayToggles(Rect rect)
    {
        if (ModsConfig.BiotechActive && Layout.TryTakeBottom(ref rect, IgnoreXenotypeRowHeight, out var ignoreRow))
            DrawIgnoreXenotypeToggle(ignoreRow);
        if (Layout.TryTakeBottom(ref rect, ToggleRowHeight, out var apparelRow))
            Widgets.CheckboxLabeled(apparelRow, "PawnEditor.ShowApparel".Translate(), ref PawnEditor.RenderClothes);
        if (Layout.TryTakeBottom(ref rect, ToggleRowHeight, out var headgearRow))
            Widgets.CheckboxLabeled(headgearRow, "PawnEditor.ShowHeadgear".Translate(), ref PawnEditor.RenderHeadgear);
    }

    /// <summary>
    /// "Ignore xenotype restrictions": lets heads, body types and cosmetic genes the pawn's xenotype or
    /// race would not allow be picked anyway.
    /// </summary>
    /// <remarks>
    /// Clicked through <c>Widgets.ButtonInvisible</c> (a plain GUI button) instead of
    /// <c>Widgets.CheckboxLabeled</c>. The Cosmetic genes tab used to have its own copy of this
    /// setting drawn with CheckboxLabeled, and it ignored clicks there while the gene buttons next to
    /// it, which are plain GUI buttons, worked. The cause was not identified (no error was logged);
    /// CheckboxLabeled goes through ButtonInvisibleDraggable, which depends on Mouse.IsOver and on the
    /// control ID sequence, so this uses the path that is known to work in that tab.
    /// </remarks>
    private void DrawIgnoreXenotypeToggle(Rect rect)
    {
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - CheckboxSize - CheckboxGap, rect.height), "PawnEditor.IgnoreXenotype".Translate());
        Widgets.CheckboxDraw(rect.xMax - CheckboxSize, rect.y + (rect.height - CheckboxSize) / 2f, ignoreXenotype, false);
        Widgets.DrawHighlightIfMouseover(rect);
        TooltipHandler.TipRegion(rect, "PawnEditor.IgnoreXenotypeDesc".Translate());

        if (!Widgets.ButtonInvisible(rect)) return;
        ignoreXenotype = !ignoreXenotype;
        (ignoreXenotype ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
    }
}
