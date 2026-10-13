using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>The Pawn Editor window: shared state, layout and the bottom buttons.</summary>
/// <remarks>
/// <para>Split by responsibility across partial files:</para>
/// <list type="bullet">
/// <item><c>PawnEditorUI.cs</c>: shared state, window layout, bottom buttons.</item>
/// <item><c>LeftPanel.cs</c>: faction and category pickers, Add button.</item>
/// <item><c>PawnList.cs</c>: the scrollable, reorderable pawn list.</item>
/// <item><c>PawnEditorUI_AddRemovePawns.cs</c>: adding and deleting pawns.</item>
/// <item><c>PawnEditorUI_SaveLoad.cs</c>: save, load and randomization menus.</item>
/// <item><c>PawnEditorUI_PawnManagement.cs</c>: list recaching, selection, tabs and widgets.</item>
/// <item><c>PawnEditorUI_Portrait.cs</c>: portraits and graphics helpers.</item>
/// </list>
/// </remarks>
[HotSwappable]
public static partial class PawnEditor
{
    private const float HeaderHeight = 50f;
    private const float LeftPanelWidth = 134f;
    private const float TabStripReserve = 40f;
    private const float RepeatButtonSize = 24f;

    // ── Shared state (accessible from all partial files) ──
    public static bool RenderClothes = true;
    public static bool RenderHeadgear = true;
    private static bool usePointLimit;
    private static float remainingPoints;
    public static Faction selectedFaction;
    private static Pawn selectedPawn;
    private static bool showFactionInfo;
    public static PawnCategory selectedCategory;
    private static float cachedValue;
    // Index of the last randomization option used (-1 = none yet), so "repeat" re-runs this frame's entry.
    private static int lastRandomizationIndex = -1;
    private static TabGroupDef tabGroup;
    private static List<TabRecord> tabs;
    private static TabDef curTab;
    private static List<WidgetDef> widgets;
    private static int startingSilver;

    private static readonly TabDef widgetTab = new()
    {
        defName = "Widgets",
        label = "MiscRecordsCategory".Translate()
    };

    public static PawnLister PawnList = new();
    public static PawnListerBase AllPawns = new();

    private static Rot4 curRot = Rot4.South;

    public static bool Pregame;

    private static TabRecord cachedWidgetTab;

    // ── Main UI layout ──

    public static void DoUI(Rect inRect, Action onClose, Action onNext)
    {
        var headerRect = inRect.TakeTopPart(HeaderHeight);
        headerRect.xMax -= 10f;
        headerRect.yMax -= 20f;
        DrawTitle(headerRect);
        if (ModsConfig.IdeologyActive)
            DrawRenderToggles(ref headerRect);
        DrawPointsDisplay(ref headerRect);

        var bottomButtonsRect = inRect.TakeBottomPart(Page.BottomButHeight);
        DrawBody(inRect);

        Action onRightButton = Pregame ? onNext : () => OpenTeleportMenu(onClose);
        DoBottomButtons(bottomButtonsRect, onClose, onRightButton);
    }

    private static void DrawTitle(Rect rect)
    {
        using (new TextBlock(GameFont.Medium))
            Widgets.Label(rect, $"{(Pregame ? "Create" : "PawnEditor.Edit")}Characters".Translate());
    }

    private static void DrawRenderToggles(ref Rect headerRect)
    {
        Text.Font = GameFont.Small;
        string headgear = "ShowHeadgear".Translate();
        string apparel = "ShowApparel".Translate();
        var width = Mathf.Max(Text.CalcSize(headgear).x, Text.CalcSize(apparel).x) + 4f + 24f;
        var rect = headerRect.TakeRightPart(width).TopPartPixels(Text.LineHeight * 2f);
        Widgets.CheckboxLabeled(rect.TopHalf(), headgear, ref RenderHeadgear);
        Widgets.CheckboxLabeled(rect.BottomHalf(), apparel, ref RenderClothes);
        headerRect.xMax -= 4f;
    }

