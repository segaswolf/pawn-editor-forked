using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Compatibility layer for Life Lessons mod.
/// Provides access to the proficiency system: reading, adding, removing proficiencies,
/// and handling save/load and duplication of proficiency state.
/// All access is via reflection to avoid hard dependency on Life Lessons assembly.
///
/// Key Life Lessons types:
/// - ProficiencyComp (ThingComp on pawn) — stores all proficiency data
/// - ProficiencyDef — defines each proficiency
/// - ProficiencyRecord — instance of a proficiency with XP progress
/// - ProficiencyCategoryDef — groups proficiencies by category
/// </summary>
[ModCompat("GhostData.lifelessons")]
public static class LifeLessonsCompat
{
    public static bool Active;
    public static string Name = "Life Lessons";

    // ── Reflection: Types ──
    private static Type proficiencyCompType;
    private static Type proficiencyDefType;
    private static Type proficiencyRecordType;
    private static Type proficiencyCategoryDefType;

    // ── Reflection: ProficiencyComp methods/properties ──
    private static MethodInfo getCompMethod;
    private static PropertyInfo completedProficienciesProperty;
    private static PropertyInfo proficienciesProperty;
    private static PropertyInfo allLearnableProficienciesProperty;
    private static MethodInfo tryGainProficiencyMethod;
    private static MethodInfo initializeMethod;
    private static MethodInfo removeProficiencyMethod;
    private static MethodInfo canLearnMethod;
    private static MethodInfo refreshModifiersMethod;

    // ── Reflection: ProficiencyRecord fields ──
    private static FieldInfo recordDefField;
    private static FieldInfo recordCompleteField;
    private static PropertyInfo recordXpProperty;

    // ── Reflection: ProficiencyDef fields ──
    private static FieldInfo defCategoryField;

