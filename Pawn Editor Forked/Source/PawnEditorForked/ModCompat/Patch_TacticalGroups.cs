using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace PawnEditor;

/// <summary>
/// Fixes TacticalGroups (Colony Groups) crash when pawns are added, removed, or replaced.
/// 
/// Bug: TacticalGroups.TacticalColonistBar.GetNonHiddenPawns() uses Dictionary[pawnName]
/// which throws KeyNotFoundException for pawns not yet registered in its internal dictionary.
/// Then ColonistBarOnGUI crashes with ArgumentOutOfRangeException from the corrupted state.
///
/// Fix: Finalizers on the three crash methods that swallow the exception and return safe defaults.
/// TacticalGroups will naturally re-sync its dictionary on the next recache cycle.
/// </summary>
[StaticConstructorOnStartup]
public static class Patch_TacticalGroups
{
    private static bool patched = false;
    private static int suppressedErrors = 0;
    private const int MaxLoggedErrors = 5;

    static Patch_TacticalGroups()
    {
        TryPatch();
    }

    public static void TryPatch()
    {
        if (patched) return;

        var assembly = FindAssembly();
        if (assembly == null) return;

        try
        {
            var harmony = new Harmony("pawneditor.patch.tacticalgroups");
            var barType = assembly.GetType("TacticalGroups.TacticalColonistBar");
            if (barType == null) return;

            // GetNonHiddenPawns — the root crash (KeyNotFoundException on dict[pawnName])
            PatchMethod(harmony, barType, "GetNonHiddenPawns",
                nameof(Finalizer_GetNonHiddenPawns));

            // CheckRecacheEntries — secondary crash from corrupted state
            PatchMethod(harmony, barType, "CheckRecacheEntries",
                nameof(Finalizer_SwallowAndLog));

            // ColonistBarOnGUI — top-level catch-all
            PatchMethod(harmony, barType, "ColonistBarOnGUI",
                nameof(Finalizer_SwallowAndLog));

            // Also patch the HarmonyPatches prefix that calls ColonistBarOnGUI
            var hpType = assembly.GetType("TacticalGroups.HarmonyPatches");
            if (hpType != null)
            {
                var colonistBarPrefix = hpType.GetMethod("ColonistBarOnGUI",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (colonistBarPrefix != null)
                {
                    harmony.Patch(colonistBarPrefix,
                        finalizer: new HarmonyMethod(typeof(Patch_TacticalGroups),
                            nameof(Finalizer_SwallowAndLog)));
                    Log.Message("[Pawn Editor] TacticalGroups HarmonyPatches.ColonistBarOnGUI finalizer applied.");  // startup — once only, intentional
                }
            }

            patched = true;
            Log.Message("[Pawn Editor] TacticalGroups compatibility patches applied successfully.");
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to patch TacticalGroups: {ex.Message}");
        }
    }

    private static void PatchMethod(Harmony harmony, Type type, string methodName, string finalizerName)
    {
        var method = type.GetMethod(methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        if (method == null)
        {
            Log.Warning($"[Pawn Editor] TacticalGroups.{methodName} not found, skipping patch.");
            return;
        }

        harmony.Patch(method,
            finalizer: new HarmonyMethod(typeof(Patch_TacticalGroups), finalizerName));
        Log.Message($"[Pawn Editor] TacticalGroups.{methodName} finalizer applied.");
    }

    /// <summary>
    /// Finalizer for GetNonHiddenPawns:
    /// On KeyNotFoundException, returns the unfiltered pawn list (safe fallback).
    /// TacticalGroups will re-sync its dictionary on the next recache.
    /// </summary>
    public static Exception Finalizer_GetNonHiddenPawns(Exception __exception, List<Pawn> pawns, ref List<Pawn> __result)
    {
        if (__exception == null) return null;

        suppressedErrors++;
        if (suppressedErrors <= MaxLoggedErrors)
        {
            Log.Warning($"[Pawn Editor] TacticalGroups.GetNonHiddenPawns: {__exception.GetType().Name} suppressed — returning unfiltered list." +
                (suppressedErrors == MaxLoggedErrors ? " Further warnings suppressed." : ""));
        }

        // Return the original pawn list without TacticalGroups' filtering
        __result = pawns ?? new List<Pawn>();
        return null; // Swallow exception
    }

    /// <summary>
    /// Generic finalizer: swallows any exception and logs once.
    /// Used for CheckRecacheEntries and ColonistBarOnGUI.
    /// </summary>
    public static Exception Finalizer_SwallowAndLog(Exception __exception)
    {
        if (__exception == null) return null;

        suppressedErrors++;
        if (suppressedErrors <= MaxLoggedErrors)
        {
            Log.Warning($"[Pawn Editor] TacticalGroups error suppressed: {__exception.GetType().Name}: {__exception.Message}" +
                (suppressedErrors == MaxLoggedErrors ? " Further warnings suppressed." : ""));
        }

        return null; // Swallow exception
    }

    /// <summary>
    /// Call this after adding/removing/replacing pawns to reset the suppression counter.
    /// TacticalGroups should re-sync within a few frames.
    /// </summary>
    public static void ResetErrorCounter()
    {
        suppressedErrors = 0;
    }

    private static Assembly FindAssembly()
    {
        foreach (var mod in LoadedModManager.RunningMods)
        {
            var id = mod.PackageId?.ToLower();
            if (id == null) continue;
            if (!id.Contains("tacticalgroups") && !id.Contains("colonygroups")) continue;

            foreach (var asm in mod.assemblies.loadedAssemblies)
            {
                if (asm.GetType("TacticalGroups.TacticalColonistBar") != null)
                    return asm;
            }
        }
        return null;
    }
}
