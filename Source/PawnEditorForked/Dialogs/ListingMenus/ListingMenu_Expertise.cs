using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PawnEditor;

/// <summary>
/// Two-column editor for Vanilla Skills Expanded expertise, laid out like the Life Lessons one
/// (<see cref="ListingMenu_Proficiencies"/>). Left: every expertise the pawn doesn't have, the ones VSE
/// allows first, the rest greyed out with VSE's reason. Right: the pawn's expertise, each with its level
/// (-/+) and a remove button. Works for any number of them, in case VSE's settings allow many.
/// One rule for every limit: at the limit, remove one to pick another (no automatic switching).
/// What may be picked is VSE's own rule (ExpertiseDef.CanApplyOn, see VSECompat).
/// Request: Yamaraja777 (Steam, 18 Oct), confirmed by xWOLFxz (22 Oct).
/// </summary>
public class ListingMenu_Expertise : Window
{
    private const float HeaderHeight = 35f;
    private const float RowHeight = 28f;
    private const float SectionHeight = 24f;
    private const float ColumnGap = 12f;
    private const float StepButtonSize = 24f;
    private const float ReasonWidth = 150f;
    private const float SearchHeight = 28f;
    private const float SortButtonWidth = 110f;

    private readonly Pawn pawn;
    private Vector2 leftScroll;
    private Vector2 rightScroll;

    // Rebuilt only after a change, a new search or a new sort; never per frame (same rule as Life Lessons).
    private List<Entry> entries;
    private string searchText = "";
    private string builtForSearch;
    private bool alphabetical;
    private bool builtAlphabetical;
    private List<(Def def, int level)> owned;
    private int allowedCount;

    // Clicks change the lists being drawn, so they run after the frame is drawn.
    private Action pendingAction;

    /// <summary>One expertise the pawn doesn't have, with VSE's verdict.</summary>
    internal sealed class Entry
    {
        public Def Def;
        public string Label;
        public bool Allowed;
        public string Reason;
    }

    public ListingMenu_Expertise(Pawn pawn)
    {
        this.pawn = pawn;
        forcePause = true;
        doCloseX = true;
        doCloseButton = true;
        closeOnClickedOutside = true;
        absorbInputAroundWindow = true;
    }

    public override Vector2 InitialSize => new(780f, 600f);

    /// <summary>Every expertise the pawn doesn't have: allowed first, then refused, each in skill order.</summary>
    internal static List<Entry> BuildEntries(Pawn pawn, bool alphabetical = false, string search = null)
    {
        var current = VSECompat.CurrentExpertise(pawn).Select(entry => entry.def).ToList();
        var result = new List<Entry>();
        foreach (var def in VSECompat.ExpertiseDefs)
        {
            if (current.Contains(def)) continue;
            var entry = new Entry { Def = def, Label = $"{def.LabelCap} ({VSECompat.SkillOf(def)?.LabelCap})" };
            // The label includes the skill, so searching "melee" lists every melee expertise.
            if (!search.NullOrEmpty() && entry.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            entry.Allowed = VSECompat.CanApplyExpertise(def, pawn, out entry.Reason);
            result.Add(entry);
        }
        if (alphabetical) result = result.OrderBy(entry => entry.Label).ToList(); // otherwise skill order, from ExpertiseDefs
        return result.Where(entry => entry.Allowed).Concat(result.Where(entry => !entry.Allowed)).ToList();
    }

    public override void DoWindowContents(Rect inRect)
    {
        if (entries == null || owned == null || builtForSearch != searchText || builtAlphabetical != alphabetical)
        {
            entries = BuildEntries(pawn, alphabetical, searchText);
            owned = VSECompat.CurrentExpertise(pawn);
            allowedCount = entries.Count(entry => entry.Allowed);
            builtForSearch = searchText;
            builtAlphabetical = alphabetical;
        }

        var header = inRect.TakeTopPart(HeaderHeight);
        using (new TextBlock(GameFont.Medium)) Widgets.Label(header, "PawnEditor.EditExpertise".Translate());
        using (new TextBlock(TextAnchor.MiddleRight)) Widgets.Label(header, pawn.LabelShortCap);

        DrawSearchRow(inRect.TakeTopPart(SearchHeight));
        inRect.yMin += 6f;
        inRect.yMax -= CloseButSize.y + 10f;
        var columnWidth = (inRect.width - ColumnGap) / 2f;
        DrawAvailable(new Rect(inRect.x, inRect.y, columnWidth, inRect.height));
        DrawOwned(new Rect(inRect.x + columnWidth + ColumnGap, inRect.y, columnWidth, inRect.height));

        if (pendingAction == null) return;
        var action = pendingAction;
        pendingAction = null;
        action();
        entries = null; // rebuild next frame
        PawnEditor.Notify_PointsUsed();
    }

    /// <summary>Search box (filters the left column) and the sort toggle: by skill or A-Z.</summary>
    private void DrawSearchRow(Rect row)
    {
        if (Widgets.ButtonText(row.TakeRightPart(SortButtonWidth), (alphabetical ? "PawnEditor.SortAlphabetical" : "PawnEditor.SortBySkill").Translate()))
            alphabetical = !alphabetical;
        row.xMax -= 6f;

        searchText = Widgets.TextField(row, searchText);
        if (!searchText.NullOrEmpty()) return;
        GUI.color = ColoredText.SubtleGrayColor;
        using (new TextBlock(TextAnchor.MiddleLeft)) Widgets.Label(row.ContractedBy(4f, 0f), "PawnEditor.Search".Translate() + "...");
        GUI.color = Color.white;
    }

    private void DrawAvailable(Rect rect)
    {
        var inner = ColumnFrame(rect, "PawnEditor.ExpertiseToPick".Translate());
        var view = new Rect(0f, 0f, inner.width - 16f, entries.Count * RowHeight + 2 * SectionHeight);
        Widgets.BeginScrollView(inner, ref leftScroll, view);

        var y = 0f;
        for (var i = 0; i < entries.Count; i++)
        {
            if (i == 0 || i == allowedCount)
            {
                DrawSection(new Rect(0f, y, view.width, SectionHeight),
                    (i < allowedCount ? "PawnEditor.ExpertiseAvailable" : "PawnEditor.ExpertiseUnavailable").Translate());
                y += SectionHeight;
            }
            DrawEntry(new Rect(0f, y, view.width, RowHeight), entries[i]);
            y += RowHeight;
        }
        Widgets.EndScrollView();
    }

    private void DrawEntry(Rect rect, Entry entry)
    {
        if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
        TooltipHandler.TipRegion(rect, () => Tooltip(entry), entry.Def.GetHashCode());

        if (!entry.Allowed)
        {
            GUI.color = Color.gray;
            if (!entry.Reason.NullOrEmpty())
                using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleRight))
                    Widgets.Label(rect.TakeRightPart(ReasonWidth), entry.Reason);
        }
        using (new TextBlock(TextAnchor.MiddleLeft)) Widgets.Label(rect.ContractedBy(4f, 0f), entry.Label);
        GUI.color = Color.white;

