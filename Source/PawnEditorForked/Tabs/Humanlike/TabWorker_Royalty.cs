using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[HotSwappable]
public class TabWorker_Royalty : TabWorker<Pawn>
{
    private enum RoyaltyPage
    {
        Titles,
        Permits,
        Psycasts
    }

    private RoyaltyPage page;
    private Vector2 permitScroll;
    private string permitSearch = string.Empty;
    private string favorBuffer;
    private Pawn favorBufferPawn;
    private Vector2 psycastScroll;
    private float psycastViewHeight = 900f;
    private Pawn psycastBufferPawn;
    private string psylinkBuffer;
    private string psyfocusBuffer;
    private string targetPsyfocusBuffer;
    private string entropyBuffer;
    private VPEPsycastEditor vpeEditor;

    public override bool ShowOn(Pawn pawn) => ModsConfig.RoyaltyActive && pawn?.royalty != null;

    public override void DrawTabContents(Rect rect, Pawn pawn)
    {
        if (!ModsConfig.RoyaltyActive || pawn?.royalty == null)
        {
            using (new TextBlock(TextAnchor.MiddleCenter))
                Widgets.Label(rect, "PawnEditor.RoyaltyUnavailable".Translate());
            return;
        }

        var tabs = new List<TabRecord>
        {
            new("PawnEditor.Royalty.Titles".Translate(), () => page = RoyaltyPage.Titles, page == RoyaltyPage.Titles),
            new("PawnEditor.Royalty.Permits".Translate(), () => page = RoyaltyPage.Permits, page == RoyaltyPage.Permits),
            new("PawnEditor.Royalty.Psycasts".Translate(), () => page = RoyaltyPage.Psycasts, page == RoyaltyPage.Psycasts)
        };

        var tabBase = rect;
        tabBase.yMin += TabDrawer.TabHeight;
        TabDrawer.DrawTabs(tabBase, tabs, 240f);
        rect.yMin += TabDrawer.TabHeight + 8f;
        rect = rect.ContractedBy(8f, 4f);

        var empire = Faction.OfEmpire;
        if (page != RoyaltyPage.Psycasts && empire == null)
        {
            using (new TextBlock(TextAnchor.MiddleCenter))
                Widgets.Label(rect, "PawnEditor.Royalty.NoFaction".Translate());
            return;
        }

        switch (page)
        {
            case RoyaltyPage.Titles:
                DrawTitles(rect, pawn, empire);
                break;
            case RoyaltyPage.Psycasts:
                DrawPsycasts(rect, pawn);
                break;
            case RoyaltyPage.Permits:
                DrawPermits(rect, pawn, empire);
                break;
        }
    }

