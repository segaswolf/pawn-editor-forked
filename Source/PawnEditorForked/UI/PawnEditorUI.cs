using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Main UI orchestrator for the Pawn Editor window.
/// Split into partial classes by responsibility:
///   PawnEditorUI.cs            — Fields, DoUI layout, DoBottomButtons, CanExit
///   PawnEditorUI_SaveLoad.cs   — GetSaveLoadItems, GetRandomizationOptions
///   PawnEditorUI_PawnManagement.cs — RecachePawnList, Select, CheckChangeTabGroup, tabs/widgets
///   PawnEditorUI_Portrait.cs   — GetPawnTex, SavePawnTex, DrawPawnPortrait, graphics helpers
/// </summary>
[HotSwappable]
public static partial class PawnEditor
{
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
    private static FloatMenuOption lastRandomization;
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
        var headerRect = inRect.TakeTopPart(50f);
        headerRect.xMax -= 10f;
        headerRect.yMax -= 20f;
        using (new TextBlock(GameFont.Medium))
            Widgets.Label(headerRect, $"{(Pregame ? "Create" : "PawnEditor.Edit")}Characters".Translate());

        if (ModsConfig.IdeologyActive)
        {
            Text.Font = GameFont.Small;
            string text = "ShowHeadgear".Translate();
            string text2 = "ShowApparel".Translate();
            var width = Mathf.Max(Text.CalcSize(text).x, Text.CalcSize(text2).x) + 4f + 24f;
            var rect2 = headerRect.TakeRightPart(width).TopPartPixels(Text.LineHeight * 2f);
            Widgets.CheckboxLabeled(rect2.TopHalf(), text, ref RenderHeadgear);
            Widgets.CheckboxLabeled(rect2.BottomHalf(), text2, ref RenderClothes);
            headerRect.xMax -= 4f;
        }

        string text3 = "PawnEditor.UsePointLimit".Translate();
        string text4 = "PawnEditor.PointsRemaining".Translate();
        var text5 = remainingPoints.ToStringMoney();
        var num = Text.CalcSize(text4).x;
        var width2 = Mathf.Max(Text.CalcSize(text3).x, num) + 4f + Mathf.Max(Text.CalcSize(text3).x, 24f);
        var rect3 = headerRect.TakeRightPart(width2).TopPartPixels(Text.LineHeight * 2f);
        UIUtility.CheckboxLabeledCentered(rect3.TopHalf(), text3, ref usePointLimit);
        rect3 = rect3.BottomHalf();
        Widgets.Label(rect3.TakeLeftPart(num), text4);
        var pointColor = usePointLimit ? ColoredText.CurrencyColor : ColoredText.SubtleGrayColor;
        using (new TextBlock(TextAnchor.MiddleCenter)) Widgets.Label(rect3, text5.Colorize(pointColor));

        var bottomButtonsRect = inRect.TakeBottomPart(Page.BottomButHeight);

        inRect.yMin -= 10f;
        DoLeftPanel(inRect.TakeLeftPart(134), Pregame);
        inRect.xMin += 12f;
        inRect = inRect.ContractedBy(6);
        inRect.TakeTopPart(40);
        Widgets.DrawMenuSection(inRect);
        if (!tabs.NullOrEmpty() && (showFactionInfo || selectedPawn != null))
            TabDrawer.DrawTabs(inRect, tabs);
        inRect = inRect.ContractedBy(6);
        if (curTab != null)
        {
            if (curTab == widgetTab)
                DoWidgets(inRect);
            else if (showFactionInfo && selectedFaction != null)
                curTab.DrawTabContents(inRect, selectedFaction);
            else if (selectedPawn != null)
                curTab.DrawTabContents(inRect, selectedPawn);
        }

