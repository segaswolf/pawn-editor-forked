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
/// prerequisites), while removing it takes it away (and everything that depends on it).
///
/// Each row surfaces prerequisite information so the player can see the dependency chain
/// without leaving the editor: learnable rows show a "needs N" badge when prerequisites are
/// missing, and every tooltip lists each prerequisite with a met (✓) / unmet (✗) marker.
/// The window stays open after each change so several proficiencies can be edited in one pass.
/// </summary>
public class ListingMenu_Proficiencies : Window
{
    private const float HeaderHeight = 35f;
    private const float SearchHeight = 28f;
    private const float RowHeight = 28f;
    private const float ColumnGap = 12f;
    private const float IconSize = 22f;
    private const float CategoryHeaderHeight = 24f;

    private readonly Pawn pawn;

    // Pawns whose Life Lessons comp we've already re-synced this session. The repair
    // (snapshot -> reinit -> restore) is only needed once per pawn to fix the PioneeringComp
    // desync left by older builds; repeating it on every editor open would be wasted work (and
    // would run the delicate reinit more than necessary). Static so it persists across opens.
    private static readonly HashSet<int> repairedPawnIds = new();

    private string searchText = "";
    private Vector2 learnableScroll;
    private Vector2 knownScroll;

    // Click actions mutate the pawn's proficiency list. Running them mid-render (while we are
    // iterating the very lists we built this frame) leaves the layout in an inconsistent state.
    // We capture the action here and run it after drawing completes.
    private System.Action pendingAction;

    // ── Per-frame allocation fix (v3.0) ──
    // The profiler showed DoWindowContents allocating ~150 KB EVERY frame (~9 MB/s at 60fps),
    // which fed the GC until it stalled and blacked out the screen. The cause was rebuilding
    // everything every frame: GroupBy().ToList(), and a NEW HashSet per row (KnownDefNames was
    // called inside CountMissingPrerequisites for every visible row). None of that changes
    // between frames — it only changes when the search text changes or the pawn gains/loses a
    // proficiency. So we build it ONCE into this cache and the per-frame render only reads it.
    // Call InvalidateCache() whenever the underlying data changes.
    private string cachedForSearch;
    private bool cacheValid;
    private HashSet<string> cachedKnownNames;          // names the pawn already has (built once)
    private List<IGrouping<string, Def>> cachedLearnableGroups;
    private List<IGrouping<string, Def>> cachedKnownGroups;
    private int cachedLearnableCount;
    private int cachedKnownCount;
    private Dictionary<Def, int> cachedMissingCounts;   // per-def missing-prereq count (built once)
    private Dictionary<Def, List<Def>> cachedDependents; // per-def known dependents (built once)

    // Column header strings include the live count, so they only change when the cache rebuilds.
    // Precompute them there instead of building a new interpolated string every frame.
    private string cachedLearnableTitle;
    private string cachedKnownTitle;

    // Static-ish UI strings built once on first use (Translate allocates a TaggedString each call).
    private string searchPlaceholder;

    /// <summary>
    /// Marks the cached proficiency snapshot stale so it is rebuilt on the next frame. Call this
    /// after any change to the pawn's proficiencies (learn/remove) so the UI reflects it.
    /// </summary>
    private void InvalidateCache() => cacheValid = false;

