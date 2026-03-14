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
        PortraitsCache.Clear();
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

        PortraitsCache.Clear();
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

    public static void Select(Pawn pawn)
    {
        selectedPawn = pawn;
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
