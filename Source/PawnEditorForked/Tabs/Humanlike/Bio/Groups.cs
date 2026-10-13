using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// The Groups section of the Bio tab: faction, ideology, certainty, role, and Trauma and Integrity.
public partial class TabWorker_Bio_Humanlike
{
    private const float GroupRowHeight = 30f;
    private const float GroupRowGap = 4f;
    private const float GroupSectionGap = 16f;
    private const float IdeoCardGap = 10f;
    private const float GroupIconSize = 30f;
    private const float SliderHeight = 10f;

    private void DoGroups(Rect inRect, Pawn pawn)
    {
        if (Layout.TryTakeTop(ref inRect, Text.LineHeight, out var title))
            Widgets.Label(title, "PawnEditor.Groups".Translate().Colorize(ColoredText.TipSectionTitleColor));
        inRect.xMin += 6;
        inRect.yMin += 4f;

        var leftWidth = UIUtility.ColumnWidth(3, "Faction".Translate(), "DifficultyIdeologySection".Translate(),
            "Certainty".Translate().CapitalizeFirst(), "Role".Translate().CapitalizeFirst()) + 4f;

        DrawFactionPicker(ref inRect, pawn, leftWidth);
        DrawFactionCard(ref inRect, pawn, leftWidth);
        inRect.yMin += GroupSectionGap;

        if (ModsConfig.IdeologyActive && pawn.Ideo != null)
        {
            DrawIdeoPicker(ref inRect, pawn, leftWidth);
            DrawIdeoCard(ref inRect, pawn, leftWidth);
            DrawCertaintySlider(ref inRect, pawn, leftWidth);
            DrawRoleButton(ref inRect, pawn, leftWidth);
            inRect.yMin += GroupSectionGap;
        }

        if (TraumaIntegrityCompat.Available)
            DrawTraumaIntegrityControls(ref inRect, pawn);
    }

    /// <summary>Takes a row, writes <paramref name="label"/> in its left column, and returns the rest.</summary>
    private static bool TryTakeLabeledRow(ref Rect rect, float leftWidth, string label, out Rect field)
    {
        if (!Layout.TryTakeTop(ref rect, GroupRowHeight, out field)) return false;

        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(field.TakeLeftPart(leftWidth), label);
        return true;
    }

    // ── Faction ──

    private static void DrawFactionPicker(ref Rect rect, Pawn pawn, float leftWidth)
    {
        if (!TryTakeLabeledRow(ref rect, leftWidth, "Faction".Translate(), out var field)) return;
        rect.yMin += GroupRowGap;

        if (Widgets.ButtonText(field, "PawnEditor.SelectFaction".Translate()))
            Find.WindowStack.Add(new FloatMenu(FactionOptions(pawn)));
    }

    /// <summary>Every faction with a humanlike, the random pickers, and "No faction".</summary>
    private static List<FloatMenuOption> FactionOptions(Pawn pawn)
    {
        var manager = Find.FactionManager;
        var options = PawnEditor_PawnsFinder.GetAllFactionsContainingAtLeastOneHumanLike()
            .Select(faction => new FloatMenuOption(faction.Name, () => AssignFaction(pawn, faction), faction.def.FactionIcon, faction.Color))
            .ToList();

        options.Add(RandomFactionOption("PawnEditor.RandomAlly".Translate(), pawn,
            () => manager.RandomAlliedFaction(), manager.RandomAlliedFaction() == null));
        options.Add(RandomFactionOption("PawnEditor.RandomEnemy".Translate(), pawn,
            () => manager.RandomEnemyFaction(), manager.RandomEnemyFaction() == null));
        options.Add(RandomFactionOption("PawnEditor.RandomNonHostile".Translate(), pawn,
            () => manager.RandomNonHostileFaction(), manager.RandomNonHostileFaction() == null));
        options.Add(RandomFactionOption("PawnEditor.RandomNonColonist".Translate(), pawn,
            () => manager.AllFactionsVisibleInViewOrder.RandomElement(),
            manager.RandomEnemyFaction() == null || manager.RandomNonHostileFaction() == null));
        options.Add(RandomFactionOption("PawnEditor.RandomFaction".Translate(), pawn,
            AnyFactionIncludingColony, manager.AllFactionsVisibleInViewOrder.Count() <= 1));
        options.Add(new FloatMenuOption("PawnEditor.NoFaction".Translate(), () => AssignFaction(pawn, null)));
        return options;
    }

    private static FloatMenuOption RandomFactionOption(string label, Pawn pawn, Func<Faction> pick, bool disabled) =>
        new(label, () => AssignFaction(pawn, pick())) { Disabled = disabled };

    // RandomElement covers the whole list. The old Random.Range(0, Count - 1) is max-exclusive, so the
    // last faction could never come up (same bug as the one fixed in Randomization and the bottom buttons).
    private static Faction AnyFactionIncludingColony()
    {
        var factions = Find.FactionManager.AllFactionsVisibleInViewOrder.ToList();
        factions.Add(Find.FactionManager.ofPlayer);
        return factions.RandomElement();
    }

