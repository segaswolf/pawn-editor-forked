using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// The left column of the Bio tab: traits, "incapable of", non-psycast abilities and extras.
public partial class TabWorker_Bio_Humanlike
{
    private const float StackMinHeight = 45f;
    private const float StackPadding = 6f;
    private const float StackExtraHeight = 14f;
    private const float SectionAddButtonWidth = 60f;
    private const float ExtrasRowHeight = 30f;
    private const float LeftListBottomSlack = 56f;
    private const float AbilityIconSize = 32f;

    private static string AddLabel => "Add".Translate().CapitalizeFirst();

    private void DoLeft(Rect inRect, Pawn pawn)
    {
        if (leftLastHeight == 0) leftLastHeight = inRect.height;
        var viewRect = new Rect(0, 0, inRect.width - 20, leftLastHeight);
        Widgets.BeginScrollView(inRect, ref leftScrollPos, viewRect);
        try
        {
            // TakeTopPart, not Layout: the view grows to whatever these sections measured last frame.
            DrawTraitsSection(ref viewRect, pawn);
            DrawIncapableSection(ref viewRect, pawn);
            DrawAbilitiesSection(ref viewRect, pawn);
            DrawExtrasSection(ref viewRect, pawn);
            leftLastHeight = Text.LineHeight * 4 + traitsLastHeight + incapableLastHeight + abilitiesLastHeight
                             + ExtrasRowHeight + LeftListBottomSlack;
        }
        finally
        {
            Widgets.EndScrollView();
        }
    }

    /// <summary>Section title. Returns the header row so a caller can carve an Add button out of it.</summary>
    private static Rect DrawStackHeader(ref Rect viewRect, string title)
    {
        var header = viewRect.TakeTopPart(Text.LineHeight);
        Widgets.Label(header, title.Colorize(ColoredText.TipSectionTitleColor));
        return header;
    }

    private static Rect TakeStackRect(ref Rect viewRect, float lastHeight) =>
        viewRect.TakeTopPart(lastHeight + StackExtraHeight).ContractedBy(StackPadding);

    /// <summary>Greyed "None" (or similar) for an empty section. Returns the height it used.</summary>
    private static float DrawEmptyStack(Rect rect, string label)
    {
        GUI.color = Color.gray;
        if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
        Widgets.Label(rect, label);
        TooltipHandler.TipRegionByKey(rect, "None");
        return Text.LineHeight;
    }

    private static void DrawStackElementBackground(Rect rect)
    {
        GUI.color = CharacterCardUtility.StackElementBackground;
        GUI.DrawTexture(rect, BaseContent.WhiteTex);
        GUI.color = Color.white;
    }

    // ── Traits ──

    private void DrawTraitsSection(ref Rect viewRect, Pawn pawn)
    {
        var header = DrawStackHeader(ref viewRect, "Traits".Translate());
        if (Widgets.ButtonText(header.TakeRightPart(SectionAddButtonWidth), AddLabel))
            Find.WindowStack.Add(new ListingMenu_Trait(pawn));

        var rect = TakeStackRect(ref viewRect, traitsLastHeight);
        var traits = pawn.story.traits.allTraits;
        traitsLastHeight = traits.NullOrEmpty()
            ? DrawEmptyStack(rect, pawn.DevelopmentalStage.Baby() ? "TraitsDevelopLaterBaby".Translate() : "None".Translate())
            : DrawTraitStack(rect, pawn, traits);
        traitsLastHeight = Mathf.Max(StackMinHeight, traitsLastHeight);
        GUI.color = Color.white;
    }

    /// <remarks>
    /// <para>
    /// Reads allTraits, not TraitsSorted: TraitsSorted hid some active traits (a trader's Mute), which
    /// the editor then refused to re-add. Suppressed traits go last.
    /// </para>
    /// </remarks>
    private static float DrawTraitStack(Rect rect, Pawn pawn, List<Trait> traits) =>
        GenUI.DrawElementStack(rect, 24, traits.OrderByDescending(t => t.Suppressed ? 0 : 1).ToList(),
            (r, trait) => DrawTraitElement(r, trait, pawn), WidthGetter, 4f, 5f, false).height;

    private static void DrawTraitElement(Rect rect, Trait trait, Pawn pawn)
    {
        DrawStackElementBackground(rect);
        if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

        if (trait.Suppressed) GUI.color = ColoredText.SubtleGrayColor;
        else if (trait.sourceGene != null) GUI.color = ColoredText.GeneColor;
        Widgets.Label(new Rect(rect.x + 5f, rect.y, rect.width - 10f, rect.height), trait.LabelCap);

        if (!Mouse.IsOver(rect)) return;
        TooltipHandler.TipRegion(rect, () => trait.TipString(pawn), rect.GetHashCode());
        if (Widgets.ButtonImage(rect.RightPartPixels(rect.height).ContractedBy(4), TexButton.Delete))
            ListingMenu_Trait.RemoveTraitAndRefresh(pawn, trait);
    }

    private static float WidthGetter(Trait trait) => Text.CalcSize(trait.LabelCap).x + 10f;

    // ── Incapable of ──

