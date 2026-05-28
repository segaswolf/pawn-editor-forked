using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PawnEditor;

/// <summary>
/// Two-column editor for Life Lessons proficiencies, styled after the in-game trade UI.
/// Left column lists proficiencies the pawn can still learn; right column lists the ones
/// already known. Clicking a row moves it across: learning grants the proficiency (and its
/// prerequisites), while removing it takes it away (and any descendants that depend on it).
/// The window stays open after each change so several proficiencies can be edited in one pass.
/// </summary>
public class ListingMenu_Proficiencies : Window
{
    private const float HeaderHeight = 35f;
    private const float SearchHeight = 28f;
    private const float RowHeight = 28f;
    private const float ColumnGap = 12f;
    private const float IconSize = 22f;

    private readonly Pawn pawn;

    private string searchText = "";
    private Vector2 learnableScroll;
    private Vector2 knownScroll;

    public ListingMenu_Proficiencies(Pawn pawn)
    {
        this.pawn = pawn;
        forcePause = true;
        doCloseX = true;
        doCloseButton = true;
        closeOnClickedOutside = true;
        absorbInputAroundWindow = true;
    }

    public override Vector2 InitialSize => new(720f, 600f);

    public override void DoWindowContents(Rect inRect)
    {
        var headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
        DrawHeader(headerRect);

        var searchRect = new Rect(inRect.x, headerRect.yMax, inRect.width, SearchHeight);
        searchText = Widgets.TextField(searchRect, searchText);
        if (searchText.NullOrEmpty())
        {
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(searchRect.ContractedBy(4f, 0f), "PawnEditor.Search".Translate() + "...");
            GUI.color = Color.white;
        }

        // Leave room at the bottom for the inherited Close button.
        var bodyRect = new Rect(
            inRect.x,
            searchRect.yMax + 8f,
            inRect.width,
            inRect.height - searchRect.yMax - 8f - CloseButSize.y - 10f);

        var columnWidth = (bodyRect.width - ColumnGap) / 2f;
        var learnableRect = new Rect(bodyRect.x, bodyRect.y, columnWidth, bodyRect.height);
        var knownRect = new Rect(learnableRect.xMax + ColumnGap, bodyRect.y, columnWidth, bodyRect.height);

        DrawColumn(learnableRect, "PawnEditor.ProficienciesLearnable".Translate(), GetLearnable(), isKnownColumn: false);
        DrawColumn(knownRect, "PawnEditor.ProficienciesKnown".Translate(), GetKnown(), isKnownColumn: true);
    }

    /// <summary>
    /// Draws the dialog title with the pawn's name on the right.
    /// </summary>
    private void DrawHeader(Rect rect)
    {
        Text.Font = GameFont.Medium;
        Widgets.Label(rect, "PawnEditor.EditProficiencies".Translate());
        Text.Font = GameFont.Small;

        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleRight;
        Widgets.Label(rect, pawn.LabelShortCap);
        Text.Anchor = anchor;
    }

    /// <summary>
    /// Draws a titled, scrollable column of proficiency rows inside a bordered section.
    /// </summary>
    private void DrawColumn(Rect rect, string title, List<Def> defs, bool isKnownColumn)
    {
        var titleRect = new Rect(rect.x, rect.y, rect.width, RowHeight);
        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(titleRect, $"{title} ({defs.Count})");
        Text.Anchor = anchor;

        var listRect = new Rect(rect.x, titleRect.yMax, rect.width, rect.height - RowHeight);
        Widgets.DrawMenuSection(listRect);
        var innerRect = listRect.ContractedBy(4f);

        var viewRect = new Rect(0f, 0f, innerRect.width - 16f, defs.Count * RowHeight);
        ref var scroll = ref isKnownColumn ? ref knownScroll : ref learnableScroll;
        Widgets.BeginScrollView(innerRect, ref scroll, viewRect);

        var rowY = 0f;
        foreach (var def in defs)
        {
            var rowRect = new Rect(0f, rowY, viewRect.width, RowHeight);
            DrawRow(rowRect, def, isKnownColumn);
            rowY += RowHeight;
        }

        Widgets.EndScrollView();
    }

    /// <summary>
    /// Draws a single proficiency row: icon hint, label, hover highlight, tooltip, and click action.
    /// </summary>
    private void DrawRow(Rect rect, Def def, bool isKnownColumn)
    {
        if (Mouse.IsOver(rect))
            Widgets.DrawHighlight(rect);

        var iconRect = new Rect(rect.x + 2f, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize);
        if (isKnownColumn)
        {
            GUI.color = Color.green;
            Widgets.DrawTextureFitted(iconRect, Widgets.CheckboxOnTex, 0.7f);
            GUI.color = Color.white;
        }

        var labelRect = new Rect(iconRect.xMax + 4f, rect.y, rect.width - IconSize - 6f, rect.height);
        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(labelRect, def.LabelCap);
        Text.Anchor = anchor;

        TooltipHandler.TipRegion(rect, () => GetTooltip(def, isKnownColumn), def.GetHashCode());

        if (Widgets.ButtonInvisible(rect))
        {
            if (isKnownColumn)
                RemoveProficiency(def);
            else
                LearnProficiency(def);
        }
    }