    private static void DrawPointsDisplay(ref Rect headerRect)
    {
        string useLimit = "PawnEditor.UsePointLimit".Translate();
        string remaining = "PawnEditor.PointsRemaining".Translate();
        var remainingWidth = Text.CalcSize(remaining).x;
        var width = Mathf.Max(Text.CalcSize(useLimit).x, remainingWidth) + 4f + Mathf.Max(Text.CalcSize(useLimit).x, 24f);
        var rect = headerRect.TakeRightPart(width).TopPartPixels(Text.LineHeight * 2f);
        UIUtility.CheckboxLabeledCentered(rect.TopHalf(), useLimit, ref usePointLimit);

        var valueRow = rect.BottomHalf();
        Widgets.Label(valueRow.TakeLeftPart(remainingWidth), remaining);
        var color = usePointLimit ? ColoredText.CurrencyColor : ColoredText.SubtleGrayColor;
        using (new TextBlock(TextAnchor.MiddleCenter))
            Widgets.Label(valueRow, ("$" + Mathf.RoundToInt(remainingPoints)).Colorize(color));
    }

    /// <summary>Left panel, then the tab strip and the current tab's contents.</summary>
    private static void DrawBody(Rect inRect)
    {
        inRect.yMin -= 10f;
        DoLeftPanel(inRect.TakeLeftPart(LeftPanelWidth), Pregame);
        inRect.xMin += 12f;
        inRect = inRect.ContractedBy(6);
        inRect.TakeTopPart(TabStripReserve);
        Widgets.DrawMenuSection(inRect);
        if (!tabs.NullOrEmpty() && (showFactionInfo || selectedPawn != null))
            TabDrawer.DrawTabs(inRect, tabs);
        DrawCurrentTab(inRect.ContractedBy(6));
    }

    private static void DrawCurrentTab(Rect rect)
    {
        if (curTab == null) return;

        if (curTab == widgetTab)
            DoWidgets(rect);
        else if (showFactionInfo && selectedFaction != null)
            curTab.DrawTabContents(rect, selectedFaction);
        else if (selectedPawn != null)
            curTab.DrawTabContents(rect, selectedPawn);
    }

    // ── Teleport (the right button in game) ──

    private static void OpenTeleportMenu(Action onClose)
    {
        if (showFactionInfo || selectedPawn == null) return;
        Find.WindowStack.Add(new FloatMenu(TeleportOptions(onClose)));
    }

    /// <summary>Every map, every caravan, the world, and "pick a cell" on the current map.</summary>
    private static List<FloatMenuOption> TeleportOptions(Action onClose) =>
        Find.Maps.Select(map => PawnList.GetTeleportOption(map, selectedPawn))
            .Concat(Find.WorldObjects.Caravans.Select(caravan => PawnList.GetTeleportOption(caravan, selectedPawn)))
            .Append(PawnList.GetTeleportOption(Find.World, selectedPawn))
            .Append(new FloatMenuOption("PawnEditor.Teleport.Specific".Translate(), () =>
            {
                onClose();
                DebugTools.curTool = new("PawnEditor.Teleport".Translate(), TeleportSelectedPawnToMouseCell);
            }))
            .ToList();

    private static void TeleportSelectedPawnToMouseCell()
    {
        var cell = UI.MouseCell();
        var map = Find.CurrentMap;
        if (!cell.Standable(map) || cell.Fogged(map)) return;

        PawnList.TeleportFromTo(selectedPawn, PawnList.GetLocation(selectedPawn), map);
        selectedPawn.Position = cell;
        selectedPawn.Notify_Teleported();
        DebugTools.curTool = null;
    }

    // ── Bottom buttons: Close/Back, Delete, Randomize, Save, Load, Teleport/Start ──

    public static void DoBottomButtons(Rect inRect, Action onLeftButton, Action onRightButton)
    {
        Text.Font = GameFont.Small;
        DrawExitButtons(ref inRect, onLeftButton, onRightButton);

        var row = BottomButtonRow.Within(inRect, Page.BottomButSize);
        var options = RandomizeMenuOptions();
        var randomizeRect = row.Randomize;
        DrawRepeatRandomizeButton(ref randomizeRect, options);
        DrawRandomizeButton(randomizeRect, options);
        DrawDeleteButton(row.Delete);
        DrawSaveLoadButton(row.Save, "Save".Translate(), item => item.MakeSaveOption());
        DrawSaveLoadButton(row.Load, "Load".Translate(), item => item.MakeLoadOption());
    }