    /// <summary>
    /// Initializes all reflection fields by resolving Life Lessons types, methods, and fields.
    /// Called automatically when the mod is detected as active.
    /// </summary>
    public static void Activate()
    {
        try
        {
            // Find the assembly
            var assembly = LoadedModManager.RunningModsListForReading
                .SelectMany(m => m.assemblies.loadedAssemblies)
                .FirstOrDefault(a => a.GetName().Name == "LifeLessons");

            if (assembly == null)
            {
                Log.Warning("[Pawn Editor] Life Lessons assembly not found.");
                return;
            }

            // Resolve types
            proficiencyCompType = assembly.GetType("LifeLessons.ProficiencyComp");
            proficiencyDefType = assembly.GetType("LifeLessons.ProficiencyDef");
            proficiencyRecordType = assembly.GetType("LifeLessons.ProficiencyRecord");
            proficiencyCategoryDefType = assembly.GetType("LifeLessons.ProficiencyCategoryDef");

            if (proficiencyCompType == null || proficiencyDefType == null)
            {
                Log.Warning("[Pawn Editor] Life Lessons core types not found.");
                return;
            }

            // ProficiencyComp — methods and properties
            getCompMethod = typeof(ThingCompUtility)
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Where(m => m.Name == "TryGetComp" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
                .FirstOrDefault()
                ?.MakeGenericMethod(proficiencyCompType);

            completedProficienciesProperty = proficiencyCompType.GetProperty("CompletedProficiencies");
            proficienciesProperty = proficiencyCompType.GetProperty("Proficiencies");
            allLearnableProficienciesProperty = proficiencyCompType.GetProperty("AllLearnableProficiencies");
            tryGainProficiencyMethod = proficiencyCompType.GetMethod("TryGainProficiency",
                new[] { proficiencyDefType, typeof(bool) });
            // ProficiencyComp.Initialize has signature Initialize(bool forceReinit, bool overrideChecks).
            // We must pass both parameter types; ThingComp base also declares Initialize(CompProperties),
            // so specifying types avoids AmbiguousMatchException AND resolves the correct overload.
            initializeMethod = proficiencyCompType.GetMethod("Initialize", new[] { typeof(bool), typeof(bool) });

            // ProficiencyComp.RemoveProficiency(ProficiencyDef def, bool removeAncestors).
            removeProficiencyMethod = proficiencyCompType.GetMethod("RemoveProficiency",
                new[] { proficiencyDefType, typeof(bool) });

            // ProficiencyComp.CanLearn(ProficiencyDef def) — validates prerequisites and conditions.
            canLearnMethod = proficiencyCompType.GetMethod("CanLearn", new[] { proficiencyDefType });

            // ProficiencyComp.RefreshModifiers() — recalculates stat/skill modifiers after changes.
            refreshModifiersMethod = proficiencyCompType.GetMethod("RefreshModifiers", Type.EmptyTypes);

            // ProficiencyRecord — fields
            if (proficiencyRecordType != null)
            {
                recordDefField = proficiencyRecordType.GetField("def");
                var completeProp = proficiencyRecordType.GetProperty("Complete");
                if (completeProp != null)
                    recordCompleteField = null; // Use property via reflection when needed
                else
                    recordCompleteField = proficiencyRecordType.GetField("Complete");
                recordXpProperty = proficiencyRecordType.GetProperty("Xp");
            }

            // ProficiencyDef — category field
            defCategoryField = proficiencyDefType.GetField("category");

            Active = true;
            Log.Message("[Pawn Editor] Life Lessons compatibility active.");
        }
        catch (Exception ex)
        {
            Log.Error($"[Pawn Editor] Failed to initialize Life Lessons compatibility: {ex}");
        }
    }

    /// <summary>
    /// Gets the ProficiencyComp from a pawn (via reflection).
    /// Returns null if the pawn doesn't have the comp or Life Lessons is not active.
    /// </summary>
    public static object GetProficiencyComp(Pawn pawn)
    {
        if (!Active || getCompMethod == null) return null;
        try
        {
            return getCompMethod.Invoke(null, new object[] { pawn });
        }
        catch { return null; }
    }

    /// <summary>
    /// Gets the list of completed ProficiencyDefs for a pawn.
    /// </summary>
    public static List<Def> GetCompletedProficiencies(Pawn pawn)
    {
        var comp = GetProficiencyComp(pawn);
        if (comp == null || completedProficienciesProperty == null) return new List<Def>();

        try
        {
            var result = completedProficienciesProperty.GetValue(comp);
            if (result is System.Collections.IList list)
                return ExtractDefs(list);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to get completed proficiencies: {ex.Message}");
        }
        return new List<Def>();
    }

    /// <summary>
    /// Extracts ProficiencyDefs from a list that may contain either ProficiencyDef
    /// or ProficiencyRecord instances. Filters out null entries defensively so callers
    /// can safely access def members without null checks.
    /// </summary>
    private static List<Def> ExtractDefs(System.Collections.IList list)
    {
        var defs = new List<Def>();
        foreach (var item in list)
        {
            if (item == null) continue;

            // Direct ProficiencyDef instance.
            if (item is Def def)
            {
                defs.Add(def);
                continue;
            }

            // ProficiencyRecord wrapping a def in its 'def' field.
            if (recordDefField != null && proficiencyRecordType != null
                && proficiencyRecordType.IsInstanceOfType(item)
                && recordDefField.GetValue(item) is Def recordDef)
                defs.Add(recordDef);
        }
        return defs;
    }

    /// <summary>
    /// Gets all learnable ProficiencyDefs for a pawn (proficiencies not yet completed).
    /// </summary>
    public static List<Def> GetLearnableProficiencies(Pawn pawn)
    {
        var comp = GetProficiencyComp(pawn);
        if (comp == null || allLearnableProficienciesProperty == null) return new List<Def>();

        try
        {
            var result = allLearnableProficienciesProperty.GetValue(comp);
            if (result is System.Collections.IList list)
                return ExtractDefs(list);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to get learnable proficiencies: {ex.Message}");
        }
        return new List<Def>();
    }

    /// <summary>
    /// Grants a proficiency to a pawn (force = true bypasses prerequisites).
    /// </summary>
    public static bool TryGainProficiency(Pawn pawn, Def proficiencyDef, bool force = true)
    {
        var comp = GetProficiencyComp(pawn);
        if (comp == null || tryGainProficiencyMethod == null) return false;

        try
        {
            var result = tryGainProficiencyMethod.Invoke(comp, new object[] { proficiencyDef, force });
            return result is bool b && b;
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to gain proficiency: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes a proficiency from a pawn. When removeAncestors is true, also removes all
    /// ancestor proficiencies (those this one builds upon).
    /// </summary>
    public static bool RemoveProficiency(Pawn pawn, Def proficiencyDef, bool removeAncestors = false)
    {
        var comp = GetProficiencyComp(pawn);
        if (comp == null || removeProficiencyMethod == null) return false;

        try
        {
            removeProficiencyMethod.Invoke(comp, new object[] { proficiencyDef, removeAncestors });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to remove proficiency: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns true if the pawn can currently learn the given proficiency
    /// (prerequisites met, not already known, learning conditions satisfied).
    /// </summary>
    public static bool CanLearn(Pawn pawn, Def proficiencyDef)
    {
        var comp = GetProficiencyComp(pawn);
        if (comp == null || canLearnMethod == null) return false;

        try
        {
            var result = canLearnMethod.Invoke(comp, new object[] { proficiencyDef });
            return result is bool b && b;
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to check CanLearn: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Recalculates the pawn's stat and skill modifiers derived from proficiencies.
    /// Call after adding or removing proficiencies to keep modifiers in sync.
    /// </summary>
    public static void RefreshModifiers(Pawn pawn)
    {
        var comp = GetProficiencyComp(pawn);
        if (comp == null || refreshModifiersMethod == null) return;

        try
        {
            refreshModifiersMethod.Invoke(comp, null);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to refresh modifiers: {ex.Message}");
        }
    }

    /// <summary>
    /// Reinitializes the ProficiencyComp after changes (recalculates modifiers, etc.).
    /// </summary>
    public static void ReinitializeComp(Pawn pawn)
    {
        var comp = GetProficiencyComp(pawn);
        if (comp == null || initializeMethod == null) return;

        try
        {
            // Initialize(forceReinit: true, overrideChecks: false)
            initializeMethod.Invoke(comp, new object[] { true, false });
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to reinitialize proficiency comp: {ex.Message}");
        }
    }

    /// <summary>
    /// Snapshot of a pawn's proficiency state for save/load and duplication.
    /// </summary>
    public class ProficiencySnapshot
    {
        public List<string> completedDefNames = new();
    }

    /// <summary>
    /// Creates a snapshot of a pawn's completed proficiencies for serialization.
    /// </summary>
    public static ProficiencySnapshot CreateSnapshot(Pawn pawn)
    {
        var snapshot = new ProficiencySnapshot();
        var completed = GetCompletedProficiencies(pawn);
        snapshot.completedDefNames = completed.Select(d => d.defName).ToList();
        return snapshot;
    }

    /// <summary>
    /// Restores a pawn's proficiencies from a snapshot.
    /// Grants all completed proficiencies that the pawn doesn't already have.
    /// </summary>
    public static void RestoreSnapshot(Pawn pawn, ProficiencySnapshot snapshot)
    {
        if (snapshot?.completedDefNames == null) return;

        // Get all ProficiencyDefs from the database
        var allDefs = GetAllProficiencyDefs();

        foreach (var defName in snapshot.completedDefNames)
        {
            var def = allDefs.FirstOrDefault(d => d.defName == defName);
            if (def != null)
                TryGainProficiency(pawn, def, force: true);
        }

        ReinitializeComp(pawn);
    }

    /// <summary>
    /// Gets all ProficiencyDefs from the database via reflection.
    /// </summary>
    public static List<Def> GetAllProficiencyDefs()
    {
        if (!Active || proficiencyDefType == null) return new List<Def>();

        try
        {
            var dbType = typeof(DefDatabase<>).MakeGenericType(proficiencyDefType);
            var allDefsProperty = dbType.GetProperty("AllDefsListForReading");
            var result = allDefsProperty?.GetValue(null);
            if (result is System.Collections.IList list)
                return list.Cast<Def>().ToList();
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to get all proficiency defs: {ex.Message}");
        }
        return new List<Def>();
    }

    /// <summary>
    /// Gets the category name of a ProficiencyDef (for grouping in the UI).
    /// </summary>
    public static string GetCategory(Def proficiencyDef)
    {
        if (defCategoryField == null) return "Unknown";

        try
        {
            var category = defCategoryField.GetValue(proficiencyDef);
            if (category is Def catDef)
                return catDef.label ?? catDef.defName;
        }
        catch { }
        return "Unknown";
    }
}
