using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

// Partial — Pawn/Faction selection, tab management, and pawn list caching.
public static partial class PawnEditor
{
    // ── Pawn list caching ──

    public static void RecachePawnListWithNoFactionPawns()
    {
        needToRecacheNullFactionPawns = true;
        List<Pawn> noFPawns = PawnEditor_PawnsFinder.GetHumanPawnsWithoutFaction();
        CheckChangeTabGroup();
        TabWorker_FactionOverview.RecachePawnsWithPawnList(noFPawns);
        TabWorker_AnimalMech.Notify_PawnAdded(selectedCategory);
        PawnList.UpdateCacheWithNullFaction();
        var pawns = noFPawns;

        if (selectedPawn == null || !pawns.Contains(selectedPawn))
        {
            selectedPawn = pawns.FirstOrDefault();
            CheckChangeTabGroup();
        }
        // Invalidate ONLY the selected pawn's portrait, not the whole game cache. The old code
        // called PortraitsCache.Clear() here, which wipes EVERY portrait in the game. That dumps a
        // large batch of RenderTextures at once, which Unity then has to reclaim via
        // UnloadUnusedAssets — a multi-second stop-the-world that drops the GUI atlas and blacks
        // out the screen (measured at ~4.4s / 165k objects on a heavy modlist). Recaching the pawn
        // LIST does not change any pawn's appearance, so there's nothing to invalidate wholesale;
        // at most the freshly-selected pawn needs a refresh.
        InvalidateSelectedPawnPortrait();
    }

    public static void RecachePawnList()
    {
        if (selectedFaction == null || !Find.FactionManager.allFactions.Contains(selectedFaction))
        {
            selectedFaction = Faction.OfPlayer;
            CheckChangeTabGroup();
        }

        if (selectedPawn is { Faction: { } pawnFaction } && pawnFaction != selectedFaction && Find.FactionManager.allFactions.Contains(pawnFaction))
        {
            selectedFaction = pawnFaction;
            CheckChangeTabGroup();
        }

        if (Pregame && selectedFaction != Faction.OfPlayer)
        {
            selectedFaction = Faction.OfPlayer;
            CheckChangeTabGroup();
        }

        TabWorker_FactionOverview.RecachePawns(selectedFaction);
        TabWorker_AnimalMech.Notify_PawnAdded(selectedCategory);

        List<Pawn> pawns;
        if (Pregame)
            pawns = selectedCategory == PawnCategory.Humans ? Find.GameInitData.startingAndOptionalPawns : StartingThingsManager.GetPawns(selectedCategory);
        else
        {
            PawnList.UpdateCache(selectedFaction, selectedCategory);
            (pawns, _, _) = PawnList.GetLists();
        }

        if (selectedPawn == null || !pawns.Contains(selectedPawn))
        {
            selectedPawn = pawns.FirstOrDefault();
            CheckChangeTabGroup();
        }

        // Invalidate ONLY the selected pawn (see note in RecachePawnListWithNoFactionPawns). This
        // method runs on every editor open, faction change, pawn add/delete, and tab-group change,
        // so the old PortraitsCache.Clear() here was wiping the whole game's portrait cache on each
        // of those — the batch RenderTexture free that triggers Unity's multi-second
        // UnloadUnusedAssets (the black screen). Recaching the list never changes appearances.
        InvalidateSelectedPawnPortrait();
    }

    /// <summary>
    /// Marks only the currently-selected pawn's cached portrait dirty, so it re-renders next time
    /// it's drawn (e.g. after an appearance edit) WITHOUT touching any other pawn's cached
    /// portrait. This replaces the old blanket PortraitsCache.Clear(): clearing the entire cache
    /// frees a large batch of RenderTextures at once, and Unity reclaims that batch with a
    /// UnloadUnusedAssets pass that can take seconds and drop the GUI texture atlas (black screen).
    /// Per-pawn invalidation frees nothing in bulk, so it never feeds that pass.
    /// </summary>
    private static void InvalidateSelectedPawnPortrait()
    {
        if (selectedPawn == null) return;
        try
        {
            PortraitsCache.SetDirty(selectedPawn);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to invalidate portrait for {selectedPawn.LabelShortCap}: {ex.Message}");
        }
    }

