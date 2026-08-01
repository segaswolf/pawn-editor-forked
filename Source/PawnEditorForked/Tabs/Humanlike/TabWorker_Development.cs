using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[HotSwappable]
public class TabWorker_Development : TabWorker<Pawn>
{
    private const float SectionGap = 12f;
    private const float WantRowHeight = 70f;
    private const float QuirkRowHeight = 56f;

    private Vector2 wantsScroll;
    private Vector2 quirksScroll;
    private string wantsSearch = string.Empty;
    private string quirksSearch = string.Empty;

    public override bool ShowOn(Pawn pawn) => CharacterDevelopmentCompat.CanEdit(pawn);

    public override void DrawTabContents(Rect rect, Pawn pawn)
    {
        rect = rect.ContractedBy(8f, 4f);
        DrawCharacterDevelopment(rect, pawn);
    }

    private void DrawCharacterDevelopment(Rect rect, Pawn pawn)
    {
        if (!CharacterDevelopmentCompat.CanEdit(pawn))
        {
            using (new TextBlock(TextAnchor.MiddleCenter))
                Widgets.Label(rect, "PawnEditor.Development.CharacterUnavailable".Translate());
            return;
        }

        using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft))
            Widgets.Label(rect.TakeTopPart(36f), pawn.LabelCap);
        rect.yMin += 6f;

        var wantsWidth = (rect.width - SectionGap) * 0.65f;
        var wantsRect = new Rect(rect.x, rect.y, wantsWidth, rect.height);
        var quirksRect = new Rect(wantsRect.xMax + SectionGap, rect.y, rect.width - wantsWidth - SectionGap, rect.height);
        DrawWants(wantsRect, pawn);
        DrawQuirks(quirksRect, pawn);
    }

    private void DrawWants(Rect rect, Pawn pawn)
    {
        Widgets.DrawMenuSection(rect);
        rect = rect.ContractedBy(6f);

        var header = rect.TakeTopPart(32f);
        var addRect = header.TakeRightPart(90f);
        using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft))
            Widgets.Label(header, "PawnEditor.Development.Wants".Translate());
        if (Widgets.ButtonText(addRect, "Add".Translate().CapitalizeFirst()))
            OpenWantPicker(pawn);

        var searchRect = rect.TakeTopPart(28f);
        wantsSearch = Widgets.TextField(searchRect, wantsSearch ?? string.Empty);
        DrawSearchPlaceholder(searchRect, wantsSearch);
        rect.yMin += 5f;

        var wants = CharacterDevelopmentCompat.GetActiveWants(pawn)
            .Where(want => Matches(wantsSearch,
                CharacterDevelopmentCompat.GetLabel(want),
                CharacterDevelopmentCompat.GetDescription(want)))
            .ToList();

        if (wants.Count == 0)
        {
            using (new TextBlock(TextAnchor.MiddleCenter))
                Widgets.Label(rect, wantsSearch.NullOrEmpty()
                    ? "PawnEditor.Development.NoWants".Translate()
                    : "PawnEditor.NoSearchResults".Translate());
            return;
        }

        var viewRect = new Rect(0f, 0f, Mathf.Max(0f, rect.width - 16f), wants.Count * WantRowHeight);
        Widgets.BeginScrollView(rect, ref wantsScroll, viewRect);
        for (var index = 0; index < wants.Count; index++)
        {
            var want = wants[index];
            var row = new Rect(0f, index * WantRowHeight, viewRect.width, WantRowHeight - 2f);
            DrawWantRow(row, pawn, want, index);
        }
        Widgets.EndScrollView();
    }

    private static void DrawWantRow(Rect row, Pawn pawn, object want, int index)
    {
        if (CharacterDevelopmentCompat.IsMentalBreakWant(want))
            Widgets.DrawBoxSolid(row, new Color(0.28f, 0.14f, 0.14f, 0.55f));
        else if (index % 2 == 0)
            Widgets.DrawAltRect(row);

        Widgets.DrawHighlightIfMouseover(row);
        TooltipHandler.TipRegion(row, CharacterDevelopmentCompat.GetDescription(want));

        var removeRect = row.TakeRightPart(30f).ContractedBy(4f);
        if (Widgets.ButtonImage(removeRect, TexButton.Delete))
        {
            if (CharacterDevelopmentCompat.RemoveWant(pawn, want))
                PawnEditor.Notify_PointsUsed();
            return;
        }
        TooltipHandler.TipRegion(removeRect, "PawnEditor.Development.RemoveWant".Translate());

        var completeRect = row.TakeRightPart(74f).ContractedBy(3f, 15f);
        if (Widgets.ButtonText(completeRect, "PawnEditor.Development.Complete".Translate()))
        {
            if (CharacterDevelopmentCompat.CompleteWant(pawn, want))
                PawnEditor.Notify_PointsUsed();
            return;
        }
        TooltipHandler.TipRegion(completeRect, "PawnEditor.Development.CompleteDesc".Translate());

        if (CharacterDevelopmentCompat.CanRerollWant(want))
        {
            var rerollRect = row.TakeRightPart(70f).ContractedBy(3f, 15f);
            if (Widgets.ButtonText(rerollRect, "PawnEditor.Development.Reroll".Translate()))
            {
                if (CharacterDevelopmentCompat.RerollWant(pawn, want))
                    PawnEditor.Notify_PointsUsed();
                return;
            }
            TooltipHandler.TipRegion(rerollRect,
                "PawnEditor.Development.RerollsRemaining".Translate(CharacterDevelopmentCompat.GetRerollsRemaining(want)));
        }

        var iconRect = row.TakeLeftPart(52f).ContractedBy(7f);
        var icon = CharacterDevelopmentCompat.GetIcon(want) ?? Widgets.PlaceholderIconTex;
        Widgets.DrawTextureFitted(iconRect, icon, 1f);

        row = row.ContractedBy(4f, 5f);
        using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, false))
            Widgets.Label(row.TakeTopPart(25f), CharacterDevelopmentCompat.GetLabel(want).Truncate(row.width));
        using (new TextBlock(GameFont.Tiny, TextAnchor.UpperLeft, false))
        {
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(row, CharacterDevelopmentCompat.GetDescription(want).Truncate(row.width));
            GUI.color = Color.white;
        }
    }

    private void DrawQuirks(Rect rect, Pawn pawn)
    {
        Widgets.DrawMenuSection(rect);
        rect = rect.ContractedBy(6f);

        var header = rect.TakeTopPart(32f);
        var addRect = header.TakeRightPart(82f);
        using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft))
            Widgets.Label(header, "PawnEditor.Development.Quirks".Translate());
        if (Widgets.ButtonText(addRect, "Add".Translate().CapitalizeFirst()))
            OpenQuirkPicker(pawn);

        var searchRect = rect.TakeTopPart(28f);
        quirksSearch = Widgets.TextField(searchRect, quirksSearch ?? string.Empty);
        DrawSearchPlaceholder(searchRect, quirksSearch);
        rect.yMin += 5f;

        if (!CharacterDevelopmentCompat.HasQuirkTargetMap(pawn))
        {
            var noticeRect = rect.TakeTopPart(44f);
            GUI.color = ColoredText.SubtleGrayColor;
            using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleLeft))
                Widgets.Label(noticeRect, "PawnEditor.Development.TargetQuirksRequireMap".Translate());
            GUI.color = Color.white;
            rect.yMin += 5f;
        }

        var quirks = CharacterDevelopmentCompat.GetQuirks(pawn)
            .Where(quirk => Matches(quirksSearch,
                CharacterDevelopmentCompat.GetLabel(quirk),
                CharacterDevelopmentCompat.GetDescription(quirk)))
            .ToList();

        if (quirks.Count == 0)
        {
            using (new TextBlock(TextAnchor.MiddleCenter))
                Widgets.Label(rect, quirksSearch.NullOrEmpty()
                    ? "PawnEditor.Development.NoQuirks".Translate()
                    : "PawnEditor.NoSearchResults".Translate());
            return;
        }

        var viewRect = new Rect(0f, 0f, Mathf.Max(0f, rect.width - 16f), quirks.Count * QuirkRowHeight);
        Widgets.BeginScrollView(rect, ref quirksScroll, viewRect);
        for (var index = 0; index < quirks.Count; index++)
        {
            var quirk = quirks[index];
            var row = new Rect(0f, index * QuirkRowHeight, viewRect.width, QuirkRowHeight - 2f);
            DrawQuirkRow(row, pawn, quirk, index);
        }
        Widgets.EndScrollView();
    }

    private static void DrawQuirkRow(Rect row, Pawn pawn, object quirk, int index)
    {
        if (index % 2 == 0)
            Widgets.DrawAltRect(row);
        Widgets.DrawHighlightIfMouseover(row);
        TooltipHandler.TipRegion(row, CharacterDevelopmentCompat.GetDescription(quirk));

        var removeRect = row.TakeRightPart(30f).ContractedBy(4f);
        if (Widgets.ButtonImage(removeRect, TexButton.Delete))
        {
            var label = CharacterDevelopmentCompat.GetLabel(quirk);
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "PawnEditor.Development.RemoveQuirkConfirm".Translate(label),
                () =>
                {
                    if (CharacterDevelopmentCompat.RemoveQuirk(pawn, quirk))
                        PawnEditor.Notify_PointsUsed();
                }));
        }
        TooltipHandler.TipRegion(removeRect, "PawnEditor.Development.RemoveQuirk".Translate());

        var iconRect = row.TakeLeftPart(44f).ContractedBy(6f);
        var icon = CharacterDevelopmentCompat.GetIcon(quirk) ?? Widgets.PlaceholderIconTex;
        Widgets.DrawTextureFitted(iconRect, icon, 1f);

        row = row.ContractedBy(3f, 5f);
        using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, false))
            Widgets.Label(row.TakeTopPart(24f), CharacterDevelopmentCompat.GetLabel(quirk).Truncate(row.width));
        using (new TextBlock(GameFont.Tiny, TextAnchor.UpperLeft, false))
        {
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(row, CharacterDevelopmentCompat.GetDescription(quirk).Truncate(row.width));
            GUI.color = Color.white;
        }
    }

    private static void OpenWantPicker(Pawn pawn)
    {
        var defs = CharacterDevelopmentCompat.GetAvailableWantDefs(pawn);
        if (defs.Count == 0)
        {
            Messages.Message("PawnEditor.Development.NoAvailableWants".Translate(), MessageTypeDefOf.RejectInput, false);
            return;
        }

        Find.WindowStack.Add(new ListingMenu<Def>(
            defs,
            CharacterDevelopmentCompat.PickerLabel,
            def => AddWant(pawn, def),
            "PawnEditor.Development.AddWant".Translate(),
            def => def.description,
            DrawDefIcon,
            pawn: pawn));
    }

    private static AddResult AddWant(Pawn pawn, Def def)
    {
        if (!CharacterDevelopmentCompat.AddWant(pawn, def))
            return "PawnEditor.Development.AddWantFailed".Translate();

        PawnEditor.Notify_PointsUsed();
        return true;
    }

    private static void OpenQuirkPicker(Pawn pawn)
    {
        var defs = CharacterDevelopmentCompat.GetAvailableQuirkDefs(pawn);
        if (defs.Count == 0)
        {
            var message = CharacterDevelopmentCompat.HasQuirkTargetMap(pawn)
                ? "PawnEditor.Development.NoAvailableQuirks".Translate()
                : "PawnEditor.Development.NoAvailableQuirksWithoutMap".Translate();
            Messages.Message(message, MessageTypeDefOf.RejectInput, false);
            return;
        }

        Find.WindowStack.Add(new ListingMenu<Def>(
            defs,
            CharacterDevelopmentCompat.PickerLabel,
            def => BeginAddQuirk(pawn, def),
            "PawnEditor.Development.AddQuirk".Translate(),
            def => def.description,
            DrawDefIcon,
            pawn: pawn));
    }

    private static AddResult BeginAddQuirk(Pawn pawn, Def def)
    {
        if (CharacterDevelopmentCompat.RequiresItem(def))
            return new SuccessInfo(() => OpenQuirkItemPicker(pawn, def));
        if (CharacterDevelopmentCompat.RequiresPawn(def))
            return new SuccessInfo(() => OpenQuirkPawnPicker(pawn, def, null));
        return AddQuirk(pawn, def, null, null);
    }

    private static void OpenQuirkItemPicker(Pawn pawn, Def def)
    {
        var items = CharacterDevelopmentCompat.GetValidItems(pawn, def);
        if (items.Count == 0)
        {
            Messages.Message("PawnEditor.Development.NoValidTargets".Translate(), MessageTypeDefOf.RejectInput, false);
            return;
        }

        Find.WindowStack.Add(new ListingMenu<ThingDef>(
            items,
            item => item.LabelCap,
            item => SelectQuirkItem(pawn, def, item),
            "PawnEditor.Development.ChooseItem".Translate(CharacterDevelopmentCompat.PickerLabel(def)),
            item => item.description,
            DrawThingIcon,
            pawn: pawn));
    }

    private static AddResult SelectQuirkItem(Pawn pawn, Def def, ThingDef item)
    {
        if (CharacterDevelopmentCompat.RequiresPawn(def))
            return new SuccessInfo(() => OpenQuirkPawnPicker(pawn, def, item));
        return AddQuirk(pawn, def, item, null);
    }

    private static void OpenQuirkPawnPicker(Pawn pawn, Def def, ThingDef item)
    {
        var pawns = CharacterDevelopmentCompat.GetValidPawns(pawn, def, item);
        if (pawns.Count == 0)
        {
            Messages.Message("PawnEditor.Development.NoValidTargets".Translate(), MessageTypeDefOf.RejectInput, false);
            return;
        }

        Find.WindowStack.Add(new ListingMenu_Pawns(
            pawns,
            pawn,
            "Add".Translate().CapitalizeFirst(),
            target => AddQuirk(pawn, def, item, target)));
    }

    private static AddResult AddQuirk(Pawn pawn, Def def, ThingDef item, Pawn target)
    {
        if (!CharacterDevelopmentCompat.AddQuirk(pawn, def, item, target))
            return "PawnEditor.Development.AddQuirkFailed".Translate();

        PawnEditor.Notify_PointsUsed();
        return true;
    }

    private static void DrawDefIcon(Def def, Rect rect)
    {
        var icon = CharacterDevelopmentCompat.GetDefIcon(def) ?? Widgets.PlaceholderIconTex;
        Widgets.DrawTextureFitted(rect, icon, 1f);
    }

    private static void DrawThingIcon(ThingDef def, Rect rect)
    {
        var icon = def?.uiIcon ?? Widgets.PlaceholderIconTex;
        Widgets.DrawTextureFitted(rect, icon, 1f);
    }

    private static void DrawSearchPlaceholder(Rect rect, string value)
    {
        if (!value.NullOrEmpty())
            return;

        GUI.color = ColoredText.SubtleGrayColor;
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(rect.ContractedBy(5f, 0f), "PawnEditor.Search".Translate() + "...");
        GUI.color = Color.white;
    }

    private static bool Matches(string filter, params string[] values)
    {
        if (filter.NullOrEmpty())
            return true;
        return values.Any(value => !value.NullOrEmpty()
                                   && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