        // v3d9 fix: explicit (Action) cast so the ternary can resolve the target type
        DoBottomButtons(bottomButtonsRect, onClose, Pregame
            ? onNext
            : (Action)(() =>
            {
                if (!showFactionInfo && selectedPawn != null)
                    Find.WindowStack.Add(new FloatMenu(Find.Maps.Select(map => PawnList.GetTeleportOption(map, selectedPawn))
                        .Concat(Find.WorldObjects.Caravans.Select(caravan => PawnList.GetTeleportOption(caravan, selectedPawn)))
                        .Append(PawnList.GetTeleportOption(Find.World, selectedPawn))
                        .Append(new("PawnEditor.Teleport.Specific".Translate(), delegate
                        {
                            onClose();
                            DebugTools.curTool = new("PawnEditor.Teleport".Translate(), () =>
                            {
                                var cell = UI.MouseCell();
                                var map = Find.CurrentMap;
                                if (!cell.Standable(map) || cell.Fogged(map)) return;
                                PawnList.TeleportFromTo(selectedPawn, PawnList.GetLocation(selectedPawn), map);
                                selectedPawn.Position = cell;
                                selectedPawn.Notify_Teleported();
                                DebugTools.curTool = null;
                            });
                        }))
                        .ToList()));
            }));
    }

    // ── Bottom buttons (Close/Start, Save, Load, Randomize, Teleport) ──

    public static void DoBottomButtons(Rect inRect, Action onLeftButton, Action onRightButton)
    {
        Text.Font = GameFont.Small;
        if (Widgets.ButtonText(inRect.TakeLeftPart(Page.BottomButSize.x), Pregame ? "Back".Translate() : "Close".Translate()) && CanExit()) onLeftButton();

        if (Widgets.ButtonText(inRect.TakeRightPart(Page.BottomButSize.x), Pregame ? "Start".Translate() : "PawnEditor.Teleport".Translate())
            && CanExit()) onRightButton();

        var randomRect = new Rect(Vector2.zero, Page.BottomButSize).CenteredOnXIn(inRect).CenteredOnYIn(inRect);

        var buttonRect = new Rect(randomRect);
        var options = GetRandomizationOptions().ToList();

        // Add randomize options for factions
        if (!showFactionInfo && selectedPawn != null && curTab == TabGroupDefOf.Humanlike.tabs[0])
        {
            var randomizeAllWithFactionOption = curTab.GetRandomizationOptions(selectedPawn).Select(option => new FloatMenuOption("PawnEditor.Randomize".Translate() + " " + "PawnEditor.AllIncludingFaction".Translate(), () =>
            {
                foreach (var option in options)
                {
                    if (option.Label.Contains("all"))
                        continue;

                    option.action();
                }
                lastRandomization = option;
                Notify_PointsUsed();
                List<Faction> factions = Find.FactionManager.AllFactionsVisibleInViewOrder.ToList();
                var chosenFaction = factions[Rand.Range(0, factions.Count - 1)];
                selectedFaction = chosenFaction;
                if (chosenFaction != selectedPawn.Faction) selectedPawn.SetFaction(chosenFaction);
                DoRecache();
            })).ToList();

            if (randomizeAllWithFactionOption.Any())
            {
                options.Add(randomizeAllWithFactionOption[0]);

                options.Add(new FloatMenuOption("PawnEditor.SelectRandomFaction".Translate(), () =>
                {
                    List<Faction> factions = Find.FactionManager.AllFactionsVisibleInViewOrder.ToList();
                    var chosenFaction = factions[Rand.Range(0, factions.Count - 1)];
                    selectedFaction = chosenFaction;
                    if (chosenFaction != selectedPawn.Faction) selectedPawn.SetFaction(chosenFaction);
                    DoRecache();
                }));
            }
        }

        if (lastRandomization != null && Widgets.ButtonImageWithBG(randomRect.TakeRightPart(20), TexUI.RotRightTex, new Vector2(12, 12)))
        {
            var label = lastRandomization.Label.ToLower();
            var matched = options.FirstOrDefault(op => op.Label.Contains(label));
            if (matched != null)
            {
                lastRandomization = matched;
                lastRandomization.action();
            }
            randomRect.TakeRightPart(1);
        }

        if (options.Count > 0 && (selectedPawn != null && selectedFaction != null))
            if (Widgets.ButtonText(randomRect, "Randomize".Translate()))
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }

        buttonRect.x -= 5 + buttonRect.width;

        if (Widgets.ButtonText(buttonRect, "Save".Translate()))
            Find.WindowStack.Add(new FloatMenu(GetSaveLoadItems()
                .Select(static item => item.MakeSaveOption())
                .Where(static option => option != null)
                .ToList()));

        buttonRect.x = randomRect.xMax + 5;

        if (Widgets.ButtonText(buttonRect, "Load".Translate()))
            Find.WindowStack.Add(new FloatMenu(GetSaveLoadItems()
                .Select(static item => item.MakeLoadOption())
                .Where(static option => option != null)
                .ToList()));
    }

    public static bool CanExit()
    {
        if (Pregame && usePointLimit && remainingPoints < 0)
        {
            Messages.Message("PawnEditor.NegativePoints".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }
        return true;
    }
}