        if (!entry.Allowed || !Widgets.ButtonInvisible(rect)) return;
        pendingAction = () =>
        {
            VSECompat.AddExpertise(pawn, entry.Def);
            owned = null;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        };
    }

    // Like the trait picker, show what it changes, not only the description. Effects grow with the level,
    // so the left column shows level 1 ("per level") and the right one the pawn's current level.
    private static string Tooltip(Entry entry) => DescribeExpertise(entry.Def, 1, "PawnEditor.ExpertisePerLevel".Translate());

    private static string DescribeExpertise(Def def, int level, string effectsTitle)
    {
        var text = def.LabelCap.AsTipTitle() + "\n\n" + def.description;
        var effects = VSECompat.ExpertiseEffects(def, level);
        if (!effects.NullOrEmpty()) text += "\n\n" + effectsTitle.Colorize(ColoredText.TipSectionTitleColor) + "\n" + effects;
        return text;
    }

    private void DrawOwned(Rect rect)
    {
        var inner = ColumnFrame(rect, "PawnEditor.ExpertiseOwned".Translate());
        if (owned.Count == 0)
        {
            using (new TextBlock(TextAnchor.MiddleCenter)) Widgets.Label(inner, "PawnEditor.ExpertiseNone".Translate());
            return;
        }

        var view = new Rect(0f, 0f, inner.width - 16f, owned.Count * RowHeight);
        Widgets.BeginScrollView(inner, ref rightScroll, view);
        for (var i = 0; i < owned.Count; i++)
            DrawOwnedRow(new Rect(0f, i * RowHeight, view.width, RowHeight), owned[i].def, owned[i].level);
        Widgets.EndScrollView();
    }

    /// <summary>Name, level bar with -/+ (0 to 20, like VSE) and remove.</summary>
    private void DrawOwnedRow(Rect rect, Def def, int level)
    {
        if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
        TooltipHandler.TipRegion(rect, () => DescribeExpertise(def, level, "PawnEditor.ExpertiseAtLevel".Translate(level)), def.GetHashCode() ^ level);

        if (Widgets.ButtonImage(rect.TakeRightPart(StepButtonSize).ContractedBy(4f), TexButton.Delete))
            pendingAction = () =>
            {
                VSECompat.RemoveExpertise(pawn, def);
                owned = null;
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            };

        using (new TextBlock(TextAnchor.MiddleLeft)) Widgets.Label(rect.TakeLeftPart(rect.width * 0.45f).ContractedBy(4f, 0f), def.LabelCap);

        var bar = rect.ContractedBy(2f);
        Widgets.FillableBar(bar, Mathf.Max(0.01f, level / (float)VSECompat.MaxExpertiseLevel), SkillUI.SkillBarFillTex, TexPawnEditor.SkillBarBGTex, false);
        using (new TextBlock(TextAnchor.MiddleLeft)) Widgets.Label(bar.ContractedBy(3f, 0f), level.ToString());

        if (Widgets.ButtonImage(bar.TakeRightPart(StepButtonSize).ContractedBy(4f), TexButton.Plus)) SetLevel(def, level + 1);
        if (Widgets.ButtonImage(bar.TakeRightPart(StepButtonSize).ContractedBy(4f), TexButton.Minus)) SetLevel(def, level - 1);
    }

    private void SetLevel(Def def, int level) => pendingAction = () =>
    {
        VSECompat.SetExpertiseLevel(pawn, def, level);
        owned = null;
    };

    /// <summary>Column title and framed area; returns the area inside the frame.</summary>
    private static Rect ColumnFrame(Rect rect, string title)
    {
        using (new TextBlock(TextAnchor.MiddleCenter)) Widgets.Label(rect.TakeTopPart(RowHeight), title);
        Widgets.DrawMenuSection(rect);
        return rect.ContractedBy(4f);
    }

    private static void DrawSection(Rect rect, string title)
    {
        GUI.color = ColoredText.SubtleGrayColor;
        using (new TextBlock(GameFont.Tiny, TextAnchor.LowerLeft)) Widgets.Label(rect.ContractedBy(2f, 0f), title.ToUpperInvariant());
        GUI.color = new Color(1f, 1f, 1f, 0.2f);
        Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
        GUI.color = Color.white;
    }
}
