using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using RimUI;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[UsedImplicitly]
public class Dialog_EditThing : Dialog_EditItem<Thing>
{
    // Grid columns each field spans in the listing (relative width, not pixels).
    private const int FieldColumns = 6;
    private const float ColorSwatchSize = 24f;
    private const float PersonaTraitsHeight = 60f;
    private const float PersonaTraitRowHeight = 22f;
    private const float AddTraitButtonHeight = 30f;
    private const float RandomizeNameButtonWidth = 30f;

    private string _buffer;

    public Dialog_EditThing(Thing item, Pawn pawn = null, UIElement element = null) : base(item, pawn, element) => _buffer = "";

    public override void Select(Thing item)
    {
        base.Select(item);
        _buffer = "";
    }

    protected override void DoContents(Listing_Horizontal listing)
    {
        var apparel = Selected as Apparel;
        if (Selected.def.MadeFromStuff) DrawStuffButton(listing);
        if (Selected.TryGetComp<CompQuality>() is { } quality) DrawQualityButton(listing, quality);
        if (apparel != null && Selected.TryGetComp<CompColorable>() != null) DrawColorPicker(listing, apparel);
        if (StyleOptionsFor(Selected.def) is { } styles) DrawStyleButton(listing, styles);
        DrawHitPointsSlider(listing);
        if (apparel != null) DrawTaintedCheckbox(listing, apparel);
        if (Selected.def.stackLimit > 1) DrawStackCount(listing);
        if (Selected.TryGetComp<CompBladelinkWeapon>() is { } bladelink) DrawPersonaTraits(listing, bladelink);
        if (Selected.TryGetComp<CompGeneratedNames>() is { } names) DrawGeneratedName(listing, names);
    }

    private void DrawStuffButton(Listing_Horizontal listing)
    {
        if (!listing.ButtonDefLabeled("StatsReport_Material".Translate(), Selected.Stuff, FieldColumns)) return;
        Find.WindowStack.Add(new FloatMenu(GenStuff.AllowedStuffsFor(Selected.def)
            .Select(stuff => new FloatMenuOption(stuff.LabelCap, () => SetStuff(stuff), Widgets.GetIconFor(stuff), stuff.uiIconColor))
            .ToList()));
    }

    private void SetStuff(ThingDef stuff)
    {
        Selected.SetStuffDirect(stuff);
        Selected.SetColor(stuff.stuffProps.color);
        Selected.Notify_ColorChanged();
        PawnEditor.Notify_PointsUsed();
    }

    private static void DrawQualityButton(Listing_Horizontal listing, CompQuality compQuality)
    {
        if (!listing.ButtonTextLabeled("Quality".Translate(), compQuality.Quality.GetLabel().CapitalizeFirst(), FieldColumns)) return;
        Find.WindowStack.Add(new FloatMenu(QualityUtility.AllQualityCategories
            .Select(quality => new FloatMenuOption(quality.GetLabel().CapitalizeFirst(), () =>
            {
                compQuality.SetQuality(quality, ArtGenerationContext.Outsider);
                PawnEditor.Notify_PointsUsed();
            }))
            .ToList()));
    }

    private void DrawColorPicker(Listing_Horizontal listing, Apparel apparel)
    {
        var buttonRect = listing.RectLabeled("Color".Translate(), FieldColumns);
        var swatchRect = buttonRect.TakeRightPart(ColorSwatchSize);
        buttonRect.xMax -= 4f;
        swatchRect.height = ColorSwatchSize;
        swatchRect.y += 3f;
        var currentColor = apparel.DrawColor;
        if (Widgets.ButtonText(buttonRect, "PawnEditor.PickColor".Translate()))
            Find.WindowStack.Add(new Dialog_ColorPicker(color => apparel.SetColor(color),
                DefDatabase<ColorDef>.AllDefs.Select(cd => cd.color).ToList(), currentColor, SpecialColorsFor(apparel)));

        Widgets.DrawRectFast(swatchRect, currentColor);
    }

    /// <summary>The "Default" and "Favorite" shortcuts shown under the palette.</summary>
    private Dictionary<string, Color> SpecialColorsFor(Apparel apparel)
    {
        var defaultColor = apparel.Stuff != null
            ? apparel.Stuff.stuffProps.color
            : apparel.def.colorGenerator?.NewRandomizedColor() ?? Color.white;
        var colors = new Dictionary<string, Color> { { "PawnEditor.DefaultColor".Translate(), defaultColor } };
        // Pawn may be null: the dialog can edit an item nobody carries.
        if (Pawn?.story?.favoriteColor is { } favorite) colors.Add("PawnEditor.FavoriteColor".Translate(), favorite.color);
        return colors;
    }

    /// <summary>Styles available for the def, or null when it has none.</summary>
    private static Dictionary<ThingStyleDef, StyleCategoryDef> StyleOptionsFor(ThingDef def) =>
        ListingMenu_Items.ThingStyles.FirstOrDefault(ts => ts.ThingDef == def).StyleDefs;