    /// <summary>
    /// Rebuilds the cached snapshot used by the per-frame render: known-name set, grouped and
    /// sorted learnable/known lists, missing-prerequisite counts, and known-dependent lists.
    /// Runs only when the cache is stale or the search text changed — NOT every frame. This is
    /// the "build once, render reads" pattern: heavy work (reflection into Life Lessons, set and
    /// list construction) happens here, at most once per user action, instead of 60 times a
    /// second. Measured under the profiler as a PerAction event so we can confirm the win.
    /// </summary>
    private void EnsureCache()
    {
        if (cacheValid && cachedForSearch == searchText) return;

        PawnEditorProfiler.Measure("Proficiencies.RebuildCache", PawnEditorProfiler.Cadence.PerAction, () =>
        {
            var completed = GetCompleted();
            cachedKnownNames = new HashSet<string>(completed.Select(d => d.defName));

            // Known list: completed proficiencies, filtered + sorted, grouped by category.
            var known = completed
                .Where(MatchesSearch)
                .OrderBy(d => LifeLessonsCompat.GetCategory(d))
                .ThenBy(d => d.LabelCap.ToString())
                .ToList();
            cachedKnownCount = known.Count;
            cachedKnownGroups = known.GroupBy(d => LifeLessonsCompat.GetCategory(d)).ToList();

            // Learnable list: everything not known, filtered + sorted, grouped by category.
            var learnable = LifeLessonsCompat.GetAllProficiencyDefs()
                .Where(d => d != null && !cachedKnownNames.Contains(d.defName))
                .Where(MatchesSearch)
                .OrderBy(d => LifeLessonsCompat.GetCategory(d))
                .ThenBy(d => d.LabelCap.ToString())
                .ToList();
            cachedLearnableCount = learnable.Count;
            cachedLearnableGroups = learnable.GroupBy(d => LifeLessonsCompat.GetCategory(d)).ToList();

            // Missing-prerequisite count per learnable def (uses the cached known-name set).
            cachedMissingCounts = new Dictionary<Def, int>();
            foreach (var def in learnable)
            {
                var prereqs = LifeLessonsCompat.GetPrerequisites(def);
                cachedMissingCounts[def] = prereqs.Count == 0
                    ? 0
                    : prereqs.Count(p => !cachedKnownNames.Contains(p.defName));
            }

            // Known dependents per known def (for the remove-cascade warning/tooltip).
            cachedDependents = new Dictionary<Def, List<Def>>();
            foreach (var def in known)
                cachedDependents[def] = BuildKnownDependents(def, completed);

            // Column titles embed the count, so rebuild them here (not every frame).
            cachedLearnableTitle = $"{"PawnEditor.ProficienciesLearnable".Translate()} ({cachedLearnableCount})";
            cachedKnownTitle = $"{"PawnEditor.ProficienciesKnown".Translate()} ({cachedKnownCount})";

            cachedForSearch = searchText;
            cacheValid = true;
        });
    }

    public ListingMenu_Proficiencies(Pawn pawn)
    {
        this.pawn = pawn;
        forcePause = true;
        doCloseX = true;
        doCloseButton = true;
        closeOnClickedOutside = true;
        absorbInputAroundWindow = true;

        // Option A safeguard: when the user opens THIS pawn's proficiency editor, clean up any
        // invalid proficiencies it may have from an old save / removed mod / hand-edit (orphans
        // without their prerequisite, or null defs). We only touch the pawn the user is editing,
        // never all pawns. Anything removed is logged; the user can re-learn it and re-save to
        // repair the save. Done in the ctor so the first cache build already reflects the cleaned
        // state and the list shows correctly from the first frame.
        if (LifeLessonsCompat.Active)
        {
            // First, surgically repair the PioneeringComp NRE that older builds baked into some
            // saves: PioneeringComp.Initialize() just re-creates its null activity lists, curing
            // the per-tick UnexhaustActivities crash without touching proficiencies. Once per pawn
            // per session, only if the pawn has proficiencies (no comp activity to fix otherwise).
            if (repairedPawnIds.Add(pawn.thingIDNumber) && GetCompleted().Count > 0)
                LifeLessonsCompat.RepairPioneeringComp(pawn);

            // Then clean up any orphaned/invalid proficiencies (see SanitizeProficiencies).
            LifeLessonsCompat.SanitizeProficiencies(pawn);
        }
    }

    public override Vector2 InitialSize => new(720f, 600f);

    public override void DoWindowContents(Rect inRect)
    {
        // Resizable now: keep a floor so the two columns don't collapse into an unusable mess.
        windowRect.width = Mathf.Max(windowRect.width, InitialSize.x);
        windowRect.height = Mathf.Max(windowRect.height, 480f);

        // [BANDERITA] The whole per-frame render of this window. If this shows up as a per-frame
        // allocator in the profiler summary, the list rebuilds below are the cause.
        PawnEditorProfiler.Measure("Proficiencies.DoWindowContents", PawnEditorProfiler.Cadence.PerFrame, () =>
        {
            DoWindowContentsInner(inRect);
        });
    }

