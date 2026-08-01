using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

public class ListingMenu<T> : Window, IMinWindowSize
{
    // Enforced by the resizer itself (see IMinWindowSize): below this the header, the list and the
    // filter panel start overlapping. Growing was always fine; it was shrinking that broke.
    public Vector2 MinWindowSize => new(InitialSize.x, 400f);

    protected readonly Pawn Pawn;
    private readonly Func<T, AddResult> _action;
    private readonly bool _allowMultiSelect;
    private readonly Action _closeAction;
    private readonly string _closeLabel;
    private readonly string _menuTitle;
    private readonly int _minCount;
    private readonly Func<List<T>, AddResult> _multiAction;

    protected Listing_Thing<T> Listing;
    protected static Dictionary<string, List<Filter<T>>> cachedActiveFilters = new();
    protected TreeNode_ThingCategory TreeNodeThingCategory;

    private Vector2 _scrollPosition;
    private bool _showFilters;
    private float _viewHeight = 100f;

    public ListingMenu(List<T> items, Func<T, string> labelGetter, Func<T, AddResult> action, string menuTitle,
        Func<T, string> descGetter = null, Action<T, Rect> iconDrawer = null, List<Filter<T>> filters = null, Pawn pawn = null, string nextLabel = null,
        string closeLabel = null, Action closeAction = null,
        IEnumerable<T> auxHighlight = null) :
        this(menuTitle, pawn, nextLabel, closeLabel, closeAction)
    {
        Listing = new(items.OrderBy(labelGetter).ToList(), labelGetter, iconDrawer, descGetter, filters, auxHighlight);
        _action = action;
        _allowMultiSelect = false;
    }

    public ListingMenu(List<T> items, Func<T, string> labelGetter, Func<List<T>, AddResult> action, string menuTitle, IntRange wantedCount,
        Func<T, string> descGetter = null, Action<T, Rect> iconDrawer = null, List<Filter<T>> filters = null, Pawn pawn = null, string nextLabel = null,
        string closeLabel = null, Action closeAction = null,
        IEnumerable<T> auxHighlight = null) :
        this(menuTitle, pawn, nextLabel, closeLabel, closeAction)
    {
        Listing = new(items.OrderBy(labelGetter).ToList(), wantedCount.TrueMax, labelGetter, iconDrawer, descGetter, filters, auxHighlight);
        _multiAction = action;
        _allowMultiSelect = true;
        _minCount = wantedCount.TrueMin;
    }

    protected ListingMenu(string menuTitle, Pawn pawn = null, string nextLabel = null, string closeLabel = null, Action closeAction = null) : this(menuTitle,
        pawn)
    {
        NextLabel = nextLabel.NullOrEmpty() ? "Add".Translate().CapitalizeFirst() : nextLabel;
        _closeLabel = closeLabel.NullOrEmpty() ? "Close".Translate() : closeLabel;
        _closeAction = closeAction;
    }

    protected ListingMenu(Func<T, AddResult> action, string menuTitle, Pawn pawn = null) : this(menuTitle, pawn)
    {
        _action = action;
        _allowMultiSelect = false;
        NextLabel = "Add".Translate().CapitalizeFirst();
        _closeLabel = "Close".Translate();
    }

    protected ListingMenu(string menuTitle, Pawn pawn = null)
    {
        Pawn = pawn;
        _menuTitle = menuTitle;

        draggable = true;
        closeOnClickedOutside = true;
        onlyOneOfTypeAllowed = true;
    }

    public override void PreOpen()
    {
        base.PreOpen();
        // Only restore a cache that actually EXISTS. The old unconditional fallback to an empty list
        // wiped the filters marked EnabledByDefault (Listing_Thing had just turned them on), so a
        // default filter could never take effect on the first open. Once the user edits the filters,
        // PostClose caches their choice and it is honoured from then on — including "I deleted it".
        if (cachedActiveFilters.TryGetValue(_menuTitle, out var cached))
            Listing.ActiveFilters = cached;
    }

    public override void PostClose()
    {
        base.PostClose();
        cachedActiveFilters[_menuTitle] = Listing.ActiveFilters;
    }