    private static void AssignFaction(Pawn pawn, Faction faction)
    {
        if (faction != pawn.Faction)
            pawn.SetFaction(faction);
        PawnEditor.selectedFaction = faction;
        PawnEditor.DoRecache();
    }

    private static void DrawFactionCard(ref Rect rect, Pawn pawn, float leftWidth)
    {
        if (!Layout.TryTakeTop(ref rect, GroupRowHeight, out var row)) return;
        rect.yMin += GroupRowGap;
        row.xMin += leftWidth;

        if (pawn.Faction == null)
            Widgets.Label(row, "PawnEditor.NoFaction".Translate());
        else if (IsFactionHidden(pawn))
            Widgets.Label(row, "PawnEditor.FactionHidden".Translate());
        else
            DrawFactionLabel(row, pawn);
    }

    /// <summary>Before the game starts, the "hide factions" setting hides every faction but the player's.</summary>
    private static bool IsFactionHidden(Pawn pawn) =>
        PawnEditor.Pregame && PawnEditorMod.Settings.HideFactions && pawn.Faction != Find.FactionManager.ofPlayer;

    private static void DrawFactionLabel(Rect rect, Pawn pawn)
    {
        Widgets.DrawHighlight(rect);
        Widgets.DrawHighlightIfMouseover(rect);
        if (Widgets.ButtonInvisible(rect))
            Find.WindowStack.Add(new Dialog_InfoCard(pawn.Faction));

        GUI.color = pawn.Faction.Color;
        GUI.DrawTexture(rect.TakeLeftPart(GroupIconSize).ContractedBy(6), pawn.Faction.def.FactionIcon);
        GUI.color = Color.white;
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(rect, pawn.Faction.Name);
    }

    // ── Ideology ──

    private static void DrawIdeoPicker(ref Rect rect, Pawn pawn, float leftWidth)
    {
        if (!TryTakeLabeledRow(ref rect, leftWidth, "DifficultyIdeologySection".Translate(), out var field)) return;
        rect.yMin += GroupRowGap;

        if (Widgets.ButtonText(field, "PawnEditor.SelectIdeo".Translate()))
            Find.WindowStack.Add(new FloatMenu(Find.IdeoManager.IdeosInViewOrder
                .Select(ideo => new FloatMenuOption(ideo.name, () => pawn.ideo.SetIdeo(ideo), ideo.Icon, ideo.Color))
                .ToList()));
    }

    private void DrawIdeoCard(ref Rect rect, Pawn pawn, float leftWidth)
    {
        if (!Layout.TryTakeTop(ref rect, GroupRowHeight, out var row)) return;
        rect.yMin += IdeoCardGap;
        row.xMin += leftWidth;

        Widgets.DrawHighlight(row);
        Widgets.DrawHighlightIfMouseover(row);
        if (Widgets.ButtonInvisible(row))
            IdeoUIUtility.OpenIdeoInfo(pawn.Ideo);

        GUI.color = pawn.Ideo.Color;
        GUI.DrawTexture(row.TakeLeftPart(GroupIconSize).ContractedBy(6), pawn.Ideo.Icon);
        GUI.color = Color.white;
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(row, pawn.Ideo.name.Truncate(row.width, truncateCache));
    }

    private static void DrawCertaintySlider(ref Rect rect, Pawn pawn, float leftWidth)
    {
        if (!TryTakeLabeledRow(ref rect, leftWidth, "Certainty".Translate().CapitalizeFirst(), out var field)) return;

        field.yMax -= Mathf.Round((field.height - SliderHeight) / 2f);
        pawn.ideo.certaintyInt = Widgets.HorizontalSlider(field, pawn.ideo.certaintyInt, 0, 1, true,
            pawn.ideo.Certainty.ToStringPercent(), "0%", "100%");
    }

    private static void DrawRoleButton(ref Rect rect, Pawn pawn, float leftWidth)
    {
        if (!TryTakeLabeledRow(ref rect, leftWidth, "Role".Translate().CapitalizeFirst(), out var field)) return;
        rect.yMin += GroupRowGap;

        var current = pawn.Ideo.GetRole(pawn);
        if (Widgets.ButtonText(field, current?.LabelForPawn(pawn) ?? "None".Translate()))
            Find.WindowStack.Add(new FloatMenu(RoleOptions(pawn, current)));
    }

    private static List<FloatMenuOption> RoleOptions(Pawn pawn, Precept_Role current)
    {
        // ToDo: removing a role sometimes switches it to a different one instead of removing it.
        var options = new List<FloatMenuOption> { new("None".Translate(), () => current?.Unassign(pawn, false)) };
        options.AddRange(pawn.Ideo.cachedPossibleRoles.Select(role =>
            new FloatMenuOption(role.LabelForPawn(pawn), () => SwitchRole(pawn, current, role))));
        return options;
    }

    private static void SwitchRole(Pawn pawn, Precept_Role current, Precept_Role role)
    {
        if (current == role) return;
        current?.Unassign(pawn, false);
        role.Assign(pawn, true);
    }
}
