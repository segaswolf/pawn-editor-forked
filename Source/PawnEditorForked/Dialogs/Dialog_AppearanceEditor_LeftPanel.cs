using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// The left panel: the interactive pawn preview, compat buttons (face editor, fur), the sex /
// developmental stage / xenotype buttons, the source filter and the display toggles.
public partial class Dialog_AppearanceEditor
{
    /// <summary>
    /// Width of the preview panel as a fraction of the window, clamped so it never dominates a huge
    /// window or starves the options on a small one. This is what makes the editor feel responsive:
    /// resize the window from its corner and the pawn preview grows/shrinks with it.
    /// </summary>
    private static float ProportionalLeftWidth(float windowWidth)
    {
        // ~32% of the window, but held between a readable minimum and a cap so options keep their room.
        var min = windowWidth <= NarrowBreakpoint ? 240f : 280f;
        var max = windowWidth >= WideBreakpoint ? 480f : 400f;
        return Layout.Proportional(windowWidth, 0.32f, min, max);
    }

    private void DoLeftSection(Rect inRect)
    {
        // The panel used to start 30px higher to fit the lock button (24px + 4px gap) under the title.
        // With the button gone, -2px keeps the preview exactly where it was before.
        inRect.yMin -= 2f;

        // The preview scales with the panel width, capped so the controls below still fit. Square and
        // centered to avoid stretching the pawn.
        // Reserve the WHOLE bottom block (Source + the three checkboxes) before sizing anything else.
        // This is the real fix for "Source lands on top of Show headgear": the preview grows with the
        // panel WIDTH, so widening the window made it taller and ate every pixel below it. Reserving
        // afterwards was useless, because by then the space was already gone.
        var bottomBlock = inRect.TakeBottomPart(Mathf.Min(
            Text.LineHeight + 34f + 60f + (ModsConfig.BiotechActive ? 50f : 0f),
            inRect.height * 0.5f));

        // Cap the preview by what is ACTUALLY free below it (face button + the sex/age/xenotype block)
        // instead of a blind 55% of the height, which didn't account for those.
        var neededBelow = 8f + 110f + 6f
            + (FacialAnimCompat.CanEditFace(pawn) ? 30f : 0f)
            + (AnthrosonaeCompat.HasFur(pawn) ? 30f : 0f);
        var previewSide = Mathf.Clamp(inRect.width, 150f, Mathf.Max(150f, inRect.height - neededBelow));
        // Snap to 24px steps so resizing the window doesn't render a NEW portrait texture every pixel
        // (PortraitsCache keys by size; unrounded sizes = per-frame RenderTexture churn = the old GC/
        // black-screen problem). The portrait only re-renders when it crosses a step.
        previewSide = Mathf.Round(previewSide / 24f) * 24f;
        var slot = inRect.TakeTopPart(previewSide);
        PawnEditor.DrawInteractivePawnPreview(
            new Rect(slot.x + (slot.width - previewSide) / 2f, slot.y, previewSide, previewSide),
            pawn, ref draggingPreview, ref pickerPreviewRotation, ref pickerPreviewCameraOffset, ref pickerPreviewZoom);

        // v3.1: open NL Facial Animation's own face editor for this pawn, if that mod is present.
        if (FacialAnimCompat.CanEditFace(pawn))
        {
            inRect.yMin += 2f;
            // Opens on a higher window layer, so it sits in FRONT of this window and the main editor
            // (both can stay open behind it).
            if (Widgets.ButtonText(inRect.TakeTopPart(28f), "PawnEditor.CustomizeFace".Translate()))
                FacialAnimCompat.OpenFaceEditor(pawn);
        }

        // Restore Anthrosonae's "Change fur" button (their own patch skips our fork; see AnthrosonaeCompat).
        // Uses their translation key so the wording matches their mod exactly.
        if (AnthrosonaeCompat.HasFur(pawn))
        {
            inRect.yMin += 2f;
            if (Widgets.ButtonText(inRect.TakeTopPart(28f), "ColorPicker.ChangeFur".Translate()))
                AnthrosonaeCompat.OpenFurPicker(pawn);
        }

        inRect.yMin += 8f;
        var buttonsRect = inRect.TakeTopPart(110);
        Widgets.DrawHighlight(buttonsRect);
        buttonsRect = buttonsRect.ContractedBy(4);

        using (new TextBlock(TextAnchor.MiddleCenter))
        {
            var sexRect = buttonsRect.TopHalf().LeftHalf().ContractedBy(2);
            Widgets.DrawHighlightIfMouseover(sexRect);

            if (Widgets.ButtonImageWithBG(sexRect.TakeTopPart(UIUtility.RegularButtonHeight), pawn.gender.GetIcon(), new Vector2(22f, 22f))
                && pawn.kindDef.fixedGender == null && pawn.RaceProps.hasGenders)
            {
                var list = new List<FloatMenuOption>
                {
                    new("Female".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetGender(pawn, Gender.Female), GenderUtility.FemaleIcon,
                        Color.white),
                    new("Male".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetGender(pawn, Gender.Male), GenderUtility.MaleIcon, Color.white)
                };

                Find.WindowStack.Add(new FloatMenu(list));
            }

            Widgets.Label(sexRect, "PawnEditor.Sex".Translate());

            TaggedString text;
            if (ModsConfig.BiotechActive)
            {
                var devStageRect = buttonsRect.TopHalf().RightHalf().ContractedBy(2);
                text = pawn.DevelopmentalStage.ToString().Translate().CapitalizeFirst();
                if (Mouse.IsOver(devStageRect))
                {
                    Widgets.DrawHighlight(devStageRect);
                    if (Find.WindowStack.FloatMenu == null)
                        TooltipHandler.TipRegion(devStageRect,
                            text.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + "DevelopmentalAgeSelectionDesc".Translate());
                }

                if (Widgets.ButtonImageWithBG(devStageRect.TakeTopPart(UIUtility.RegularButtonHeight), pawn.DevelopmentalStage.Icon().Texture,
                        new Vector2(22f, 22f)))
                {
                    var options = new List<FloatMenuOption>
                    {
                        new("Adult".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetDevStage(pawn, DevelopmentalStage.Adult),
                            DevelopmentalStageExtensions.AdultTex.Texture, Color.white),
                        new("Child".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetDevStage(pawn, DevelopmentalStage.Child),
                            DevelopmentalStageExtensions.ChildTex.Texture, Color.white),
                        new("Baby".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetDevStage(pawn, DevelopmentalStage.Baby),
                            DevelopmentalStageExtensions.BabyTex.Texture, Color.white)
                    };
                    Find.WindowStack.Add(new FloatMenu(options));
                }

                Widgets.Label(devStageRect, text);

                var xenotypeRect = buttonsRect.BottomHalf().LeftHalf().ContractedBy(2);
                text = pawn.genes.XenotypeLabelCap;
                if (Mouse.IsOver(xenotypeRect))
                {
                    Widgets.DrawHighlight(xenotypeRect);
                    if (Find.WindowStack.FloatMenu == null)
                        TooltipHandler.TipRegion(xenotypeRect, text.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + "XenotypeSelectionDesc".Translate());
                }


                if (Widgets.ButtonImageWithBG(xenotypeRect.TakeTopPart(UIUtility.RegularButtonHeight), pawn.genes.XenotypeIcon, new Vector2(22f, 22f)))
                {
                    var list = new List<FloatMenuOption>();
                    foreach (var item in DefDatabase<XenotypeDef>.AllDefs.Where(x => x != pawn.genes.xenotype).OrderBy(x => 0f - x.displayPriority))
                    {
                        var xenotype = item;
                        list.Add(new(xenotype.LabelCap,
                            () => { SetXenotype(xenotype); }, xenotype.Icon, XenotypeDef.IconColor, MenuOptionPriority.Default,
                            r => TooltipHandler.TipRegion(r, xenotype.descriptionShort ?? xenotype.description), null, 24f,
                            r => Widgets.InfoCardButton(r.x, r.y + 3f, xenotype), extraPartRightJustified: true));
                    }

                    foreach (var customXenotype in CharacterCardUtility.CustomXenotypes.Where(x => x != pawn.genes.CustomXenotype))
                    {
                        var customInner = customXenotype;
                        list.Add(new(customInner.name.CapitalizeFirst() + " (" + "Custom".Translate() + ")",
                            delegate
                            {
                                // Use customInner (the per-iteration copy), NOT customXenotype: the loop
                                // variable is shared across all delegates, so referencing it directly would
                                // apply whichever xenotype was LAST in the loop, not the one clicked.
                                ApplyCustomXenotype(customInner);
                            }, customInner.IconDef.Icon, XenotypeDef.IconColor, MenuOptionPriority.Default, null, null, 24f, delegate(Rect r)
                            {
                                if (Widgets.ButtonImage(new(r.x, r.y + (r.height - r.width) / 2f, r.width, r.width), TexButton.Delete, GUI.color))
                                {
                                    Find.WindowStack.Add(new Dialog_Confirm("ConfirmDelete".Translate(customInner.name.CapitalizeFirst()), "ConfirmDeleteXenotype",
                                        delegate
                                        {
                                            var path = GenFilePaths.AbsFilePathForXenotype(customInner.name);
                                            if (File.Exists(path))
                                            {
                                                File.Delete(path);
                                                CharacterCardUtility.cachedCustomXenotypes = null;
                                            }
                                        }, true));
                                    return true;
                                }

                                return false;
                            }, extraPartRightJustified: true));
                    }

                    list.Add(new("XenotypeEditor".Translate() + "...",
                        delegate { Find.WindowStack.Add(new Dialog_CreateXenotype(-1, delegate { CharacterCardUtility.cachedCustomXenotypes = null; })); }));

                    Find.WindowStack.Add(new FloatMenu(list));
                }

                Widgets.Label(xenotypeRect, text.Truncate(xenotypeRect.width));
            }
        }

        inRect.yMin += 6;

        // Draw into the block reserved at the top of this method, never into whatever happens to be
        // left over: that is what guarantees these can't collide no matter how the window is resized.
        var bottomRect = bottomBlock.TakeBottomPart(60f + (ModsConfig.BiotechActive ? 50f : 0f));

        using (new TextBlock(GameFont.Tiny)) Widgets.Label(bottomBlock.TakeTopPart(Text.LineHeight), "Source".Translate().CapitalizeFirst());

        if (mainTab != MainTab.HAR && bottomBlock.height >= 30f
            && Widgets.ButtonText(bottomBlock.TakeTopPart(30).ContractedBy(3), sourceFilter?.Name ?? "PawnEditor.All".Translate().CapitalizeFirst()))
        {
            var allDefs = SourceCandidatesForCurrentTab();
            var options = LoadedModManager.RunningMods.Intersect(allDefs.Select(def => def.modContentPack).Distinct())
                .Where(x => x != null)
                .Select(mod => new FloatMenuOption(mod.Name, () => sourceFilter = mod))
                .Prepend(new(
                    "PawnEditor.All".Translate().CapitalizeFirst(), () => sourceFilter = null))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        if (ModsConfig.BiotechActive && mainTab != MainTab.Xenotype)
            Widgets.CheckboxLabeled(bottomRect.TakeBottomPart(50), "PawnEditor.IgnoreXenotype".Translate(), ref ignoreXenotype);
        Widgets.CheckboxLabeled(bottomRect.TakeBottomPart(30), "PawnEditor.ShowApparel".Translate(), ref PawnEditor.RenderClothes);
        Widgets.CheckboxLabeled(bottomRect.TakeBottomPart(30), "PawnEditor.ShowHeadgear".Translate(), ref PawnEditor.RenderHeadgear);
    }
}