    protected virtual string NextLabel { get; }

    public override Vector2 InitialSize => new(400f, 600f);
    private static Vector2 WideSize => new(800f, 600f);


    public override void DoWindowContents(Rect inRect)
    {
        // These menus are user-resizable now, so keep a floor: below this the list and the filter panel
        // collapse into an unusable mess.
        if (windowRect.width < InitialSize.x || windowRect.height < 400f)
        {
            windowRect.width = Mathf.Max(windowRect.width, InitialSize.x);
            windowRect.height = Mathf.Max(windowRect.height, 400f);
        }

        DrawHeader(inRect.TakeTopPart(Text.LineHeightOf(GameFont.Medium)));
        inRect.yMin += 16f;

        // Size the list from the CURRENT width, not InitialSize. Hardcoding the original width left the
        // list stuck at its starting size after a resize (dead space on the right, or clipped content).
        // The filter panel keeps its fixed width; everything else the user gains goes to the list.
        var filterWidth = Listing.Filters != null && _showFilters ? WideSize.x - InitialSize.x : 0f;
        var leftRect = inRect.TakeLeftPart(Mathf.Max(200f, inRect.width - filterWidth));
        var bottomButRect = leftRect.TakeBottomPart(UIUtility.BottomButtonSize.y);
        DrawBottomButtons(bottomButRect);
        DrawFooter(ref leftRect);
        DrawFootnote(leftRect.TakeBottomPart(Text.LineHeightOf(GameFont.Small) + 8f));


        DrawListing(leftRect);

        if (Listing.Filters != null && _showFilters)
        {
            inRect.xMin += 16f;
            DrawFilters(inRect);
        }

        UpdateWindowRect();
        CloseIfNotSelected();
    }

    protected virtual void DrawFooter(ref Rect inRect)
    {
    }