    /// <summary>
    /// Grants a proficiency (and its prerequisites) to the pawn, then refreshes derived state.
    /// </summary>
    private void LearnProficiency(Def def)
    {
        LifeLessonsCompat.TryGainProficiency(pawn, def, force: true);
        LifeLessonsCompat.RefreshModifiers(pawn);
        SoundDefOf.Tick_High.PlayOneShotOnCamera();
    }

    /// <summary>
    /// Removes a proficiency (and its descendants) from the pawn, then refreshes derived state.
    /// Descendants are removed because they would otherwise have unmet prerequisites.
    /// </summary>
    private void RemoveProficiency(Def def)
    {
        // Remove any known proficiency that lists this one as a prerequisite first,
        // so we never leave a known proficiency with a missing prerequisite.
        foreach (var dependent in GetKnownDependents(def))
            LifeLessonsCompat.RemoveProficiency(pawn, dependent, removeAncestors: false);

        LifeLessonsCompat.RemoveProficiency(pawn, def, removeAncestors: false);
        LifeLessonsCompat.RefreshModifiers(pawn);
        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
    }

    /// <summary>
    /// Finds known proficiencies that depend (directly or transitively) on the given def,
    /// so they can be removed alongside it to keep the proficiency tree consistent.
    /// </summary>
    private List<Def> GetKnownDependents(Def def)
    {
        var known = GetKnown();
        var result = new List<Def>();
        foreach (var candidate in known)
        {
            if (candidate == def) continue;
            var prereqs = GetPrerequisites(candidate);
            if (prereqs.Contains(def.defName))
                result.Add(candidate);
        }
        return result;
    }

    /// <summary>
    /// Reads the prerequisite defNames of a proficiency def via its 'prerequisites' field.
    /// Returns an empty set when the field is absent or empty.
    /// </summary>
    private static HashSet<string> GetPrerequisites(Def def)
    {
        var result = new HashSet<string>();
        var field = def.GetType().GetField("prerequisites");
        if (field?.GetValue(def) is System.Collections.IEnumerable prereqs)
            foreach (var p in prereqs)
                if (p is Def pd)
                    result.Add(pd.defName);
        return result;
    }

    /// <summary>
    /// Proficiencies the pawn already knows, filtered by the search box.
    /// </summary>
    private List<Def> GetKnown()
    {
        return LifeLessonsCompat.GetCompletedProficiencies(pawn)
            .Where(MatchesSearch)
            .OrderBy(d => d.LabelCap.ToString())
            .ToList();
    }

    /// <summary>
    /// Proficiencies the pawn does not yet know, filtered by the search box.
    /// </summary>
    private List<Def> GetLearnable()
    {
        var knownNames = new HashSet<string>(
            LifeLessonsCompat.GetCompletedProficiencies(pawn).Select(d => d.defName));

        return LifeLessonsCompat.GetAllProficiencyDefs()
            .Where(d => d != null && !knownNames.Contains(d.defName))
            .Where(MatchesSearch)
            .OrderBy(d => d.LabelCap.ToString())
            .ToList();
    }

    private bool MatchesSearch(Def def)
    {
        if (searchText.NullOrEmpty()) return true;
        return def.LabelCap.ToString().IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Builds the hover tooltip for a proficiency row, including category and learn/remove hint.
    /// </summary>
    private string GetTooltip(Def def, bool isKnownColumn)
    {
        var sb = new StringBuilder();
        sb.AppendLine(def.LabelCap.AsTipTitle());

        if (!def.description.NullOrEmpty())
        {
            sb.AppendLine();
            sb.AppendLine(def.description);
        }

        var category = LifeLessonsCompat.GetCategory(def);
        if (category != "Unknown")
        {
            sb.AppendLine();
            sb.AppendLine(("Category: " + category).Colorize(ColoredText.SubtleGrayColor));
        }

        sb.AppendLine();
        sb.AppendLine(isKnownColumn
            ? "PawnEditor.ProficiencyClickRemove".Translate().Colorize(ColoredText.SubtleGrayColor)
            : "PawnEditor.ProficiencyClickLearn".Translate().Colorize(ColoredText.SubtleGrayColor));

        return sb.ToString();
    }
}
