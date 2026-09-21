using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace PawnEditor;

/// <summary>
/// Emits one startup line summarising which mod-compatibility layers hooked in and which did not.
///
/// WHY THIS EXISTS
/// Our compat layers reach into other mods by reflection, looking up types and fields by name. When a
/// supported mod renames something in an update, the lookup simply returns null and the feature goes
/// quiet: no crash, no error, just a button that stopped appearing. We then find out weeks later from
/// a player report, which is how the Facial Animation and Gradient Hair issues reached us.
///
/// The case worth shouting about is narrow and specific: **the mod is installed, but we could not
/// resolve its API**. That is always our problem to fix, and it is invisible today. This turns it into
/// a line in the log on the very first launch after the other mod updates.
///
/// Deliberately ONE line, at Message level when everything resolved and Warning when it did not. A
/// diagnostic that spams is a diagnostic people filter out.
/// </summary>
public static class ModCompatReport
{
    private const string Prefix = "[Pawn Editor] Compat: ";

    /// <summary>Name and type of every compat layer whose mod was detected and that was activated.</summary>
    public static void Emit(List<(string Name, Type Type)> activated)
    {
        if (activated == null || activated.Count == 0) return;

        var resolved = new List<string>();
        var unresolved = new List<string>();

        foreach (var (name, type) in activated)
            (IsResolved(type) ? resolved : unresolved).Add(name);

        var message = Prefix + Describe("active", resolved);
        if (unresolved.Count == 0)
        {
            Log.Message(message);
            return;
        }

        Log.Warning(message + " | " + Describe("installed but API not resolved", unresolved)
                    + ". Those mods are present, so this is a Pawn Editor problem: their API most "
                    + "likely changed and our compatibility layer needs updating.");
    }

    private static string Describe(string label, List<string> names) =>
        names.Count == 0 ? $"{label} (0)" : $"{label} ({names.Count}): {string.Join(", ", names)}";

    /// <summary>
    /// A compat layer that exposes an <c>Available</c> flag uses it to report whether it actually
    /// resolved the other mod's API, as opposed to merely having detected the mod. Layers without one
    /// have nothing to fail at, so activation is enough.
    /// </summary>
    private static bool IsResolved(Type type)
    {
        try
        {
            var available = AccessTools.PropertyGetter(type, "Available");
            if (available == null) return true;
            return available.Invoke(null, Array.Empty<object>()) is true;
        }
        catch (Exception ex)
        {
            // Reading the flag must never be what breaks startup.
            Log.Warning($"[Pawn Editor] Could not read compatibility state for {type.Name}: {ex.Message}");
            return false;
        }
    }
}