    private void DoWindowContentsInner(Rect inRect)
    {
        // Build the snapshot once (here, only when stale/search changed) so the per-frame draw
        // below allocates nothing. This is what removed the ~9 MB/s churn that caused the GC
        // stall / black screen.
        EnsureCache();

        var headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
        DrawHeader(headerRect);

        var searchRect = new Rect(inRect.x, headerRect.yMax, inRect.width, SearchHeight);
        var newSearch = Widgets.TextField(searchRect, searchText);
        if (newSearch != searchText)
        {
            searchText = newSearch;
            InvalidateCache(); // search changed → rebuild snapshot next frame
        }
        if (searchText.NullOrEmpty())
        {
            searchPlaceholder ??= "PawnEditor.Search".Translate() + "..."; // build once, reuse
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(searchRect.ContractedBy(4f, 0f), searchPlaceholder);
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

        DrawColumn(learnableRect, cachedLearnableTitle, cachedLearnableGroups, cachedLearnableCount, isKnownColumn: false);
        DrawColumn(knownRect, cachedKnownTitle, cachedKnownGroups, cachedKnownCount, isKnownColumn: true);
        // Run any click action now that all drawing for this frame is done.
        if (pendingAction != null)
        {
            var action = pendingAction;
            pendingAction = null;
            action();
        }
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
    /// Draws a titled, scrollable column of proficiency rows, grouped by category. Reads the
    /// pre-built groups and title from the cache (see EnsureCache); does no list/group/string
    /// construction itself, so it allocates nothing per frame beyond the Rects it draws.
    /// </summary>
    private void DrawColumn(Rect rect, string title, List<IGrouping<string, Def>> groups, int totalCount, bool isKnownColumn)
    {
        var titleRect = new Rect(rect.x, rect.y, rect.width, RowHeight);
        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(titleRect, title); // precomputed title with count, no per-frame string build
        Text.Anchor = anchor;

        var listRect = new Rect(rect.x, titleRect.yMax, rect.width, rect.height - RowHeight);
        Widgets.DrawMenuSection(listRect);
        var innerRect = listRect.ContractedBy(4f);

        var groupCount = groups?.Count ?? 0;
        var contentHeight = totalCount * RowHeight + groupCount * CategoryHeaderHeight;
        var viewRect = new Rect(0f, 0f, innerRect.width - 16f, contentHeight);

        // Use the column's own scroll position. A ref-local over a ternary can fail to
        // persist the updated value back to the field between frames, which collapses the
        // scroll view — so branch explicitly instead.
        var scroll = isKnownColumn ? knownScroll : learnableScroll;
        Widgets.BeginScrollView(innerRect, ref scroll, viewRect);
        if (isKnownColumn) knownScroll = scroll; else learnableScroll = scroll;

        var rowY = 0f;
        if (groups != null)
            foreach (var group in groups)
            {
                var catRect = new Rect(0f, rowY, viewRect.width, CategoryHeaderHeight);
                DrawCategoryHeader(catRect, group.Key);
                rowY += CategoryHeaderHeight;

                foreach (var def in group)
                {
                    var rowRect = new Rect(0f, rowY, viewRect.width, RowHeight);
                    DrawRow(rowRect, def, isKnownColumn);
                    rowY += RowHeight;
                }
            }

        Widgets.EndScrollView();
    }

    /// <summary>
    /// Draws a category header row used to separate proficiency groups within a column.
    /// </summary>
    private static void DrawCategoryHeader(Rect rect, string category)
    {
        GUI.color = ColoredText.SubtleGrayColor;
        Text.Font = GameFont.Tiny;
        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.LowerLeft;
        Widgets.Label(rect.ContractedBy(2f, 0f), category.ToUpperInvariant());
        Text.Anchor = anchor;
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
        GUI.color = new Color(1f, 1f, 1f, 0.2f);
        Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
        GUI.color = Color.white;
    }

    /// <summary>
    /// Draws a single proficiency row: known-check or "needs N" prerequisite badge,
    /// label, hover highlight, dependency tooltip, and click action.
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

        // For learnable rows, show how many prerequisites are still missing (if any).
        // Read from the prebuilt cache instead of recomputing (and allocating a HashSet) per row.
        var labelRect = new Rect(iconRect.xMax + 4f, rect.y, rect.width - IconSize - 6f, rect.height);
        int missing = isKnownColumn ? 0 : (cachedMissingCounts != null && cachedMissingCounts.TryGetValue(def, out var m) ? m : 0);
        if (missing > 0)
        {
            var badgeRect = new Rect(rect.xMax - 70f, rect.y, 66f, rect.height);
            labelRect.width -= 70f;
            GUI.color = ColorLibrary.RedReadable;
            Text.Font = GameFont.Tiny;
            var ba = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(badgeRect, "PawnEditor.ProficiencyNeedsN".Translate(missing));
            Text.Anchor = ba;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(labelRect, def.LabelCap);
        Text.Anchor = anchor;

        TooltipHandler.TipRegion(rect, () => GetTooltip(def, isKnownColumn), def.GetHashCode());

        if (Widgets.ButtonInvisible(rect))
        {
            // Defer to end of frame (see pendingAction field comment).
            if (isKnownColumn)
                pendingAction = () => RemoveProficiency(def);
            else
                pendingAction = () => LearnProficiency(def);
        }
    }

    /// <summary>
    /// Grants a proficiency (and its prerequisites) to the pawn, then refreshes derived state.
    /// </summary>
    private void LearnProficiency(Def def)
    {
        LifeLessonsCompat.TryGainProficiency(pawn, def, force: true);
        LifeLessonsCompat.RefreshModifiers(pawn);
        // NOTE: we deliberately do NOT call ReinitializeComp here. The profiler trace [PE-LLDBG]
        // proved that ReinitializeComp re-resolves the proficiency list from scratch and DROPS
        // proficiencies (e.g. gain took the count 20->21, then reinit collapsed it to 18). It was
        // added to fix an NRE in PioneeringComp, but it does more harm than good on the live-edit
        // path. The NRE is handled separately (see LifeLessonsCompat) without nuking the list.
        InvalidateCache(); // pawn's proficiencies changed → rebuild snapshot
        SoundDefOf.Tick_High.PlayOneShotOnCamera();
    }

    /// <summary>
    /// Removes a proficiency from the pawn. Uses Life Lessons' native removeAncestors flag
    /// so anything that depends on this proficiency is removed too, keeping the tree valid.
    /// If dependents would be removed, asks the player to confirm first.
    /// </summary>
    private void RemoveProficiency(Def def)
    {
        var dependents = GetKnownDependents(def);
        if (dependents.Count > 0)
        {
            var names = string.Join(", ", dependents.Select(d => d.LabelCap.ToString()));
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "PawnEditor.ProficiencyRemoveCascade".Translate(def.LabelCap, names),
                () => DoRemove(def)));
            return;
        }

        DoRemove(def);
    }

