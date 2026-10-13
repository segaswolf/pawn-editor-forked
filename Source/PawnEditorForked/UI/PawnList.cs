using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PawnEditor;

// The scrollable, reorderable pawn list in the left panel: one card per pawn.
public static partial class PawnEditor
{
    private const float PawnRowHeight = 59f;
    private const float SectionHeaderHeight = 20f;
    private const float ReorderHintHeight = 20f;
    private const float RowContentPadding = 5f;
    private const float DeleteButtonSize = 16f;
    private const float PortraitAlpha = 0.2f;
    private static readonly Vector2 PortraitCenter = new(105f, 40f);

    private static Vector2 pawnListScrollPos;
    private static int reorderableGroupID;

    private static void DoPawnList(Rect inRect, List<Pawn> pawns, List<string> sections, Action<Pawn, int, int> onReorder)
    {
        var contentHeight = PawnListContentHeight(pawns, sections);
        if (pawns.Count > 0)
            DrawReorderHint(ref inRect, contentHeight);

        var viewRect = new Rect(0, 0, inRect.width, contentHeight);
        inRect.xMax += Layout.ScrollBarWidth;
        Widgets.BeginScrollView(inRect, ref pawnListScrollPos, viewRect);
        if (Event.current.type == EventType.Repaint)
            reorderableGroupID = NewPawnReorderGroup(pawns, onReorder, viewRect);

        for (var i = 0; i < pawns.Count; i++)
        {
            DrawSectionHeader(ref viewRect, sections[i]);
            DrawPawnRow(ref viewRect, pawns[i]);
        }

        Widgets.EndScrollView();
    }

    /// <summary>Rows plus one header per section, with room for one header more.</summary>
    private static float PawnListContentHeight(List<Pawn> pawns, List<string> sections) =>
        pawns.Count * PawnRowHeight + (sections.Count(s => s != null) + 1) * SectionHeaderHeight;

    /// <summary>"Drag to reorder": right under the rows when they fit, otherwise at the bottom.</summary>
    private static void DrawReorderHint(ref Rect inRect, float contentHeight)
    {
        var hint = inRect.TakeBottomPart(ReorderHintHeight);
        if (contentHeight <= inRect.height)
        {
            inRect.height = contentHeight + ReorderHintHeight;
            hint = inRect.TakeBottomPart(ReorderHintHeight);
            hint.y -= ReorderHintHeight;
        }

        using (new TextBlock(GameFont.Tiny))
            Widgets.Label(hint, "DragToReorder".Translate().Colorize(ColoredText.SubtleGrayColor));
    }

    private static int NewPawnReorderGroup(List<Pawn> pawns, Action<Pawn, int, int> onReorder, Rect viewRect) =>
        ReorderableWidget.NewGroup((from, to) =>
        {
            var item = pawns[from];
            pawns.Insert(to, item);
            pawns.RemoveAt(from < to ? from : from + 1);
            onReorder(item, from, to);
        }, ReorderableDirection.Vertical, viewRect, -1f, null, false);

    private static void DrawSectionHeader(ref Rect viewRect, string header)
    {
        if (header == null || !Layout.TryTakeTop(ref viewRect, SectionHeaderHeight, out var row)) return;

        using (new TextBlock(GameFont.Tiny))
            Widgets.Label(row, header);
    }

    private static void DrawPawnRow(ref Rect viewRect, Pawn pawn)
    {
        if (!Layout.TryTakeTop(ref viewRect, PawnRowHeight, out var row)) return;

        var card = row.ContractedBy(2, 3);
        Widgets.DrawOptionBackground(card, !showFactionInfo && selectedPawn == pawn);
        MouseoverSounds.DoRegion(card);

        Widgets.BeginGroup(card);
        try
        {
            var content = card.AtZero().ContractedBy(RowContentPadding);
            DrawRowPortrait(pawn);
            DrawRowLabels(content, pawn);
            HandleRowClick(content, pawn);
        }
        finally
        {
            Widgets.EndGroup();
        }

        if (ReorderableWidget.Reorderable(reorderableGroupID, card))
            Widgets.DrawRectFast(card.ContractedBy(3), Widgets.WindowBGFillColor * new Color(1f, 1f, 1f, 0.5f));
    }

    /// <summary>Faded portrait behind the text. Animals and mechs face east.</summary>
    private static void DrawRowPortrait(Pawn pawn)
    {
        var size = Page_ConfigureStartingPawns.PawnSelectorPortraitSize;
        var humans = selectedCategory == PawnCategory.Humans;
        var texture = GetPawnTex(pawn, size, humans ? Rot4.South : Rot4.East,
            humans ? default : new Vector3(-0.01f, 0, 0), 1 / Mathf.Clamp(pawn.BodySize, 1, 5));

        GUI.color = new Color(1f, 1f, 1f, PortraitAlpha);
        // Draws a placeholder while the portrait atlas reloads, instead of a null texture or a black gap.
        DrawPortraitOrPlaceholder(new Rect(PortraitCenter - size / 2f, size), texture);
        GUI.color = Color.white;
    }

    private static void DrawRowLabels(Rect rect, Pawn pawn)
    {
        using (new TextBlock(TextAnchor.MiddleLeft))
        {
            Widgets.Label(rect.TopPart(0.5f).Rounded(), RowName(pawn).Truncate(rect.width));
            Widgets.Label(rect.BottomPart(0.5f).Rounded(), RowSubtitle(pawn, rect.width));
        }
    }

    /// <summary>Nickname, else first name, else the short label (animals, mechs).</summary>
    private static string RowName(Pawn pawn)
    {
        if (pawn.Name is not NameTriple name) return pawn.LabelShort;
        return name.Nick.NullOrEmpty() ? name.First : name.Nick;
    }

    /// <summary>Title, shortened if it doesn't fit; the kind label for pawns without a story.</summary>
    private static string RowSubtitle(Pawn pawn, float width)
    {
        if (pawn.story == null) return pawn.KindLabel.CapitalizeFirst();
        return Text.CalcSize(pawn.story.TitleCap).x > width ? pawn.story.TitleShortCap : pawn.story.TitleCap;
    }

    private static void HandleRowClick(Rect rect, Pawn pawn)
    {
        if (!Mouse.IsOver(rect)) return;

        var deleteRect = rect.RightPartPixels(DeleteButtonSize).TopPartPixels(DeleteButtonSize);
        if (Widgets.ButtonImage(deleteRect, TexButton.CloseXSmall, Color.red))
            ConfirmDeletePawn(pawn);
        else if (Event.current.type == EventType.MouseDown)
            SelectPawnFromList(pawn);
    }

    private static void SelectPawnFromList(Pawn pawn)
    {
        selectedPawn = pawn;
        showFactionInfo = false;
        CheckChangeTabGroup();
    }
}
