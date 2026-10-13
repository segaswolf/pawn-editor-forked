using System.Collections.Generic;
using System.Linq;
using RimUI;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// The Bio tab's top-right buttons: developmental stage, xenotype, sex, body shape, appearance editor.
public partial class TabWorker_Bio_Humanlike
{
    private const float margin = 6f;
    // Grid columns each button spans in the listing (relative width, not pixels).
    private const int ButtonColumns = 6;

    private Listing_Horizontal listing = new();
    private float height = 999f;

    public TabWorker_Bio_Humanlike()
    {
        listing.InlineSpacing = 4f;
        listing.BlockSpacing = 8f;
    }

    private void DoButtons(ref Rect buttonsRect, Pawn pawn)
    {
        using var block = new TextBlock(TextAnchor.MiddleCenter);
        var outerRect = buttonsRect.TakeTopPart(height + margin);
        Widgets.DrawHighlight(outerRect);
        buttonsRect = outerRect.ContractedBy(margin);
        listing.Begin(buttonsRect);

        if (ModsConfig.BiotechActive)
        {
            DrawDevStageButton(pawn);
            DrawXenotypeButton(pawn);
        }

        DrawSexButton(pawn);
        if (listing.ButtonImageLabeledVStack("PawnEditor.Shape".Translate(), TexPawnEditor.GetBodyTypeIcon(pawn.story.bodyType), ButtonColumns))
            Find.WindowStack.Add(new FloatMenu(BodyTypeMenuOptions(pawn)));
        if (listing.ButtonText("PawnEditor.EditAppearance".Translate(), ButtonColumns))
            Find.WindowStack.Add(new Dialog_AppearanceEditor(pawn));

        listing.End();
        height = listing.curHeight;
    }

    private void DrawDevStageButton(Pawn pawn)
    {
        string label = pawn.DevelopmentalStage.ToString().Translate().CapitalizeFirst();
        var tip = label.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + "DevelopmentalAgeSelectionDesc".Translate();
        if (listing.ButtonImageLabeledVStack(label, pawn.DevelopmentalStage.Icon().Texture, ButtonColumns, tip))
            Find.WindowStack.Add(new FloatMenu(DevStageMenuOptions(pawn)));
    }

    /// <summary>Only the stages this race actually has (v3d10).</summary>
    private static List<FloatMenuOption> DevStageMenuOptions(Pawn pawn)
    {
        var options = new List<FloatMenuOption>();
        AddDevStageOption(options, pawn, DevelopmentalStage.Adult, "Adult".Translate(), DevelopmentalStageExtensions.AdultTex.Texture);
        AddDevStageOption(options, pawn, DevelopmentalStage.Child, "Child".Translate(), DevelopmentalStageExtensions.ChildTex.Texture);
        AddDevStageOption(options, pawn, DevelopmentalStage.Baby, "Baby".Translate(), DevelopmentalStageExtensions.BabyTex.Texture);
        return options;
    }

    private static void AddDevStageOption(List<FloatMenuOption> options, Pawn pawn, DevelopmentalStage stage, string label, Texture2D icon)
    {
        if (pawn.RaceProps.lifeStageAges.Any(lifeStage => lifeStage.def.developmentalStage == stage))
            options.Add(new FloatMenuOption(label.CapitalizeFirst(), () => ConfirmAndSetDevStage(pawn, stage), icon, Color.white));
    }

    private void DrawXenotypeButton(Pawn pawn)
    {
        var label = pawn.genes.XenotypeLabelCap;
        var tip = label.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + "XenotypeSelectionDesc".Translate();
        if (listing.ButtonImageLabeledVStack(label, pawn.genes.XenotypeIcon, ButtonColumns, tip))
            OpenXenotypePicker(pawn);
    }

