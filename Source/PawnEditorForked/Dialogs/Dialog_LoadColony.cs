using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// v3.1 — Single-window colony loader. Lists the saved colonies, lets the user pick the load mode
/// (new clones vs replace-by-ID) and which categories to load, then runs <see cref="ColonyLoadUtility"/>.
///
/// It's a Window, NOT nested FloatMenus, on purpose: a FloatMenu opened from inside another FloatMenu's
/// option gets closed by RimWorld on the same frame, so the old "colony menu -> new/replace menu" chain
/// silently did nothing. A Window opened from a FloatMenu option is fine (the Save dialog does the same).
/// </summary>
[HotSwappable]
public class Dialog_LoadColony : Window
{
    private readonly List<(string folderType, string label)> colonies;
    private int selectedIndex;
    private bool replace;          // false = load as new clones, true = replace matching pawns by ID
    private bool humans = true;
    private bool animals = true;
    private bool mechs = true;
    private Vector2 scroll;

    public override Vector2 InitialSize => new Vector2(480f, 540f);

    public Dialog_LoadColony(List<(string folderType, string label)> colonies)
    {
        this.colonies = colonies;
        forcePause = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = true;
    }

    public override void DoWindowContents(Rect inRect)
    {
        using (new TextBlock(GameFont.Medium))
            Widgets.Label(inRect.TakeTopPart(34f), "PawnEditor.LoadColonyPawns".Translate());
        inRect.yMin += 4f;

        var buttonRow = inRect.TakeBottomPart(36f);
        inRect.yMax -= 6f;

        // ── Categories (bottom block) ──
        var catRect = inRect.TakeBottomPart(90f);
        var catListing = new Listing_Standard();
        catListing.Begin(catRect);
        catListing.CheckboxLabeled(PawnCategory.Humans.LabelCapPlural(),  ref humans);
        catListing.CheckboxLabeled(PawnCategory.Animals.LabelCapPlural(), ref animals);
        catListing.CheckboxLabeled(PawnCategory.Mechs.LabelCapPlural(),   ref mechs);
        catListing.End();

        // ── Load mode (new clones / replace by ID) ──
        var modeRect = inRect.TakeBottomPart(58f);
        var modeListing = new Listing_Standard();
        modeListing.Begin(modeRect);
        if (modeListing.RadioButton("PawnEditor.ColonyLoadNew".Translate(),     !replace)) replace = false;
        if (modeListing.RadioButton("PawnEditor.ColonyLoadReplace".Translate(),  replace)) replace = true;
        modeListing.End();
        inRect.yMax -= 6f;

        // ── Colony list (scrollable) ──
        Widgets.Label(inRect.TakeTopPart(24f), "PawnEditor.PickColony".Translate());
        Widgets.DrawMenuSection(inRect);
        var listArea = inRect.ContractedBy(4f);
        const float rowH = 50f;
        var innerHeight = colonies.Count * rowH;
        var viewRect = new Rect(0f, 0f, listArea.width - 20f, Mathf.Max(innerHeight, listArea.height));
        Widgets.BeginScrollView(listArea, ref scroll, viewRect);
        for (var i = 0; i < colonies.Count; i++)
        {
            var row = new Rect(0f, i * rowH, viewRect.width, rowH - 2f);

            if (selectedIndex == i) Widgets.DrawHighlightSelected(row);
            else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
            if (Widgets.ButtonInvisible(row)) selectedIndex = i;

            Widgets.RadioButtonDraw(row.x + 8f, row.y + (row.height - 24f) / 2f, selectedIndex == i, false);

            var textRect = new Rect(row.x + 42f, row.y, row.width - 48f, row.height);
            SplitLabel(colonies[i].label, out var title, out var meta);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                if (meta.NullOrEmpty())
                    Widgets.Label(textRect, title);
                else
                {
                    Widgets.Label(textRect.TopPart(0.58f), title);
                    using (new TextBlock(GameFont.Tiny))
                        Widgets.Label(textRect.BottomPart(0.42f), meta.Colorize(ColoredText.SubtleGrayColor));
                }
            }
        }
        Widgets.EndScrollView();

        // ── Buttons ──
        if (Widgets.ButtonText(buttonRow.RightHalf().ContractedBy(2f, 0f), "Load".Translate()))
        {
            if (colonies.Count == 0) { Close(); return; }
            if (!humans && !animals && !mechs)
            {
                Messages.Message("PawnEditor.PickAtLeastOneCategory".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            var chosen = colonies[selectedIndex];
            Close();
            ColonyLoadUtility.LoadColony(chosen.folderType, replace, humans, animals, mechs);
        }

        if (Widgets.ButtonText(buttonRow.LeftHalf().ContractedBy(2f, 0f), "CancelButton".Translate()))
            Close();
    }

    // Split "Faction / Settlement  (10 pawns, 7/13/2026)" into a bold title and a gray meta line.
    private static void SplitLabel(string label, out string title, out string meta)
    {
        title = label;
        meta = null;
        if (label.NullOrEmpty()) return;
        var open = label.LastIndexOf('(');
        var close = label.LastIndexOf(')');
        if (open > 0 && close > open)
        {
            title = label.Substring(0, open).TrimEnd();
            meta = label.Substring(open + 1, close - open - 1);
        }
    }
}
