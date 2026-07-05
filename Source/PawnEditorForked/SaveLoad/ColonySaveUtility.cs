using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// v3.1 — Colony save (CAPA 1, save side only).
///
/// Saves every player-faction pawn (colonists, slaves, animals, mechs, across all
/// maps and caravans) as an individual portable blueprint, into a per-colony folder:
///
///     Colony/&lt;faction&gt;/&lt;settlement&gt;/
///
/// Reuses <see cref="PawnBlueprintSaveLoad.SaveBlueprint"/> once per pawn — that path is
/// already portable and tolerant of missing mods. Deliberately does NOT save the map,
/// buildings or research: only the pawns, like a "Prepare Carefully" preset. The rest is
/// left for the user to rebuild. Loading a whole colony back is CAPA 2.
/// </summary>
public static class ColonySaveUtility
{
    private const string RootFolder = "Colony";

    // ─────────────────────────────────────────────────────────────────────────
    //  Public entry point
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gather the colony's pawns and write one blueprint each into the colony folder,
    /// then report how many were saved. No-op (with a message) when there are no
    /// player pawns to save.
    /// </summary>
    public static void SaveColony()
    {
        var pawns = GetColonyPawns();
        if (pawns.Count == 0)
        {
            Messages.Message("Pawn Editor: No colony pawns found to save.", MessageTypeDefOf.RejectInput, false);
            return;
        }

        var folderType = BuildColonyFolderType();

        // Run the loop inside a SYNCHRONOUS long event: a full colony can be 30+ pawns, each
        // ~30 ms of XML write plus a portrait render. Without a long event the UI freezes with
        // no feedback; with one the user sees a "Saving..." bar. doAsynchronously:false keeps the
        // work on the main thread, which the portrait render (SavePawnTex) requires.
        LongEventHandler.QueueLongEvent(
            () => PawnEditorProfiler.Measure("SaveLoad.SaveColony", PawnEditorProfiler.Cadence.PerAction,
                () => SaveColonyPawns(pawns, folderType)),
            "PawnEditor.SavingColony", doAsynchronously: false, null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Pawn discovery
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// All living player-faction pawns, deduplicated: colonists, slaves, animals and mechs,
    /// across every map and the world (caravans). Anything that is a pawn and belongs to the
    /// player counts; the scenario, map and research are intentionally out of scope.
    /// </summary>
    private static List<Pawn> GetColonyPawns()
    {
        try
        {
            return PawnBlueprintSaveLoad.GetAllReachablePawnsPublic()
                .Where(p => p != null && !p.Dead && p.Faction != null && p.Faction.IsPlayer)
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] GetColonyPawns failed: {ex.Message}");
            return new List<Pawn>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Save loop
    // ─────────────────────────────────────────────────────────────────────────

    private static void SaveColonyPawns(List<Pawn> pawns, string folderType)
    {
        try
        {
            var dir = SaveLoadUtility.SaveFolderForItemType(folderType);

            // Re-saving a colony should produce a fresh snapshot, not accumulate stale files
            // from a previous, larger save. Clear this leaf folder's blueprints first.
            ClearColonyFolder(dir);

            var usedNames = new HashSet<string>();
            var saved = 0;

            foreach (var pawn in pawns)
            {
                try
                {
                    var name = UniqueFileName(usedNames, Sanitize(pawn.LabelShort, "Pawn"));
                    var path = Path.Combine(dir.FullName, name + ".xml");
                    PawnBlueprintSaveLoad.SaveBlueprint(pawn, path);
                    saved++;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Pawn Editor] Failed to save colony pawn '{pawn?.LabelShortCap}': {ex.Message}");
                }
            }

            Messages.Message(
                $"Pawn Editor: Saved {saved} colony pawn(s) to {folderType}.",
                MessageTypeDefOf.TaskCompletion, false);
        }
        catch (Exception ex)
        {
            Log.Error($"[Pawn Editor] Colony save failed: {ex}");
            Messages.Message("Pawn Editor: Failed to save colony. Check log.", MessageTypeDefOf.RejectInput, false);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Folder path (relative to the Pawn Editor save root) for this colony:
    /// Colony/&lt;faction&gt;/&lt;settlement&gt;, each segment sanitized for the filesystem.
    /// SaveFolderForItemType creates the whole nested chain on demand.
    /// </summary>
    private static string BuildColonyFolderType()
    {
        var faction = Sanitize(Faction.OfPlayer?.Name, "Player");

        string settlementRaw = null;
        var parent = Find.CurrentMap?.Parent;
        if (parent != null) settlementRaw = parent.LabelCap.ToString();
        var settlement = Sanitize(settlementRaw, "Colony");

        return Path.Combine(RootFolder, faction, settlement);
    }

    /// <summary>
    /// Delete the blueprint XML and portrait PNG files directly inside this colony folder so a
    /// re-save is a clean snapshot. Only touches this leaf folder, never recurses; every delete
    /// is guarded so one locked file can't abort the save.
    /// </summary>
    private static void ClearColonyFolder(DirectoryInfo dir)
    {
        try
        {
            foreach (var file in dir.GetFiles())
            {
                if (file.Extension != ".xml" && file.Extension != ".png") continue;
                try { file.Delete(); }
                catch (Exception ex) { Log.Warning($"[Pawn Editor] Could not delete '{file.Name}': {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] ClearColonyFolder failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Return a name not already used this run, appending " 2", " 3"… for duplicate pawn labels
    /// (two "Muffalo" become "Muffalo" and "Muffalo 2"). The folder is cleared first, so this only
    /// has to disambiguate within the current batch.
    /// </summary>
    private static string UniqueFileName(HashSet<string> used, string baseName)
    {
        var name = baseName;
        var n = 1;
        while (!used.Add(name))
        {
            n++;
            name = $"{baseName} {n}";
        }
        return name;
    }

    /// <summary>
    /// Strip characters the filesystem rejects, falling back to a safe default when the source
    /// is empty or reduces to nothing (e.g. a name made only of invalid characters).
    /// </summary>
    private static string Sanitize(string raw, string fallback)
    {
        if (raw.NullOrEmpty()) return fallback;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(raw.Where(c => Array.IndexOf(invalid, c) < 0).ToArray()).Trim();
        return cleaned.NullOrEmpty() ? fallback : cleaned;
    }
}