    private void DrawStyleButton(Listing_Horizontal listing, Dictionary<ThingStyleDef, StyleCategoryDef> styles)
    {
        var currentStyle = styles.FirstOrDefault(style => style.Key == Selected.GetStyleDef()).Value;
        if (!listing.ButtonDefLabeled("Stat_Thing_StyleLabel".Translate(), currentStyle)) return;
        Find.WindowStack.Add(new FloatMenu(styles
            .Select(style => new FloatMenuOption(style.Value.LabelCap, () => SetStyle(style.Key), style.Value.Icon, Color.white))
            .Append(new("None".Translate(), () => SetStyle(null)))
            .ToList()));
    }

    private void SetStyle(ThingStyleDef style)
    {
        Selected.SetStyleDef(style);
        Selected.Notify_ColorChanged();
    }

    private void DrawHitPointsSlider(Listing_Horizontal listing)
    {
        float maxHitPoints = Selected.MaxHitPoints;
        var hitPoints = (int)listing.SliderLabeled("HitPointsBasic".Translate().CapitalizeFirst(), Selected.HitPoints, 1, maxHitPoints,
            (Selected.HitPoints / maxHitPoints).ToStringPercent(), null, null, FieldColumns);
        if (hitPoints == Selected.HitPoints) return;
        Selected.HitPoints = hitPoints;
        PawnEditor.Notify_PointsUsed();
    }

    private static void DrawTaintedCheckbox(Listing_Horizontal listing, Apparel apparel)
    {
        var isTainted = apparel.WornByCorpse;
        listing.CheckboxLabeled("PawnEditor.Tainted".Translate(), ref isTainted, FieldColumns);
        if (apparel.WornByCorpse == isTainted) return;
        apparel.WornByCorpse = isTainted;
        apparel.Notify_ColorChanged();
        PawnEditor.Notify_PointsUsed();
    }

    private void DrawStackCount(Listing_Horizontal listing)
    {
        var fieldRect = listing.RectLabeled("PenFoodTab_Count".Translate(), FieldColumns);
        var countBefore = Selected.stackCount;
        UIUtility.IntField(fieldRect, ref Selected.stackCount, 1, Selected.def.stackLimit, ref _buffer, true);
        if (countBefore != Selected.stackCount) PawnEditor.Notify_PointsUsed();
    }

    private void DrawPersonaTraits(Listing_Horizontal listing, CompBladelinkWeapon bladelink)
    {
        var traitsRect = listing.RectLabeled("Traits".Translate(), height: PersonaTraitsHeight);
        string addLabel = "Add".Translate().CapitalizeFirst() + " " + "Trait".Translate();
        var addRect = traitsRect.TakeRightPart(Text.CalcSize(addLabel).x + 48).TopPartPixels(AddTraitButtonHeight);
        if (Widgets.ButtonText(addRect, addLabel))
            Find.WindowStack.Add(new FloatMenu(DefDatabase<WeaponTraitDef>.AllDefs
                .Select(traitDef => new FloatMenuOption(traitDef.LabelCap, () => TryAddPersonaTrait(bladelink, traitDef)))
                .ToList()));

        traitsRect.yMin += 4f;
        GenUI.DrawElementStack(traitsRect, PersonaTraitRowHeight, bladelink.traits,
            (rect, traitDef) => DrawPersonaTrait(rect, bladelink, traitDef),
            traitDef => Text.CalcSize(traitDef.LabelCap).x + 10f, 5f);
    }

    private void TryAddPersonaTrait(CompBladelinkWeapon bladelink, WeaponTraitDef traitDef)
    {
        if (!bladelink.CanAddTrait(traitDef))
        {
            Messages.Message("PawnEditor.TraitDisallowedByKind".Translate(traitDef.label, Selected.Label), MessageTypeDefOf.RejectInput);
            return;
        }

        bladelink.traits.Add(traitDef);
        PawnEditor.Notify_PointsUsed();
    }

    private static void DrawPersonaTrait(Rect rect, CompBladelinkWeapon bladelink, WeaponTraitDef traitDef)
    {
        GUI.color = CharacterCardUtility.StackElementBackground;
        GUI.DrawTexture(rect, BaseContent.WhiteTex);
        GUI.color = Color.white;
        var hovered = Mouse.IsOver(rect);
        if (hovered) Widgets.DrawHighlight(rect);
        Widgets.Label(new(rect.x + 5f, rect.y, rect.width - 10f, rect.height), traitDef.LabelCap);
        if (!hovered) return;

        TooltipHandler.TipRegion(rect, traitDef.description);
        if (!Widgets.ButtonImage(rect.RightPartPixels(rect.height).ContractedBy(4), TexButton.Delete)) return;
        bladelink.traits.Remove(traitDef);
        PawnEditor.Notify_PointsUsed();
    }

    private static void DrawGeneratedName(Listing_Horizontal listing, CompGeneratedNames names)
    {
        var fieldRect = listing.RectLabeled("PawnEditor.Name".Translate(), FieldColumns, grow: false);
        if (Widgets.ButtonImage(fieldRect.TakeRightPart(RandomizeNameButtonWidth).ContractedBy(4f), TexPawnEditor.Randomize))
            names.Initialize(names.Props);
        names.name = Widgets.TextField(fieldRect, names.name);
    }
}