    private void DrawHeader(Rect inRect)
    {
        using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft, false)) Widgets.Label(inRect.TakeLeftPart(Text.CalcSize(_menuTitle).x), _menuTitle);

        if (Pawn == null) return;
        using (new TextBlock(GameFont.Medium))
        {
            var lineHeight = Text.LineHeight;
            var name = Pawn.Name.ToStringShort.Colorize(Color.white);
            float scaleFactor = 1;

            if (!Pawn.NonHumanlikeOrWildMan())
            {
                var job = (", " + Pawn.story.TitleCap).Colorize(ColoredText.SubtleGrayColor);
                scaleFactor = 8f;

                if (inRect.width >= Text.CalcSize(name + job).x + lineHeight)
                    name += job;
            }

            using (new TextBlock(TextAnchor.MiddleRight))
                Widgets.Label(inRect.TakeRightPart(Text.CalcSize(name).x + 8f), name);

            var portraitRect = inRect.TakeRightPart(inRect.height);
            portraitRect.height = inRect.height;

            portraitRect = portraitRect.ExpandedBy(1 * scaleFactor);
            Widgets.ThingIcon(portraitRect, Pawn);
        }
    }

    private void DrawListing(Rect inRect)
    {
        Widgets.DrawMenuSection(inRect);
        if (TreeNodeThingCategory != null)
            using (new TextBlock(GameFont.Tiny))
                DrawNodeCollapse(inRect.TakeTopPart(26f).ContractedBy(1f));

        inRect = inRect.ContractedBy(4f);
        inRect.yMin -= 4f;
        var viewRect = new Rect(0.0f, 0.0f, inRect.width - 16f, _viewHeight);
        var visibleRect = new Rect(0.0f, 0.0f, inRect.width, inRect.height);
        visibleRect.position += _scrollPosition;
        var outRect = inRect;
        outRect.yMax -= UIUtility.SearchBarHeight + 4f;
        Listing.DrawSearchBar(new(inRect.x, inRect.yMax - UIUtility.SearchBarHeight, inRect.width,
            UIUtility.SearchBarHeight));
        Widgets.BeginScrollView(outRect, ref _scrollPosition, viewRect);
        var rect3 = new Rect(0.0f, 2f, viewRect.width, 999999f);
        visibleRect.position -= rect3.position;
        Listing.Begin(rect3);
        if (Listing is Listing_TreeThing listingTree)
            listingTree.ListCategoryChildren(TreeNodeThingCategory, 1, visibleRect);
        else
            Listing.ListChildren(visibleRect);

        Listing.End();
        if (Event.current.type == EventType.Layout)
            _viewHeight = Listing.CurHeight;
        Widgets.EndScrollView();
    }

    private void DrawFootnote(Rect inRect)
    {
        using (new TextBlock(TextAnchor.MiddleLeft))
        {
            DrawSelected(ref inRect);
            // Show filters checkbox
            if (Listing.Filters == null) return;
            var checkboxLabelRect = inRect.TakeRightPart(Widgets.CheckboxSize + 8f);
            Widgets.Checkbox(new(checkboxLabelRect.xMax - Widgets.CheckboxSize, checkboxLabelRect.yMin + (checkboxLabelRect.height - Widgets.CheckboxSize) / 2),
                ref _showFilters);
            GUI.color = ColoredText.SubtleGrayColor;
            using (new TextBlock(TextAnchor.MiddleRight))
                Widgets.Label(inRect, "Show filters");
            GUI.color = Color.white;
        }
    }

    protected virtual void DrawSelected(ref Rect inRect)
    {
        // Current selection label
        if (Listing.IconDrawer != null)
        {
            if (_allowMultiSelect)
                foreach (var item in Listing.MultiSelected)
                {
                    Listing.IconDrawer(item, new(inRect.x, inRect.y, 32f, 32f));
                    inRect.xMin += 32f;
                }
            else
            {
                Listing.IconDrawer(Listing.Selected, new(inRect.x, inRect.y, 32f, 32f));
                inRect.xMin += 32f;
            }
        }

        var labelStr = $"{"StartingPawnsSelected".Translate()}: ";
        var labelWidth = Text.CalcSize(labelStr).x;
        var selectedStr = _allowMultiSelect ? Listing.MultiSelected.Count == 0 ? "None".Translate() : Listing.MultiSelected.Join(Listing.LabelGetter) :
            Listing.Selected != null ? (TaggedString)Listing.LabelGetter(Listing.Selected) : "None".Translate();
        Widgets.Label(inRect, labelStr.Colorize(ColoredText.SubtleGrayColor));
        inRect.xMin += labelWidth;
        Widgets.Label(inRect, selectedStr);
    }

    private void DrawBottomButtons(Rect inRect)
    {
        if (_allowMultiSelect ? Listing.MultiSelected.Count >= _minCount : Listing.Selected != null)
        {
            if (Widgets.ButtonText(new(inRect.xMax - UIUtility.BottomButtonSize.x, inRect.y, UIUtility.BottomButtonSize.x, UIUtility.BottomButtonSize.y),
                    NextLabel))
                (_allowMultiSelect ? _multiAction(Listing.MultiSelected) : _action(Listing.Selected)).HandleResult(() => Close());

            if (Widgets.ButtonText(new(inRect.x, inRect.y, UIUtility.BottomButtonSize.x, UIUtility.BottomButtonSize.y), _closeLabel))
            {
                _closeAction?.Invoke();
                Close();
            }
        }
        else
        {
            if (Widgets.ButtonText(
                    new((float)((inRect.width - (double)UIUtility.BottomButtonSize.x) / 2.0), inRect.y, UIUtility.BottomButtonSize.x,
                        UIUtility.BottomButtonSize.y), _closeLabel))
            {
                _closeAction?.Invoke();
                Close();
            }
        }
    }

    private void DrawFilters(Rect inRect)
    {
        var allFilters = Listing.Filters;
        var activeFilters = Listing.ActiveFilters;

        UIUtility.ListSeparator(inRect.TakeTopPart(Text.LineHeightOf(GameFont.Small) + 8f), $"{"PawnEditor.Filters".Translate().CapitalizeFirst()}");
        string label1 = "Add".Translate().CapitalizeFirst() + " " + "PawnEditor.Filter".Translate().ToLower() + "...";
        string label2 = "RemoveOrgan".Translate().CapitalizeFirst() + " " + "PawnEditor.All".Translate().ToLower();
        var buttonRect = inRect.TakeTopPart(UIUtility.RegularButtonHeight);

        var list = new List<FloatMenuOption>();

        foreach (var filter in allFilters)
            list.Insert(0, new(filter.Label, delegate
            {
                var maxFilterCount = allFilters.Count(f => f.Label == filter.Label);

                if (activeFilters.Count(f => f.Label == filter.Label) < maxFilterCount)
                {
                    // FirstOrDefault could return NULL here and the old code added it blindly, which
                    // poisons the list: every later Matches() call throws and the listing stops
                    // filtering. Re-adding a filter after deleting it is exactly the path that hits it.
                    var toAdd = allFilters.FirstOrDefault(f => f.Label == filter.Label && !activeFilters.Contains(f));
                    if (toAdd != null)
                    {
                        toAdd.Inverted = false; // re-added filters start in their normal (non-inverted) state
                        activeFilters.Add(toAdd);
                    }
                }
                else
                    Messages.Message(new("Reached limit of this specific filter count", MessageTypeDefOf.RejectInput));
            }));

        var distinctList = list.GroupBy(l => l.Label).Select(m => m.First()).OrderBy(n => n.Label).ToList();

        if (Widgets.ButtonText(buttonRect.TakeLeftPart(inRect.width * 0.7f), label1)) Find.WindowStack.Add(new FloatMenu(distinctList));

        buttonRect.xMin += 4f;
        if (Widgets.ButtonText(buttonRect, label2))
        {
            activeFilters.ForEach(f => f.Inverted = false);
            activeFilters.Clear();
        }

        inRect.yMin += 4f;

        var filtersToRemove = new List<Filter<T>>();
        foreach (var activeFilter in activeFilters)
        {
            if (activeFilter.DrawFilter(ref inRect))
                filtersToRemove.Add(activeFilter);
            inRect.yMin += 4f;
        }


        filtersToRemove.ForEach(ftr => activeFilters.Remove(ftr));
    }

    private void DrawNodeCollapse(Rect inRect)
    {
        if (Widgets.ButtonText(inRect.RightHalf(), "OpenFolder".Translate() + " " + "AllDays".Translate()))
        {
            foreach (var treeNodeThingCategory in TreeNodeThingCategory.ChildCategoryNodes)
            {
                treeNodeThingCategory.SetOpen(1, true);
                foreach (var child in treeNodeThingCategory.ChildCategoryNodes) child.SetOpen(1, true);
            }
            if (Listing is Listing_TreeThing listingTree)
                listingTree.SetManualGroupsOpen(1, true);
        }

        if (Widgets.ButtonText(inRect.LeftHalf(), "Close".Translate() + " " + "AllDays".Translate()))
        {
            foreach (var treeNodeThingCategory in TreeNodeThingCategory.ChildCategoryNodes)
            {
                treeNodeThingCategory.SetOpen(1, false);
                foreach (var child in treeNodeThingCategory.ChildCategoryNodes) child.SetOpen(1, false);
            }
            if (Listing is Listing_TreeThing listingTree)
                listingTree.SetManualGroupsOpen(1, false);
        }
    }

    private bool _appliedShowFilters;

    private void UpdateWindowRect()
    {
        // Only widen/narrow when the FILTERS PANEL is actually toggled. The old version slammed the
        // window back to a fixed size on every frame whenever the width didn't match, which fought the
        // resizer and made these menus impossible to resize (they'd snap back instantly).
        if (_showFilters == _appliedShowFilters) return;

        var filterWidth = WideSize.x - InitialSize.x;
        windowRect.width = Mathf.Max(InitialSize.x, windowRect.width + (_showFilters ? filterWidth : -filterWidth));
        windowRect.height = Mathf.Max(InitialSize.y, windowRect.height);
        _appliedShowFilters = _showFilters;
    }

    private void CloseIfNotSelected()
    {
        if (Find.WindowStack.focusedWindow is Dialog_PawnEditor) Find.WindowStack.TryRemove(this);
    }
}