    private void DrawIncapableSection(ref Rect viewRect, Pawn pawn)
    {
        DrawStackHeader(ref viewRect, "IncapableOf".Translate());
        var rect = TakeStackRect(ref viewRect, incapableLastHeight);
        var disabledTags = pawn.CombinedDisabledWorkTags;
        incapableLastHeight = disabledTags == WorkTags.None
            ? DrawEmptyStack(rect, "None".Translate())
            : GenUI.DrawElementStack(rect, 22f, CharacterCardUtility.WorkTagsFrom(disabledTags).ToList(),
                (r, tag) => DrawWorkTagElement(r, tag, pawn), WorkTagWidth, 5f).height;
        incapableLastHeight = Mathf.Max(StackMinHeight, incapableLastHeight);
        GUI.color = Color.white;
    }

    private static void DrawWorkTagElement(Rect rect, WorkTags tag, Pawn pawn)
    {
        DrawStackElementBackground(rect);
        GUI.color = CharacterCardUtility.GetDisabledWorkTagLabelColor(pawn, tag);
        if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
        Widgets.Label(new Rect(rect.x + 5f, rect.y, rect.width - 10f, rect.height), tag.LabelTranslated().CapitalizeFirst());

        if (Mouse.IsOver(rect))
            TooltipHandler.TipRegion(rect, () => CharacterCardUtility.GetWorkTypeDisabledCausedBy(pawn, tag) + "\n"
                + CharacterCardUtility.GetWorkTypesDisabledByWorkTag(tag), rect.GetHashCode());
    }

    private static float WorkTagWidth(WorkTags tag) => Text.CalcSize(tag.LabelTranslated().CapitalizeFirst()).x + 10f;

    // ── Abilities (psycasts have their own tab) ──

    private void DrawAbilitiesSection(ref Rect viewRect, Pawn pawn)
    {
        var header = DrawStackHeader(ref viewRect, "Abilities".Translate());
        if (Widgets.ButtonText(header.TakeRightPart(SectionAddButtonWidth), AddLabel))
            Find.WindowStack.Add(new ListingMenu_Abilities(pawn, ListingMenu_Abilities.AbilityListMode.NonPsycasts));

        var abilities = pawn.abilities.abilities
            .Where(a => !a.def.IsPsycast)
            .OrderBy(a => a.def.level).ThenBy(a => a.def.EntropyGain)
            .ToList();
        var rect = TakeStackRect(ref viewRect, abilitiesLastHeight);
        abilitiesLastHeight = abilities.Count == 0
            ? DrawEmptyStack(rect, "None".Translate())
            : GenUI.DrawElementStack(rect, AbilityIconSize, abilities, (r, ability) => DrawAbilityElement(r, ability, pawn),
                _ => AbilityIconSize).height;
        abilitiesLastHeight = Mathf.Max(StackMinHeight, abilitiesLastHeight);
        GUI.color = Color.white;
    }

    private static void DrawAbilityElement(Rect rect, Ability ability, Pawn pawn)
    {
        if (Mouse.IsOver(rect))
        {
            Widgets.DrawHighlight(rect);
            if (Widgets.ButtonImage(rect.TopPart(0.3f).RightPart(0.3f), TexButton.Delete))
                pawn.abilities.RemoveAbility(ability.def);
        }

        if (Widgets.ButtonImage(rect, ability.def.uiIcon, false))
            Find.WindowStack.Add(new Dialog_InfoCard(ability.def));

        if (Mouse.IsOver(rect))
            TooltipHandler.TipRegion(rect, () => ability.Tooltip + "\n\n" + "ClickToLearnMore".Translate().Colorize(ColoredText.SubtleGrayColor),
                rect.GetHashCode());
    }

    // ── Extras ──

    private static void DrawExtrasSection(ref Rect viewRect, Pawn pawn)
    {
        DrawStackHeader(ref viewRect, "PawnEditor.Extras".Translate());
        DrawFavoriteColorRow(viewRect.TakeTopPart(ExtrasRowHeight), pawn);
    }

    private static void DrawFavoriteColorRow(Rect row, Pawn pawn)
    {
        row.xMin += 6f;
        string label = "PawnEditor.FavColor".Translate();
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(row.TakeLeftPart(Text.CalcSize(label).x + 4), label);

        var current = pawn.story.favoriteColor?.color ?? Color.white;
        Widgets.DrawBoxSolid(row.TakeRightPart(30).ContractedBy(2.5f), current);
        if (Widgets.ButtonText(row, "PawnEditor.PickColor".Translate()))
            Find.WindowStack.Add(new Dialog_ColorPicker(color => SetFavoriteColor(pawn, color),
                DefDatabase<ColorDef>.AllDefs.Select(colorDef => colorDef.color).ToList(), current));
    }

    /// <summary>FIX #002: picks the nearest ColorDef instead of mutating a shared one.</summary>
    private static void SetFavoriteColor(Pawn pawn, Color color)
    {
        var nearest = DefDatabase<ColorDef>.AllDefsListForReading
            .Where(colorDef => colorDef != null)
            .OrderBy(colorDef => ColorDistanceSquared(colorDef.color, color))
            .FirstOrDefault();
        if (nearest != null)
            pawn.story.favoriteColor = nearest;
    }

    private static float ColorDistanceSquared(Color a, Color b)
    {
        float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
        return dr * dr + dg * dg + db * db;
    }
}
