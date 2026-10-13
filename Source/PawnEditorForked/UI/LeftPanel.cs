using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PawnEditor;

// The left panel: faction picker and card, category picker, Add button and the pawn list.
public static partial class PawnEditor
{
    private const float FactionButtonHeight = 30f;
    private const float FactionButtonGap = 2f;
    private const float FactionCardHeight = 54f;
    private const float FactionCardPadding = 3f;
    private const float CategoryButtonHeight = 30f;
    private const float AddButtonHeight = 25f;
    private const float SectionGap = 4f;
    private const float PawnListTopGap = 12f;
    private const float PawnListHeight = 415f;
    private const float FactionHintOverflow = 12f;

    private static bool needToRecacheNullFactionPawns;

    /// <summary>Recaches the pawn list, with or without a selected faction.</summary>
    public static void DoRecache()
    {
        if (selectedFaction == null)
            RecachePawnListWithNoFactionPawns();
        else
            RecachePawnList();
    }

    private static void DoLeftPanel(Rect inRect, bool pregame)
    {
        DrawSmallLabel(ref inRect, "PawnEditor.SelectedFaction".Translate());
        RecacheIfNeeded(pregame);
        if (!pregame)
            DrawSelectFactionButton(ref inRect);
        DrawFactionCard(ref inRect);
        DrawFactionHint(ref inRect);

        inRect.yMin += SectionGap;
        DrawSmallLabel(ref inRect, "PawnEditor.SelectedCategory".Translate());
        DrawCategoryButton(ref inRect);
        DrawAddButton(ref inRect);

        if (pregame)
            TryRefreshSelectedPawn();
        var list = pregame ? PregamePawnList() : InGamePawnList();

        inRect.yMin += PawnListTopGap;
        if (list.Pawns.Any() && Layout.TryTakeTop(ref inRect, Mathf.Min(PawnListHeight, inRect.height), out var listRect))
            DoPawnList(listRect, list.Pawns, list.Sections, list.OnReorder);
    }

    private static void DrawSmallLabel(ref Rect rect, string label)
    {
        using (new TextBlock(GameFont.Tiny))
        {
            if (Layout.TryTakeTop(ref rect, Text.LineHeight, out var row))
                Widgets.Label(row, label);
        }
    }

    private static void RecacheIfNeeded(bool pregame)
    {
        if (selectedFaction == null && needToRecacheNullFactionPawns)
        {
            RecachePawnListWithNoFactionPawns();
            needToRecacheNullFactionPawns = false;
        }
        else if (pregame && selectedFaction != Faction.OfPlayer)
            RecachePawnList();
    }

    private static void DrawSelectFactionButton(ref Rect rect)
    {
        if (!Layout.TryTakeTop(ref rect, FactionButtonHeight + FactionButtonGap, out var row)) return;

        row.yMax -= FactionButtonGap;
        if (Widgets.ButtonText(row, "PawnEditor.SelectFaction".Translate()))
            Find.WindowStack.Add(new FloatMenu(FactionMenuOptions()));
    }

    /// <summary>Every faction with at least one humanlike (Ancients included), plus "No faction".</summary>
    private static List<FloatMenuOption> FactionMenuOptions()
    {
        var options = PawnEditor_PawnsFinder.GetAllFactionsContainingAtLeastOneHumanLike()
            .Select(faction => new FloatMenuOption(faction.Name, () => SelectFaction(faction), faction.def.FactionIcon, faction.Color))
            .ToList();
        options.Add(new FloatMenuOption("PawnEditor.NoFaction".Translate(), SelectNoFaction));
        return options;
    }

    private static void SelectFaction(Faction faction)
    {
        selectedFaction = faction;
        selectedPawn = null;
        RecachePawnList();
        CheckChangeTabGroup();
    }

    private static void SelectNoFaction()
    {
        selectedFaction = null;
        selectedPawn = null;
        needToRecacheNullFactionPawns = true;
        CheckChangeTabGroup();
    }

    private static void DrawFactionCard(ref Rect rect)
    {
        if (!Layout.TryTakeTop(ref rect, FactionCardHeight, out var row)) return;

        var card = row.ContractedBy(FactionCardPadding);
        Widgets.DrawOptionBackground(card, showFactionInfo);
        MouseoverSounds.DoRegion(card);
        DrawFactionIcon(card.ContractedBy(6).RightPart(0.25f).BottomPart(0.75f));
        using (new TextBlock(GameFont.Small))
            Widgets.Label(card.ContractedBy(5f), selectedFaction?.Name ?? "PawnEditor.NoFaction".Translate().ToString());

        if (!Widgets.ButtonInvisible(card)) return;
        showFactionInfo = !showFactionInfo;
        CheckChangeTabGroup();
    }

