using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Point limit system for Pawn Editor.
/// Tracks remaining points/silver as pawns and items are added, removed, or modified.
/// In pregame: uses a fixed point budget from settings.
/// In-game with silver mode: uses colony silver as currency.
/// In-game without silver: tracks the delta from the baseline colony value.
/// </summary>
public partial class PawnEditor
{
    private static readonly HashSet<int> pawnValueWarningIds = new();

    /// <summary>Resets remaining points to the configured limit or colony silver amount.</summary>
    public static void ResetPoints()
    {
        remainingPoints = PawnEditorMod.Settings.PointLimit;
        cachedValue = 0;
        if (!Pregame && PawnEditorMod.Settings.UseSilver)
        {
            startingSilver = ColonyInventory.AllItemsInInventory().Sum(static t => t.def == ThingDefOf.Silver ? t.stackCount : 0);
            remainingPoints = startingSilver;
        }
        else if (!Pregame)
        {
            // In-game without silver: pre-calculate the current colony value as baseline
            // so that remainingPoints reflects only CHANGES made in the editor,
            // not the entire existing colony value subtracted from the budget.
            try
            {
                AllPawns.UpdateCache(PawnEditorMod.Settings.CountNPCs ? null : Faction.OfPlayer, PawnCategory.All);
                cachedValue = ValueOfPawns(AllPawns.GetList()) + ValueOfThings(ColonyInventory.AllItemsInInventory());
            }
            catch (Exception ex)
            {
                Log.Warning($"[Pawn Editor] ResetPoints: failed to pre-calculate colony value: {ex.Message}");
                cachedValue = 0;
            }
        }

        Notify_PointsUsed();
    }

    /// <summary>
    /// Applies point changes as silver in the colony. Positive delta = spawn silver, negative = remove silver.
    /// Only used in-game with silver mode enabled.
    /// </summary>
    public static void ApplyPoints()
    {
        var amount = remainingPoints - startingSilver;
        if (amount > 0)
        {
            var pos = ColonyInventory.AllItemsInInventory().FirstOrDefault(static t => t.def == ThingDefOf.Silver).Position;
            var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = Mathf.RoundToInt(amount);
            GenPlace.TryPlaceThing(silver, pos, Find.CurrentMap, ThingPlaceMode.Near);
        }
        else if (amount < 0)
        {
            amount = -amount;
            foreach (var thing in ColonyInventory.AllItemsInInventory().Where(static t => t.def == ThingDefOf.Silver))
            {
                var toRemove = Math.Min(thing.stackCount, Mathf.RoundToInt(amount));
                thing.stackCount -= toRemove;
                amount -= toRemove;

                if (thing.stackCount <= 0) thing.Destroy();
                if (amount < 1f) break;
            }
        }
    }

    /// <summary>
    /// Checks whether the player has enough remaining points for the given cost.
    /// Returns an AddResult with the error message if insufficient.
    /// </summary>
    public static AddResult CanUsePoints(float amount)
    {
        if (!usePointLimit) return true;
        if (remainingPoints >= amount) return true;
        return "PawnEditor.NotEnoughPoints".Translate(amount.ToStringMoney(), remainingPoints.ToStringMoney());
    }

    public static AddResult CanUsePoints(Thing thing) => CanUsePoints(GetThingValue(thing));
    public static AddResult CanUsePoints(Pawn pawn) => CanUsePoints(GetPawnValue(pawn));

    /// <summary>
    /// Recalculates remaining points by comparing the current total value
    /// against the cached baseline. Called after any editor change.
    /// If amount is provided, directly subtracts it instead of recalculating.
    /// </summary>
    public static void Notify_PointsUsed(float? amount = null)
    {
        if (amount.HasValue)
            remainingPoints -= amount.Value;
        else
        {
            try
            {
                var value = 0f;
                if (Pregame)
                {
                    value += ValueOfPawns(Find.GameInitData.startingAndOptionalPawns);
                    value += ValueOfPawns(StartingThingsManager.GetPawns(PawnCategory.Animals));
                    value += ValueOfPawns(StartingThingsManager.GetPawns(PawnCategory.Mechs));
                    value += ValueOfThings(StartingThingsManager.GetStartingThingsNear());
                    value += ValueOfThings(StartingThingsManager.GetStartingThingsFar());
                }
                else
                {
                    AllPawns.UpdateCache(PawnEditorMod.Settings.CountNPCs ? null : Faction.OfPlayer, PawnCategory.All);
                    value += ValueOfPawns(AllPawns.GetList());
                    value += ValueOfThings(ColonyInventory.AllItemsInInventory());
                }

                remainingPoints -= value - cachedValue;
                cachedValue = value;
            }
            catch (Exception ex)
            {
                Log.Error($"[Pawn Editor] Failed to recalculate points safely. Keeping previous points values. {ex}");
            }
        }
    }

    /// <summary>Sums the market value of all pawns in a collection.</summary>
    private static float ValueOfPawns(IEnumerable<Pawn> pawns)
    {
        if (pawns == null) return 0f;
        var total = 0f;
        foreach (var pawn in pawns) total += GetPawnValue(pawn);
        return total;
    }

    /// <summary>Sums the market value × stack count of all things in a collection.</summary>
    private static float ValueOfThings(IEnumerable<Thing> things)
    {
        if (things == null) return 0f;
        var total = 0f;
        foreach (var thing in things) total += GetThingValue(thing);
        return total;
    }

    /// <summary>Returns the total market value of a thing (price × stack count).</summary>
    private static float GetThingValue(Thing thing)
    {
        try
        {
            return thing?.MarketValue * thing?.stackCount ?? 0f;
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>Returns the total value of a pawn: market value + worn apparel + equipped weapons.</summary>
    private static float GetPawnValue(Pawn pawn)
    {
        if (pawn == null) return 0f;

        var num = 0f;
        try
        {
            num += pawn.MarketValue;
        }
        catch (Exception ex)
        {
            WarnPawnValueError(pawn, "MarketValue", ex);
        }

        if (pawn.apparel != null)
        {
            try
            {
                foreach (var apparel in pawn.apparel.WornApparel)
                    num += apparel?.MarketValue ?? 0f;
            }
            catch (Exception ex)
            {
                WarnPawnValueError(pawn, "Apparel", ex);
            }
        }

        if (pawn.equipment != null)
        {
            try
            {
                foreach (var eq in pawn.equipment.AllEquipmentListForReading)
                    num += eq?.MarketValue ?? 0f;
            }
            catch (Exception ex)
            {
                WarnPawnValueError(pawn, "Equipment", ex);
            }
        }

        return num;
    }

    /// <summary>Logs a warning once per pawn when value calculation fails for a component.</summary>
    private static void WarnPawnValueError(Pawn pawn, string component, Exception ex)
    {
        var id = pawn?.thingIDNumber ?? -1;
        if (!pawnValueWarningIds.Add(id)) return;
        Log.Warning($"[Pawn Editor] Failed to evaluate pawn value component '{component}' for {pawn?.LabelCap ?? "<null>"} ({pawn?.ThingID ?? "no-id"}). " +
                    $"Points were recalculated with partial data. {ex.GetType().Name}: {ex.Message}");
    }
}