    private void DrawSexButton(Pawn pawn)
    {
        if (listing.ButtonImageLabeledVStack("PawnEditor.Sex".Translate(), pawn.gender.GetIcon(), ButtonColumns)
            && pawn.kindDef.fixedGender == null
            && pawn.RaceProps.hasGenders)
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new("Female".Translate().CapitalizeFirst(), () => SetGender(pawn, Gender.Female), GenderUtility.FemaleIcon, Color.white),
                new("Male".Translate().CapitalizeFirst(), () => SetGender(pawn, Gender.Male), GenderUtility.MaleIcon, Color.white)
            }));
    }

    /// <summary>Sets the pawn's gender and keeps body type and (with HAR) head type consistent with it.</summary>
    public static void SetGender(Pawn pawn, Gender gender)
    {
        pawn.gender = gender;
        if (pawn.story.bodyType == BodyTypeDefOf.Female && gender == Gender.Male) pawn.story.bodyType = BodyTypeDefOf.Male;
        if (pawn.story.bodyType == BodyTypeDefOf.Male && gender == Gender.Female) pawn.story.bodyType = BodyTypeDefOf.Female;

        // HAR doesn't like head types that don't match the gender.
        if (HARCompat.Active && pawn.story.headType.gender != gender
                             && !pawn.story.TryGetRandomHeadFromSet(HARCompat.FilterHeadTypes(DefDatabase<HeadTypeDef>.AllDefs, pawn)))
            Log.Warning("Failed to find head type for " + pawn);

        RecacheGraphics(pawn);
    }

    /// <summary>
    /// Opens the xenotype listing (search, custom xenotypes, editor). Shared by the Bio tab's button and
    /// the appearance editor's xenotype row, so both behave the same.
    /// </summary>
    public static void OpenXenotypePicker(Pawn pawn) =>
        Find.WindowStack.Add(new ListingMenu_Xenotypes(pawn, xenotype => ChooseXenotype(pawn, xenotype)));

    /// <summary>Applies the xenotype; if the pawn already has genes, asks first whether to reset them all.</summary>
    private static void ChooseXenotype(Pawn pawn, XenotypeDef xenotype)
    {
        var hasExistingGenes = pawn.genes.GenesListForReading.Count > 0;
        if (!hasExistingGenes || xenotype == pawn.genes.Xenotype)
        {
            SetXenotype(pawn, xenotype);
            RecacheGraphics(pawn);
            return;
        }

        Find.WindowStack.Add(new Dialog_Confirm(
            "PawnEditor.XenotypeChangeWarning".Translate(pawn.genes.XenotypeLabelCap, xenotype.LabelCap)
            + "\n\n" + "PawnEditor.ResetGenesToBase".Translate(),
            "XenotypeChangeConfirm",
            () =>
            {
                ResetAndSetXenotype(pawn, xenotype);
                RecacheGraphics(pawn);
            },
            destructive: true));
    }

    /// <summary>
    /// The Shape button's menu: the body types the appearance editor offers, ordinary ones first,
    /// mod-internal ones last with a warning in their label and tooltip.
    /// </summary>
    private static List<FloatMenuOption> BodyTypeMenuOptions(Pawn pawn) =>
        BodyTypeRules.For(pawn)
            .OrderBy(bodyType => BodyTypeRules.IsSpecial(bodyType, pawn))
            .Select(bodyType => BodyTypeOption(pawn, bodyType))
            .ToList();

    private static FloatMenuOption BodyTypeOption(Pawn pawn, BodyTypeDef bodyType)
    {
        var label = BodyTypeRules.LabelFor(bodyType).CapitalizeFirst();
        var warning = BodyTypeRules.WarningFor(bodyType, pawn);
        if (warning != null) label = "PawnEditor.SpecialBodyType".Translate(label);
        return new FloatMenuOption(label, () =>
            {
                pawn.story.bodyType = bodyType;
                RecacheGraphics(pawn);
            }, TexPawnEditor.GetBodyTypeIcon(bodyType), Color.white,
            mouseoverGuiAction: warning == null ? null : rect => TooltipHandler.TipRegion(rect, warning));
    }

    /// <summary>Thin wrapper over <see cref="PawnEditor.RefreshPawnGraphics"/>, kept so its many callers stay as they are.</summary>
    public static void RecacheGraphics(Pawn pawn) => PawnEditor.RefreshPawnGraphics(pawn);
}
