using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[ModCompat("vanillaexpanded.vanillaaspirationsexpanded")]
public static class VAspirECompat
{
    public static bool Active;
    public static string Name = "Vanilla Aspirations Expanded";

    // Types
    private static Type needFulfillmentType;
    private static Type aspirationDefType;
    private static Type aspirationWorkerType;

    // Need_Fulfillment fields
    private static FieldInfo aspirationsField;
    private static FieldInfo completedTicksField;
    private static FieldInfo aspirationsForThisPawnField;

    // Need_Fulfillment methods
    private static MethodInfo completeMethod;
    private static MethodInfo isCompleteMethod;
    private static MethodInfo debugAddMethod;
    private static MethodInfo debugRemoveMethod;
    private static MethodInfo checkCompletionMethod;

    // AspirationDef fields/properties
    private static PropertyInfo iconProperty;
    private static PropertyInfo workerProperty;

    // AspirationWorker.ValidOn
    private static MethodInfo validOnMethod;
    public sealed class FulfillmentSnapshot
    {
        public int AspirationCount = 4;
        public List<string> AspirationDefNames { get; } = new();
        public HashSet<string> CompletedDefNames { get; } = new();

        public bool HasData => AspirationDefNames.Count > 0;
    }
    public static void Activate()
    {
        needFulfillmentType = AccessTools.TypeByName("VAspirE.Need_Fulfillment");
        aspirationDefType = AccessTools.TypeByName("VAspirE.AspirationDef");
        aspirationWorkerType = AccessTools.TypeByName("VAspirE.AspirationWorker");

        if (needFulfillmentType == null || aspirationDefType == null)
        {
            Log.Error("[Pawn Editor] VAspirE types not found. Disabling VAspirE compat.");
            Active = false;
            return;
        }

        // Need_Fulfillment
        aspirationsField = AccessTools.Field(needFulfillmentType, "Aspirations");
        completedTicksField = AccessTools.Field(needFulfillmentType, "completedTicks");
        aspirationsForThisPawnField = AccessTools.Field(needFulfillmentType, "aspirationsForThisPawn");
        completeMethod = AccessTools.Method(needFulfillmentType, "Complete");
        isCompleteMethod = AccessTools.Method(needFulfillmentType, "IsComplete");
        debugAddMethod = AccessTools.Method(needFulfillmentType, "DebugAddAspiration");
        debugRemoveMethod = AccessTools.Method(needFulfillmentType, "DebugRemoveAspiration");
        checkCompletionMethod = AccessTools.Method(needFulfillmentType, "CheckCompletion");

        // AspirationDef
        iconProperty = AccessTools.Property(aspirationDefType, "Icon");
        workerProperty = AccessTools.Property(aspirationDefType, "Worker");

        // AspirationWorker
        validOnMethod = AccessTools.Method(aspirationWorkerType, "ValidOn");
    }

    /// <summary>
    /// Gets the Need_Fulfillment from a pawn, or null if not found.
    /// </summary>
    public static Need GetFulfillmentNeed(Pawn pawn)
    {
        if (needFulfillmentType == null || pawn?.needs == null) return null;
        return pawn.needs.AllNeeds.FirstOrDefault(n => needFulfillmentType.IsInstanceOfType(n));
    }

    /// <summary>
    /// Returns true if this Need is VAspirE's Fulfillment need.
    /// </summary>
    public static bool IsFulfillmentNeed(Need need)
    {
        if (needFulfillmentType == null || need == null) return false;
        return needFulfillmentType.IsInstanceOfType(need);
    }

    /// <summary>
    /// Gets the list of AspirationDefs assigned to this pawn's fulfillment need.
    /// </summary>
    public static List<Def> GetAspirations(Need fulfillmentNeed)
    {
        if (aspirationsField == null) return new List<Def>();
        var raw = aspirationsField.GetValue(fulfillmentNeed);
        if (raw is System.Collections.IList list)
            return list.Cast<Def>().ToList();
        return new List<Def>();
    }