    private void DrawTitles(Rect inRect, Pawn pawn, Faction empire)
    {
        using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft))
            Widgets.Label(inRect.TakeTopPart(42f), pawn.LabelCap);
        inRect.yMin += 8f;

        DrawValueRow(ref inRect, "PawnEditor.Royalty.Faction".Translate(), row =>
        {
            using (new TextBlock(TextAnchor.MiddleLeft))
                Widgets.Label(row, empire.Name);
        });

        var currentTitle = pawn.royalty.GetCurrentTitle(empire);
        DrawValueRow(ref inRect, "PawnEditor.Royalty.CurrentTitle".Translate(), row =>
        {
            if (Widgets.ButtonText(row, currentTitle?.GetLabelCapFor(pawn) ?? "None".Translate()))
                OpenTitleMenu(pawn, empire);
        });

        DrawValueRow(ref inRect, "PawnEditor.Honor".Translate(), row =>
        {
            var currentFavor = pawn.royalty.GetFavor(empire);
            if (favorBufferPawn != pawn)
            {
                favorBufferPawn = pawn;
                favorBuffer = currentFavor.ToString();
            }

            var editedFavor = currentFavor;
            Widgets.TextFieldNumeric(row, ref editedFavor, ref favorBuffer);
            editedFavor = Mathf.Max(0, editedFavor);
            if (editedFavor != currentFavor)
                pawn.royalty.SetFavor(empire, editedFavor, false);
        });

        DrawValueRow(ref inRect, "PawnEditor.Royalty.AvailablePermitPoints".Translate(), row =>
        {
            using (new TextBlock(TextAnchor.MiddleLeft))
                Widgets.Label(row, pawn.royalty.GetPermitPoints(empire).ToString());
        });

        inRect.yMin += 12f;
        if (currentTitle != null)
        {
            Widgets.Label(inRect.TakeTopPart(Text.LineHeight), currentTitle.GetLabelCapFor(pawn).Colorize(ColoredText.TipSectionTitleColor));
            inRect.yMin += 6f;
            var descriptionHeight = Mathf.Max(Text.LineHeight, Text.CalcHeight(currentTitle.description, inRect.width));
            Widgets.Label(inRect.TakeTopPart(descriptionHeight), currentTitle.description);
        }

        inRect.yMin += 18f;
        DrawSuccessors(inRect, pawn, empire);
    }

    private void DrawPsycasts(Rect inRect, Pawn pawn)
    {
        if (psycastBufferPawn != pawn)
        {
            psycastBufferPawn = pawn;
            psylinkBuffer = pawn.GetPsylinkLevel().ToString();
            psyfocusBuffer = (pawn.psychicEntropy.CurrentPsyfocus * 100f).ToString("0.#");
            targetPsyfocusBuffer = (pawn.psychicEntropy.TargetPsyfocus * 100f).ToString("0.#");
            entropyBuffer = pawn.psychicEntropy.EntropyValue.ToString("0.##");
            vpeEditor = VPECompat.Active ? new VPEPsycastEditor(pawn) : null;
        }

        var viewRect = new Rect(0f, 0f, Mathf.Max(0f, inRect.width - 20f), Mathf.Max(inRect.height, psycastViewHeight));
        Widgets.BeginScrollView(inRect, ref psycastScroll, viewRect);

        var listing = new Listing_Standard();
        listing.Begin(viewRect);
        using (new TextBlock(GameFont.Small))
        {
            if (VPECompat.Active)
            {
                vpeEditor ??= new VPEPsycastEditor(pawn);
                vpeEditor.Draw(listing);
            }
            else
            {
                DrawPsycastSectionHeader(listing, "PawnEditor.Royalty.VanillaPsycasts".Translate());
                DrawPsylinkRow(listing, pawn);
                DrawPsyfocusRow(listing, pawn, false);
                DrawPsyfocusRow(listing, pawn, true);
                DrawEntropyRow(listing, pawn);

                var limitEntropy = pawn.psychicEntropy.limitEntropyAmount;
                listing.CheckboxLabeled("PawnEditor.Royalty.LimitEntropy".Translate(), ref limitEntropy);
                if (limitEntropy != pawn.psychicEntropy.limitEntropyAmount)
                {
                    pawn.psychicEntropy.limitEntropyAmount = limitEntropy;
                    PawnEditor.Notify_PointsUsed();
                }

                listing.GapLine();
                DrawLearnedPsycasts(listing, pawn);
            }
        }

        psycastViewHeight = listing.CurHeight + 12f;
        listing.End();
        Widgets.EndScrollView();
    }

    private void DrawPsylinkRow(Listing_Standard listing, Pawn pawn)
    {
        var current = pawn.GetPsylinkLevel();
        var value = current;
        var row = listing.GetRect(30f);
        Widgets.Label(row.LeftPart(0.55f), "PawnEditor.Royalty.PsylinkLevel".Translate());
        Widgets.TextFieldNumeric(row.RightPart(0.35f), ref value, ref psylinkBuffer, 0, pawn.GetMaxPsylinkLevel());
        if (value == current)
            return;

        SetPsylinkLevel(pawn, value);
        PawnEditor.Notify_PointsUsed();
    }

    private void DrawPsyfocusRow(Listing_Standard listing, Pawn pawn, bool target)
    {
        var current = target ? pawn.psychicEntropy.TargetPsyfocus : pawn.psychicEntropy.CurrentPsyfocus;
        var value = current * 100f;
        var row = listing.GetRect(30f);
        Widgets.Label(row.LeftPart(0.55f), (target
            ? "PawnEditor.Royalty.TargetPsyfocus"
            : "PawnEditor.Royalty.CurrentPsyfocus").Translate());

        if (target)
            Widgets.TextFieldNumeric(row.RightPart(0.35f), ref value, ref targetPsyfocusBuffer, 0f, 100f);
        else
            Widgets.TextFieldNumeric(row.RightPart(0.35f), ref value, ref psyfocusBuffer, 0f, 100f);
        value = Mathf.Clamp01(value / 100f);
        if (Math.Abs(value - current) <= 0.0001f)
            return;

        if (target)
            pawn.psychicEntropy.SetPsyfocusTarget(value);
        else
            pawn.psychicEntropy.OffsetPsyfocusDirectly(value - current);
        PawnEditor.Notify_PointsUsed();
    }

    private void DrawEntropyRow(Listing_Standard listing, Pawn pawn)
    {
        var current = pawn.psychicEntropy.EntropyValue;
        var value = current;
        var row = listing.GetRect(30f);
        Widgets.Label(row.LeftPart(0.55f), "PawnEditor.Royalty.Entropy".Translate());
        Widgets.TextFieldNumeric(row.RightPart(0.35f), ref value, ref entropyBuffer, 0f, pawn.psychicEntropy.MaxPotentialEntropy);
        if (Math.Abs(value - current) <= 0.001f)
            return;

        pawn.psychicEntropy.RemoveAllEntropy();
        if (value > 0f)
            pawn.psychicEntropy.TryAddEntropy(value, null, false, true);
        PawnEditor.Notify_PointsUsed();
    }

    private static void DrawLearnedPsycasts(Listing_Standard listing, Pawn pawn)
    {
        var header = listing.GetRect(32f);
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(header.LeftPart(0.65f), "PawnEditor.Royalty.LearnedPsycasts".Translate().Colorize(ColoredText.TipSectionTitleColor));
        if (Widgets.ButtonText(header.RightPartPixels(110f), "Add".Translate().CapitalizeFirst()))
            Find.WindowStack.Add(new ListingMenu_Abilities(pawn, ListingMenu_Abilities.AbilityListMode.Psycasts));

        var learned = pawn.abilities.abilities
            .Where(ability => ability.def.IsPsycast)
            .OrderBy(ability => ability.def.level)
            .ThenBy(ability => ability.def.label ?? ability.def.defName)
            .ToList();
        if (learned.Count == 0)
        {
            listing.Label("PawnEditor.Royalty.NoPsycasts".Translate());
            return;
        }

        foreach (var ability in learned)
        {
            var row = listing.GetRect(34f);
            if (Mouse.IsOver(row))
                Widgets.DrawHighlight(row);
            TooltipHandler.TipRegion(row, ability.Tooltip);

            var deleteRect = row.TakeRightPart(30f).ContractedBy(4f);
            if (Widgets.ButtonImage(deleteRect, TexButton.Delete))
            {
                pawn.abilities.RemoveAbility(ability.def);
                PawnEditor.Notify_PointsUsed();
            }

            var iconRect = row.TakeLeftPart(34f).ContractedBy(3f);
            if (Widgets.ButtonImage(iconRect, ability.def.uiIcon, false))
                Find.WindowStack.Add(new Dialog_InfoCard(ability.def));

            row.xMin += 6f;
            using (new TextBlock(TextAnchor.MiddleLeft))
                Widgets.Label(row, "PawnEditor.Royalty.PsycastLevel".Translate(ability.def.LabelCap, ability.def.level));
        }
    }

    private static void DrawPsycastSectionHeader(Listing_Standard listing, string label)
    {
        using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft))
            Widgets.Label(listing.GetRect(34f), label);
    }

    internal static void SetPsylinkLevel(Pawn pawn, int targetLevel)
    {
        targetLevel = Mathf.Clamp(targetLevel, 0, pawn.GetMaxPsylinkLevel());
        var psylink = pawn.GetMainPsylinkSource();
        if (targetLevel == 0)
        {
            if (psylink != null)
                pawn.health.RemoveHediff(psylink);
            return;
        }

        if (psylink == null)
        {
            pawn.ChangePsylinkLevel(1, false);
            psylink = pawn.GetMainPsylinkSource();
        }

        if (psylink == null)
            return;
        psylink.level = targetLevel;
        psylink.Severity = targetLevel;
        pawn.psychicEntropy.Notify_GainedPsylink();
    }

    private void OpenTitleMenu(Pawn pawn, Faction empire)
    {
        var options = new List<FloatMenuOption>
        {
            new("None".Translate(), () =>
            {
                pawn.royalty.SetTitle(empire, null, false, false, false);
                favorBuffer = "0";
                PawnEditor.Notify_PointsUsed();
            })
        };

        options.AddRange(empire.def.RoyalTitlesAllInSeniorityOrderForReading.Select(title =>
            new FloatMenuOption(title.GetLabelCapFor(pawn), () =>
            {
                pawn.royalty.SetTitle(empire, title, false, false, false);
                favorBuffer = "0";
                PawnEditor.Notify_PointsUsed();
            })));
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void DrawPermits(Rect inRect, Pawn pawn, Faction empire)
    {
        var summaryRect = inRect.TakeTopPart(34f);
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(summaryRect.TakeLeftPart(summaryRect.width * 0.5f),
                "PawnEditor.Royalty.AvailablePermitPointsValue".Translate(pawn.royalty.GetPermitPoints(empire)));

        var searchRect = summaryRect.TakeRightPart(Mathf.Min(360f, summaryRect.width));
        permitSearch = Widgets.TextField(searchRect, permitSearch ?? string.Empty);
        TooltipHandler.TipRegion(searchRect, "PawnEditor.Royalty.SearchPermits".Translate());
        inRect.yMin += 8f;

        var permits = DefDatabase<RoyalTitlePermitDef>.AllDefsListForReading
            .Where(permit => permit.permitPointCost > 0 && (permit.faction == null || permit.faction == empire.def))
            .Where(permit => Matches(permitSearch, permit.LabelCap.ToString(), permit.defName, permit.description))
            .OrderBy(permit => permit.minTitle?.seniority ?? -1)
            .ThenBy(permit => permit.uiPosition.y)
            .ThenBy(permit => permit.uiPosition.x)
            .ThenBy(permit => permit.label)
            .ToList();

        var headingRect = inRect.TakeTopPart(28f);
        DrawPermitColumns(headingRect, "PawnEditor.Royalty.Permit".Translate(), "PawnEditor.Royalty.MinimumTitle".Translate(),
            "PawnEditor.Royalty.Cost".Translate(), "PawnEditor.Royalty.Status".Translate(), string.Empty);

        var viewRect = new Rect(0f, 0f, Mathf.Max(0f, inRect.width - 16f), permits.Count * 44f);
        Widgets.BeginScrollView(inRect, ref permitScroll, viewRect);
        for (var i = 0; i < permits.Count; i++)
        {
            var permit = permits[i];
            var row = new Rect(0f, i * 44f, viewRect.width, 42f);
            if (i % 2 == 0)
                Widgets.DrawAltRect(row);
            Widgets.DrawHighlightIfMouseover(row);
            TooltipHandler.TipRegion(row, permit.description);
            DrawPermitRow(row, pawn, empire, permit);
        }
        Widgets.EndScrollView();
    }

    private static void DrawPermitRow(Rect row, Pawn pawn, Faction empire, RoyalTitlePermitDef permit)
    {
        var directPermit = pawn.royalty.GetPermit(permit, empire);
        var held = pawn.royalty.HasPermit(permit, empire);
        var includedWithTitle = held && directPermit == null;

        var status = directPermit != null
            ? "PawnEditor.Royalty.Granted".Translate()
            : includedWithTitle
                ? "PawnEditor.Royalty.IncludedWithTitle".Translate()
                : "PawnEditor.Royalty.NotGranted".Translate();

        var minimumTitle = permit.minTitle?.GetLabelCapFor(pawn) ?? "None".Translate();
        var action = directPermit != null ? "Remove".Translate() : "PawnEditor.Royalty.Grant".Translate();
        DrawPermitColumns(row.ContractedBy(4f, 3f), permit.LabelCap, minimumTitle, permit.permitPointCost.ToString(), status, action,
            actionRect =>
            {
                if (directPermit != null)
                {
                    if (Widgets.ButtonText(actionRect, action))
                        pawn.royalty.AllFactionPermits.Remove(directPermit);
                    return;
                }

                if (includedWithTitle)
                    return;

                if (Widgets.ButtonText(actionRect, action))
                    pawn.royalty.AddPermit(permit, empire);
            });
    }

    private void DrawSuccessors(Rect inRect, Pawn pawn, Faction empire)
    {
        var title = pawn.royalty.GetCurrentTitle(empire);
        using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft))
            Widgets.Label(inRect.TakeTopPart(42f), "PawnEditor.Royalty.Successors".Translate());
        inRect.yMin += 10f;

        if (title == null)
        {
            Widgets.Label(inRect, "PawnEditor.Royalty.NoTitleForSuccessor".Translate());
            return;
        }

        if (!title.canBeInherited)
        {
            Widgets.Label(inRect, "PawnEditor.Royalty.TitleNotInheritable".Translate(title.GetLabelCapFor(pawn)));
            return;
        }

        var heir = pawn.royalty.GetHeir(empire);
        var card = inRect.TakeTopPart(110f);
        Widgets.DrawLightHighlight(card);
        var iconRect = card.TakeLeftPart(100f).ContractedBy(10f);
        if (heir != null)
            Widgets.DrawTextureFitted(iconRect, PawnEditor.GetPawnTex(heir, iconRect.size, Rot4.South, cameraZoom: 1.8f), 1f);

        card = card.ContractedBy(10f, 8f);
        using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft))
            Widgets.Label(card.TakeTopPart(36f), heir?.LabelCap ?? "None".Translate());
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(card, heir?.Faction?.Name ?? "PawnEditor.Royalty.NoSuccessor".Translate());

        inRect.yMin += 16f;
        var buttons = inRect.TakeTopPart(UIUtility.RegularButtonHeight);
        var clearRect = buttons.TakeRightPart(150f);
        buttons.xMax -= 8f;
        if (Widgets.ButtonText(buttons, "PawnEditor.Royalty.ChooseSuccessor".Translate()))
            OpenSuccessorPicker(pawn, empire);

        var oldColor = GUI.color;
        if (heir == null)
            GUI.color = ColoredText.SubtleGrayColor;
        var clear = Widgets.ButtonText(clearRect, "PawnEditor.Royalty.ClearSuccessor".Translate());
        GUI.color = oldColor;
        if (clear && heir != null)
            pawn.royalty.SetHeir(null, empire);
    }

    private static void OpenSuccessorPicker(Pawn pawn, Faction empire)
    {
        var candidates = PawnBlueprintSaveLoad.GetAllReachablePawnsPublic()
            .Where(candidate => candidate != null && candidate != pawn && !candidate.Dead && candidate.RaceProps.Humanlike)
            .OrderBy(candidate => candidate.LabelShort)
            .ToList();

        Find.WindowStack.Add(new ListingMenu_Pawns(candidates, pawn, "PawnEditor.Royalty.Select".Translate(), selected =>
        {
            pawn.royalty.SetHeir(selected, empire);
            return true;
        }));
    }

    private static void DrawValueRow(ref Rect inRect, string label, Action<Rect> drawer)
    {
        var row = inRect.TakeTopPart(34f);
        inRect.yMin += 6f;
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(row.TakeLeftPart(Mathf.Min(180f, row.width * 0.38f)), label);
        drawer(row);
    }

    private static void DrawPermitColumns(Rect row, string permit, string minimumTitle, string cost, string status, string action,
        Action<Rect> drawAction = null)
    {
        var actionRect = row.TakeRightPart(105f);
        var statusRect = row.TakeRightPart(155f);
        var costRect = row.TakeRightPart(75f);
        var titleRect = row.TakeRightPart(180f);
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(row, permit.Truncate(row.width));
        using (new TextBlock(TextAnchor.MiddleCenter))
        {
            Widgets.Label(titleRect, minimumTitle.Truncate(titleRect.width));
            Widgets.Label(costRect, cost);
            Widgets.Label(statusRect, status.Truncate(statusRect.width));
            if (drawAction == null)
                Widgets.Label(actionRect, action);
        }
        drawAction?.Invoke(actionRect);
    }

    private static bool Matches(string filter, params string[] values)
    {
        if (filter.NullOrEmpty())
            return true;
        return values.Any(value => !value.NullOrEmpty() && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