    /// <summary>Faint faction icon in the card's corner; an empty box when there is no faction.</summary>
    private static void DrawFactionIcon(Rect rect)
    {
        var color = selectedFaction?.Color ?? Color.white;
        color.a = 0.2f;
        GUI.color = color;
        if (selectedFaction == null)
            Widgets.DrawBox(rect);
        else if (selectedFaction.def.FactionIcon != null)
            GUI.DrawTexture(rect, selectedFaction.def.FactionIcon);
        GUI.color = Color.white;
    }

    private static void DrawFactionHint(ref Rect rect)
    {
        if (!Layout.TryTakeTop(ref rect, Text.LineHeight, out var row)) return;

        // Allowed to spill a little to the right: the panel is narrow and the hint is one line.
        row.xMax += FactionHintOverflow;
        using (new TextBlock(GameFont.Tiny))
            Widgets.Label(row, "PawnEditor.ClickFactionOverview".Translate().Colorize(ColoredText.SubtleGrayColor));
    }

    private static void DrawCategoryButton(ref Rect rect)
    {
        if (Layout.TryTakeTop(ref rect, CategoryButtonHeight, out var row)
            && Widgets.ButtonText(row, selectedCategory.LabelCapPlural()))
            Find.WindowStack.Add(new FloatMenu(CategoryMenuOptions()));
    }

    private static List<FloatMenuOption> CategoryMenuOptions() =>
        Enum.GetValues(typeof(PawnCategory))
            .Cast<PawnCategory>()
            .Where(category => category != PawnCategory.All)
            .Select(category => new FloatMenuOption(category.LabelCapPlural(), () => SelectCategory(category)))
            .ToList();

    private static void SelectCategory(PawnCategory category)
    {
        selectedCategory = category;
        DoRecache();
        CheckChangeTabGroup();
    }

    private static void DrawAddButton(ref Rect rect)
    {
        if (!Layout.TryTakeTop(ref rect, AddButtonHeight, out var row)
            || !Widgets.ButtonText(row, "Add".Translate().CapitalizeFirst())) return;

        if (selectedFaction != Faction.OfPlayer && selectedCategory == PawnCategory.Mechs)
            Messages.Message("PawnEditor.NoAddMechWarning".Translate(), MessageTypeDefOf.RejectInput);
        else
            AddPawn(selectedCategory);
    }

    /// <summary>What the pawn list shows: the pawns, their section headers and how to reorder them.</summary>
    private readonly struct PawnListView
    {
        public readonly List<Pawn> Pawns;
        public readonly List<string> Sections;
        public readonly Action<Pawn, int, int> OnReorder;

        public PawnListView(List<Pawn> pawns, List<string> sections, Action<Pawn, int, int> onReorder)
        {
            Pawns = pawns;
            Sections = sections;
            OnReorder = onReorder;
        }
    }

    private static PawnListView PregamePawnList() =>
        selectedCategory == PawnCategory.Humans ? StartingColonistsList() : StartingThingsPawnList();

    /// <summary>Starting colonists, split into "selected" and "left behind".</summary>
    private static PawnListView StartingColonistsList()
    {
        var pawns = Find.GameInitData.startingAndOptionalPawns;
        if (!pawns.Any()) return new PawnListView(pawns, null, null);

        var sections = Enumerable.Repeat<string>(null, pawns.Count).ToList();
        sections[0] = "StartingPawnsSelected".Translate();
        if (Find.GameInitData.startingPawnCount < sections.Count)
            sections[Find.GameInitData.startingPawnCount] = "StartingPawnsLeftBehind".Translate();
        return new PawnListView(pawns, sections, PawnList.OnReorder);
    }

    private static PawnListView StartingThingsPawnList()
    {
        var pawns = StartingThingsManager.GetPawns(selectedCategory);
        if (!pawns.Any()) return new PawnListView(pawns, null, null);

        var sections = Enumerable.Repeat<string>(null, pawns.Count).ToList();
        sections[0] = "StartingPawnsSelected".Translate();
        return new PawnListView(pawns, sections, (_, _, _) => { });
    }

    private static PawnListView InGamePawnList()
    {
        var (pawns, sections, _) = PawnList.GetLists();
        if (selectedFaction == null)
        {
            pawns = PawnEditor_PawnsFinder.GetHumanPawnsWithoutFaction();
            if (!pawns.Any())
                sections = new();
        }

        return new PawnListView(pawns, sections, PawnList.OnReorder);
    }

    /// <summary>Pregame: if the selected pawn is no longer in the list, select the first one.</summary>
    private static void TryRefreshSelectedPawn()
    {
        var pawns = selectedCategory == PawnCategory.Humans
            ? Find.GameInitData.startingAndOptionalPawns
            : StartingThingsManager.GetPawns(selectedCategory);
        if (pawns.Contains(selectedPawn)) return;

        selectedPawn = pawns.FirstOrDefault();
        RefreshTabs();
    }
}