    /// <summary>
    /// Gets the completedTicks list (parallel to Aspirations).
    /// </summary>
    public static List<int> GetCompletedTicks(Need fulfillmentNeed)
    {
        if (completedTicksField == null) return new List<int>();
        return completedTicksField.GetValue(fulfillmentNeed) as List<int> ?? new List<int>();
    }

    /// <summary>
    /// Gets aspirationsForThisPawn (4 or 5).
    /// </summary>
    public static int GetAspirationCount(Need fulfillmentNeed)
    {
        if (aspirationsForThisPawnField == null) return 4;
        return (int)aspirationsForThisPawnField.GetValue(fulfillmentNeed);
    }

    /// <summary>
    /// Checks if a specific aspiration is completed.
    /// </summary>
    public static bool IsComplete(Need fulfillmentNeed, Def aspirationDef)
    {
        if (isCompleteMethod == null) return false;
        return (bool)isCompleteMethod.Invoke(fulfillmentNeed, new object[] { aspirationDef });
    }

    /// <summary>
    /// Marks an aspiration as complete. Uses silent mode in pre-colony to avoid
    /// growth moment letters and messages that shouldn't fire during pawn creation.
    /// In-game (post-colony), uses VAspirE's native Complete() which handles letters properly.
    /// </summary>
    public static void Complete(Need fulfillmentNeed, Def aspirationDef)
    {
        if (Current.ProgramState != ProgramState.Playing)
        {
            CompleteSilent(fulfillmentNeed, aspirationDef);
            return;
        }
        completeMethod?.Invoke(fulfillmentNeed, new object[] { aspirationDef });
    }

    /// <summary>
    /// Marks an aspiration as completed via reflection without triggering letters or messages.
    /// Used in pre-colony context where growth moment notifications are inappropriate.
    /// </summary>
    private static void CompleteSilent(Need fulfillmentNeed, Def aspirationDef)
    {
        var aspirations = GetAspirations(fulfillmentNeed);
        var idx = aspirations.IndexOf(aspirationDef);
        if (idx == -1) return;

        var ticks = completedTicksField?.GetValue(fulfillmentNeed) as List<int>;
        if (ticks == null || idx >= ticks.Count) return;
        if (ticks[idx] != -1) return; // Already completed

        ticks[idx] = GenTicks.TicksAbs;
        fulfillmentNeed.CurLevel += 1f;
    }

    

    /// <summary>
    /// Uncompletes an aspiration by resetting its completedTick to -1 and adjusting CurLevel.
    /// VAspirE has no public API for this, so we do it via reflection.
    /// </summary>
    public static void Uncomplete(Need fulfillmentNeed, Def aspirationDef)
    {
        var aspirations = GetAspirations(fulfillmentNeed);
        var idx = aspirations.IndexOf(aspirationDef);
        if (idx == -1) return;

        var ticks = completedTicksField?.GetValue(fulfillmentNeed) as List<int>;
        if (ticks == null || idx >= ticks.Count) return;
        if (ticks[idx] == -1) return; // Already not completed

        ticks[idx] = -1;
        fulfillmentNeed.CurLevel = Math.Max(0f, fulfillmentNeed.CurLevel - 1f);
    }

    /// <summary>
    /// Adds an aspiration to the pawn's list (no duplicates).
    /// </summary>
    public static void AddAspiration(Need fulfillmentNeed, Def aspirationDef)
    {
        debugAddMethod?.Invoke(fulfillmentNeed, new object[] { aspirationDef });
    }

    /// <summary>
    /// Removes an aspiration from the pawn's list.
    /// </summary>
    public static void RemoveAspiration(Need fulfillmentNeed, Def aspirationDef)
    {
        debugRemoveMethod?.Invoke(fulfillmentNeed, new object[] { aspirationDef });
    }

    /// <summary>
    /// Re-checks completion of all aspirations.
    /// </summary>
    public static void CheckCompletion(Need fulfillmentNeed)
    {
        checkCompletionMethod?.Invoke(fulfillmentNeed, new object[] { true });
    }