    private static void DrawExitButtons(ref Rect inRect, Action onLeftButton, Action onRightButton)
    {
        var leftLabel = Pregame ? "Back".Translate() : "Close".Translate();
        if (Widgets.ButtonText(inRect.TakeLeftPart(Page.BottomButSize.x), leftLabel) && CanExit())
            onLeftButton();

        var rightLabel = Pregame ? "Start".Translate() : "PawnEditor.Teleport".Translate();
        if (Widgets.ButtonText(inRect.TakeRightPart(Page.BottomButSize.x), rightLabel) && CanExit())
            onRightButton();
    }

    /// <summary>The current tab's options, plus the faction options on the main Bio tab.</summary>
    private static List<FloatMenuOption> RandomizeMenuOptions()
    {
        var options = GetRandomizationOptions().ToList();
        if (showFactionInfo || selectedPawn == null || curTab != TabGroupDefOf.Humanlike.tabs[0]
            || !curTab.GetRandomizationOptions(selectedPawn).Any())
            return options;

        // A copy taken before adding the faction options, so "all - including faction" can never run itself.
        var tabOptions = options.ToList();
        options.Add(new FloatMenuOption("PawnEditor.Randomize".Translate() + " " + "PawnEditor.AllIncludingFaction".Translate(),
            () => RandomizeAllIncludingFaction(tabOptions)));
        options.Add(new FloatMenuOption("PawnEditor.SelectRandomFaction".Translate(), RandomizeSelectedPawnFaction));
        return options;
    }

    private static void RandomizeAllIncludingFaction(List<FloatMenuOption> tabOptions)
    {
        foreach (var option in tabOptions)
        {
            // Skips the tab's own "all" entry, which would repeat the others. Matches the English label
            // only; in other languages that entry runs too, which is redundant but harmless.
            if (option.Label.Contains("all")) continue;
            option.action();
        }

        Notify_PointsUsed();
        RandomizeSelectedPawnFaction();
    }

    private static void RandomizeSelectedPawnFaction()
    {
        var faction = Find.FactionManager.AllFactionsVisibleInViewOrder.ToList().RandomElement();
        selectedFaction = faction;
        if (faction != selectedPawn.Faction)
            selectedPawn.SetFaction(faction);
        DoRecache();
    }

    /// <summary>Small square at the right end of the Randomize slot that repeats the last option.</summary>
    private static void DrawRepeatRandomizeButton(ref Rect randomizeRect, List<FloatMenuOption> options)
    {
        var canRepeat = lastRandomizationIndex >= 0 && lastRandomizationIndex < options.Count;
        if (!canRepeat || randomizeRect.width < BottomButtonRow.MinButtonWidth + RepeatButtonSize) return;

        var repeatRect = randomizeRect.TakeRightPart(RepeatButtonSize);
        if (Widgets.ButtonImageWithBG(repeatRect, TexUI.RotRightTex, new Vector2(12, 12)))
            options[lastRandomizationIndex].action();
    }

    private static void DrawRandomizeButton(Rect rect, List<FloatMenuOption> options)
    {
        if (options.Count == 0 || selectedPawn == null || selectedFaction == null) return;
        if (Widgets.ButtonText(rect, "Randomize".Translate()))
            Find.WindowStack.Add(new FloatMenu(options));
    }

    private static void DrawDeleteButton(Rect rect)
    {
        var canDelete = !showFactionInfo && selectedPawn != null;
        if (Widgets.ButtonText(rect, "Delete".Translate(), active: canDelete))
            ConfirmDeletePawn(selectedPawn);
    }

    private static void DrawSaveLoadButton(Rect rect, string label, Func<SaveLoadItem, FloatMenuOption> makeOption)
    {
        if (!Widgets.ButtonText(rect, label)) return;

        Find.WindowStack.Add(new FloatMenu(GetSaveLoadItems()
            .Select(makeOption)
            .Where(static option => option != null)
            .ToList()));
    }

    public static bool CanExit()
    {
        if (!Pregame || !usePointLimit || remainingPoints >= 0) return true;

        Messages.Message("PawnEditor.NegativePoints".Translate(), MessageTypeDefOf.RejectInput, false);
        return false;
    }
}