    // ── Tab/Widget management ──

    private static void SetTabGroup(TabGroupDef def)
    {
        tabGroup = def;
        curTab = def?.tabs?.FirstOrDefault();
        tabs = def?.tabs?.Select(static tab => new TabRecord(tab.LabelCap, () => curTab = tab, () => curTab == tab)).ToList() ?? new List<TabRecord>();
    }

    public static void CheckChangeTabGroup()
    {
        TabGroupDef desiredTabGroup;

        if (showFactionInfo && selectedFaction != null)
            desiredTabGroup = selectedFaction.IsPlayer ? TabGroupDefOf.PlayerFaction : TabGroupDefOf.NPCFaction;
        else if (showFactionInfo && selectedFaction == null)
            desiredTabGroup = TabGroupDefOf.NPCFaction;
        else if (selectedPawn != null)
            desiredTabGroup = selectedCategory == PawnCategory.Humans ? TabGroupDefOf.Humanlike : TabGroupDefOf.AnimalMech;
        else desiredTabGroup = null;

        if (desiredTabGroup != tabGroup)
            SetTabGroup(desiredTabGroup);

        RecacheWidgets();
    }

    private static void RecacheWidgets()
    {
        if (cachedWidgetTab != null) tabs.Remove(cachedWidgetTab);

        Func<WidgetDef, bool> predicate;
        if (showFactionInfo && selectedFaction != null) predicate = def => def.type == TabDef.TabType.Faction && def.ShowOn(selectedFaction);
        else if (selectedPawn != null) predicate = def => def.type == TabDef.TabType.Pawn && def.ShowOn(selectedPawn);
        else predicate = _ => false;

        widgets = DefDatabase<WidgetDef>.AllDefs.Where(predicate).ToList();

        if (widgets.NullOrEmpty())
            cachedWidgetTab = null;
        else
        {
            cachedWidgetTab = new(widgetTab.LabelCap, static () => curTab = widgetTab, static () => curTab == widgetTab);
            tabs.Add(cachedWidgetTab);
        }
    }

    // ── Selection ──

    // Pawns whose Life Lessons PioneeringComp we've already re-synced this session (Option A+).
    // Older Pawn Editor builds could leave a pawn's PioneeringComp with null activity lists,
    // which then throw an NRE every tick in PioneeringComp.UnexhaustActivities. We repair it the
    // first time the user selects that pawn in the editor (not just when opening proficiencies),
    // so simply browsing the colony in the editor heals the damaged pawns. Once per pawn per
    // session; the repair is surgical (re-creates only the null lists, never touches proficiencies).
    private static readonly HashSet<int> pioneeringRepairedIds = new();

    public static void Select(Pawn pawn)
    {
        selectedPawn = pawn;

        // Option A+: heal the PioneeringComp NRE on first selection of this pawn.
        if (pawn != null && LifeLessonsCompat.Active && pioneeringRepairedIds.Add(pawn.thingIDNumber))
            LifeLessonsCompat.RepairPioneeringComp(pawn);

        var recache = false;
        if (pawn.Faction != selectedFaction)
        {
            selectedFaction = pawn.Faction;
            recache = true;
        }

        showFactionInfo = false;
        if (!selectedCategory.Includes(pawn))
        {
            selectedCategory = pawn.RaceProps.Humanlike ? PawnCategory.Humans : pawn.RaceProps.IsMechanoid ? PawnCategory.Mechs : PawnCategory.Animals;
            recache = true;
        }

        if (recache || tabGroup == TabGroupDefOf.PlayerFaction || tabGroup == TabGroupDefOf.NPCFaction)
        {
            CheckChangeTabGroup();
            DoRecache();
        }
    }

    public static void Select(Faction faction)
    {
        selectedFaction = faction;
        selectedPawn = null;
        showFactionInfo = true;
        CheckChangeTabGroup();
    }

    public static void GotoTab(TabDef tab)
    {
        curTab = tab;
    }
}