    /// <summary>
    /// Gets the icon texture for an AspirationDef.
    /// </summary>
    public static Texture2D GetIcon(Def aspirationDef)
    {
        if (iconProperty == null) return null;
        return iconProperty.GetValue(aspirationDef) as Texture2D;
    }

    /// <summary>
    /// Checks if an AspirationDef is valid for a given pawn.
    /// </summary>
    public static bool IsValidOn(Def aspirationDef, Pawn pawn)
    {
        if (workerProperty == null || validOnMethod == null) return true;
        var worker = workerProperty.GetValue(aspirationDef);
        if (worker == null) return true;
        try
        {
            return (bool)validOnMethod.Invoke(worker, new object[] { pawn });
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets all AspirationDefs from the def database.
    /// </summary>
    public static List<Def> GetAllAspirationDefs()
    {
        if (aspirationDefType == null) return new List<Def>();
        var allDefs = GenDefDatabase.GetAllDefsInDatabaseForDef(aspirationDefType);
        return allDefs.ToList();
    }

    /// <summary>
    /// Gets AspirationDefs valid for a pawn that are not already assigned.
    /// </summary>
    public static List<Def> GetAvailableAspirations(Pawn pawn, Need fulfillmentNeed)
    {
        return GetAllAspirationDefs()
            .Where(d => d != null)
            .OrderBy(d => d.LabelCap.RawText)
            .ToList();
    }

    /// <summary>
    /// Sets aspirationsForThisPawn count.
    /// </summary>
    public static void SetAspirationCount(Need fulfillmentNeed, int count)
    {
        aspirationsForThisPawnField?.SetValue(fulfillmentNeed, count);
    }

    /// <summary>
    /// Completes all aspirations at once.
    /// </summary>
    public static void CompleteAll(Need fulfillmentNeed)
    {
        var aspirations = GetAspirations(fulfillmentNeed);
        foreach (var asp in aspirations)
        {
            if (!IsComplete(fulfillmentNeed, asp))
                Complete(fulfillmentNeed, asp);
        }
    }

    /// <summary>
    /// Uncompletes all aspirations at once.
    /// </summary>
    public static void UncompleteAll(Need fulfillmentNeed)
    {
        var aspirations = GetAspirations(fulfillmentNeed);
        foreach (var asp in aspirations)
        {
            Uncomplete(fulfillmentNeed, asp);
        }
    }
    public static Def GetAspirationDefByName(string defName)
    {
        if (defName.NullOrEmpty()) return null;

        return GetAllAspirationDefs().FirstOrDefault(d => d.defName == defName);
    }

    public static bool TryRestoreSnapshot(Pawn pawn, FulfillmentSnapshot snapshot)
    {
        if (!Active || pawn == null || snapshot == null || !snapshot.HasData)
            return false;

        Need fulfillmentNeed = null;

        try
        {
            fulfillmentNeed = GetFulfillmentNeed(pawn);

            // If the pawn should have the need but it is missing, try to let RimWorld rebuild needs.
            if (fulfillmentNeed == null)
            {
                pawn.needs?.AddOrRemoveNeedsAsAppropriate();
                fulfillmentNeed = GetFulfillmentNeed(pawn);
            }

            if (fulfillmentNeed == null)
                return false;

            // Clear existing aspirations first to avoid RNG leftovers / duplicates
            foreach (var existing in GetAspirations(fulfillmentNeed).ToList())
                RemoveAspiration(fulfillmentNeed, existing);

            SetAspirationCount(fulfillmentNeed, snapshot.AspirationCount);

            foreach (var defName in snapshot.AspirationDefNames)
            {
                var def = GetAspirationDefByName(defName);
                if (def == null) continue;

                AddAspiration(fulfillmentNeed, def);
            }

            foreach (var defName in snapshot.CompletedDefNames)
            {
                var def = GetAspirationDefByName(defName);
                if (def == null) continue;

                if (GetAspirations(fulfillmentNeed).Contains(def))
                    Complete(fulfillmentNeed, def);
            }

            CheckCompletion(fulfillmentNeed);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to restore VAspirE aspirations for {pawn.LabelCap}: {ex.Message}");
            return false;
        }
    }
}