    /// <summary>
    /// Performs the actual removal (with native ancestor cleanup) and refreshes modifiers.
    /// </summary>
    private void DoRemove(Def def)
    {
        // removeAncestors: true → LL removes everything that has this as a prerequisite.
        LifeLessonsCompat.RemoveProficiency(pawn, def, removeAncestors: true);
        LifeLessonsCompat.RefreshModifiers(pawn);
        // NOTE: no ReinitializeComp here either — see LearnProficiency. It was dropping
        // proficiencies. The PioneeringComp NRE is addressed in LifeLessonsCompat without the
        // destructive full reinit.
        InvalidateCache(); // pawn's proficiencies changed → rebuild snapshot
        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
    }

    /// <summary>
    /// Finds known proficiencies that have the given def among their direct prerequisites,
    /// searching within the supplied completed-list. These are the proficiencies that would
    /// become invalid if this one were removed. Built once into the cache (see EnsureCache).
    /// </summary>
    private List<Def> BuildKnownDependents(Def def, List<Def> completed)
    {
        var result = new List<Def>();
        foreach (var candidate in completed)
        {
            if (candidate == def) continue;
            if (LifeLessonsCompat.GetPrerequisites(candidate).Any(p => p.defName == def.defName))
                result.Add(candidate);
        }
        return result;
    }

    /// <summary>Known dependents of a def, read from the cache (empty if not present).</summary>
    private List<Def> GetKnownDependents(Def def) =>
        cachedDependents != null && cachedDependents.TryGetValue(def, out var list) ? list : new List<Def>();

    // ── Data helpers ──

    private List<Def> GetCompleted() => LifeLessonsCompat.GetCompletedProficiencies(pawn);

    private bool MatchesSearch(Def def)
    {
        if (searchText.NullOrEmpty()) return true;
        return def.LabelCap.ToString().IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Builds the hover tooltip: title, description, category, and the prerequisite chain
    /// with a met (✓) / unmet (✗) marker per prerequisite, plus a click-action hint.
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

        // Prerequisite chain with met/unmet markers.
        var prereqs = LifeLessonsCompat.GetPrerequisites(def);
        if (prereqs.Count > 0)
        {
            var knownNames = cachedKnownNames ?? new HashSet<string>(GetCompleted().Select(d => d.defName));
            sb.AppendLine();
            sb.AppendLine("PawnEditor.ProficiencyRequires".Translate().Colorize(ColoredText.SubtleGrayColor));
            foreach (var prereq in prereqs)
            {
                bool has = knownNames.Contains(prereq.defName);
                var marker = has ? "✓".Colorize(Color.green) : "✗".Colorize(ColorLibrary.RedReadable);
                sb.AppendLine($"  {marker} {prereq.LabelCap}");
            }
        }

        // For known rows, warn if removing will cascade to dependents.
        if (isKnownColumn)
        {
            var dependents = GetKnownDependents(def);
            if (dependents.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("PawnEditor.ProficiencyRequiredBy".Translate(
                    string.Join(", ", dependents.Select(d => d.LabelCap.ToString())))
                    .Colorize(ColorLibrary.RedReadable));
            }
        }

        sb.AppendLine();
        sb.AppendLine(isKnownColumn
            ? "PawnEditor.ProficiencyClickRemove".Translate().Colorize(ColoredText.SubtleGrayColor)
            : "PawnEditor.ProficiencyClickLearn".Translate().Colorize(ColoredText.SubtleGrayColor));

        return sb.ToString();
    }
}
