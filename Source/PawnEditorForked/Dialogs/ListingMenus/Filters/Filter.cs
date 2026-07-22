using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

public abstract class Filter<T>
{
    public readonly string Description;
    public readonly bool EnabledByDefault;
    public readonly string Label;
    public bool Inverted;

    protected Filter(string label, bool enabledByDefault = false, string description = null)
    {
        Label = label;
        EnabledByDefault = enabledByDefault;
        Description = description;
    }

    protected virtual float Height => UIUtility.RegularButtonHeight * 2 + 8;

    public bool DrawFilter(ref Rect inRect)
    {
        var rowHeight = Height;
        //if (_filterType == TFilter<>.FilterType.Toggle) { rowHeight -= (UIUtility.RegularButtonHeight + 4); }

        var filterRect = inRect.TakeTopPart(rowHeight);

        // Grey background
        GUI.color = CharacterCardUtility.StackElementBackground;
        GUI.DrawTexture(filterRect, BaseContent.WhiteTex);
        GUI.color = Color.white;
        filterRect = filterRect.ContractedBy(6f);

        // Filter widget
        DrawWidget(filterRect.TakeBottomPart(UIUtility.RegularButtonHeight));
        filterRect.yMax -= 4f;

        // Filter info
        var topRowRect = filterRect.TakeTopPart(Text.LineHeightOf(GameFont.Small));
        using (new TextBlock(TextAnchor.MiddleLeft))
        {
            // Delete and invert used to sit flush against each other, and invert's hitbox was expanded
            // by 4px on top of that, so they were easy to confuse: hitting invert instead of delete
            // silently shows the OPPOSITE of what the filter means, which reads like the filter broke.
            // Now they have a real gap, both have tooltips, and an inverted filter says so in its label.
            var deleteRect = topRowRect.TakeRightPart(topRowRect.height);
            TooltipHandler.TipRegion(deleteRect, "PawnEditor.RemoveFilter".Translate());
            if (Widgets.ButtonImage(deleteRect, TexButton.Delete))
            {
                Inverted = false;
                return true;
            }

            topRowRect.xMax -= 8f;
            var invertRect = topRowRect.TakeRightPart(topRowRect.height);
            TooltipHandler.TipRegion(invertRect, "PawnEditor.InvertFilter".Translate());

            var filter = Inverted ? TexPawnEditor.InvertFilterActive : TexPawnEditor.InvertFilter;
            if (Widgets.ButtonImage(invertRect, filter)) Inverted = !Inverted;

            topRowRect.xMax -= 4f;
            // Typed as string on purpose: a string/TaggedString ternary makes the Widgets.Label call
            // ambiguous between its string and TaggedString overloads.
            string displayLabel = Label;
            if (Inverted) displayLabel = Label + " " + "PawnEditor.FilterInverted".Translate().ToString();
            Widgets.Label(topRowRect, displayLabel);
            if (Mouse.IsOver(topRowRect) && Description != "")
                TooltipHandler.TipRegion(topRowRect, $"{Label.Colorize(ColoredText.TipSectionTitleColor)}\n\n{Description}");
        }

        return false;
    }

    protected abstract void DrawWidget(Rect rect);

    public bool Matches(T item) => Inverted ? !MatchesInt(item) : MatchesInt(item);

    protected abstract bool MatchesInt(T item);
}
