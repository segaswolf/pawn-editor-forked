using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
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
        // Let the user pick which categories to include (Humanlike / Animals / Mechs) before saving.
        // The note makes the scope explicit: only pawns currently on the map are saved (a colonist away
        // in a caravan is left out on purpose).
        Find.WindowStack.Add(new Dialog_ColonyCategories(
            "PawnEditor.SaveColonyPawns".Translate(), "Save".Translate(), SaveColonyFiltered,
            note: "PawnEditor.ColonySaveScopeNote".Translate()));
    }

    private static void SaveColonyFiltered(bool humans, bool animals, bool mechs)
    {
        var pawns = GetColonyPawns(humans, animals, mechs);
        if (pawns.Count == 0)
        {
            Messages.Message("Pawn Editor: No colony pawns found to save.", MessageTypeDefOf.RejectInput, false);
            return;
        }

        var folderType = BuildColonyFolderType();

        // A save already exists here: offer BOTH behaviours. Overwrite replaces the whole colony
        // with the selected categories (wipes the folder). Merge replaces ONLY the selected
        // categories and keeps the rest (so saving just "Animals" won't nuke the saved humans).
        if (ColonyFolderHasSave(folderType))
        {
            Find.WindowStack.Add(new Dialog_MessageBox(
                $"A colony save already exists at '{folderType}'.\n\n" +
                "Overwrite: replace the whole save with the selected categories.\n" +
                "Merge: update only the selected categories, keep the rest.",
                "PawnEditor.ColonyOverwriteAll".Translate(), () => RunColonySave(pawns, folderType, humans, animals, mechs, merge: false),
                "PawnEditor.ColonyMerge".Translate(),        () => RunColonySave(pawns, folderType, humans, animals, mechs, merge: true),
                title: "PawnEditor.SaveColonyPawns".Translate(),
                buttonADestructive: true));
            return;
        }

        RunColonySave(pawns, folderType, humans, animals, mechs, merge: false);
    }

    /// <summary>True if this colony folder already contains at least one saved blueprint.</summary>
    private static bool ColonyFolderHasSave(string folderType)
    {
        try { return SaveLoadUtility.SaveFolderForItemType(folderType).GetFiles().Any(f => f.Extension == ".xml"); }
        catch { return false; }
    }

    /// <summary>
    /// Run the save loop inside a SYNCHRONOUS long event: a full colony can be 30+ pawns, each ~30 ms
    /// of XML write plus a portrait render. Without a long event the UI freezes with no feedback;
    /// with one the user sees a "Saving..." bar. doAsynchronously:false keeps the work on the main
    /// thread, which the portrait render (SavePawnTex) requires.
    /// </summary>
    private static void RunColonySave(List<Pawn> pawns, string folderType, bool humans, bool animals, bool mechs, bool merge)
    {
        LongEventHandler.QueueLongEvent(
            () => PawnEditorProfiler.Measure("SaveLoad.SaveColony", PawnEditorProfiler.Cadence.PerAction,
                () => SaveColonyPawns(pawns, folderType, humans, animals, mechs, merge)),
            "PawnEditor.SavingColony", doAsynchronously: false, null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Pawn discovery
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ACTIVE colony: living player-faction pawns present on the current map (colonists, slaves,
    /// animals, mechs). World-scattered player pawns (caravans, other maps, quest lodgers) are
    /// intentionally EXCLUDED — they aren't "the colony" and re-spawning them elsewhere on load causes
    /// problems. (A future CAPA 3 option could let the user include them.)
    /// </summary>
    private static List<Pawn> GetColonyPawns(bool humans = true, bool animals = true, bool mechs = true)
    {
        var result = new List<Pawn>();
        try
        {
            var map = Find.CurrentMap;
            if (map?.mapPawns?.AllPawns != null)
                result.AddRange(map.mapPawns.AllPawns);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] GetColonyPawns failed: {ex.Message}"); }

        return result
            .Where(p => p != null && !p.Dead && p.Faction != null && p.Faction.IsPlayer
                     && CategoryEnabled(p, humans, animals, mechs))
            .Distinct()
            .ToList();
    }

    /// <summary>Whether a pawn's category is among the ones the user chose to include.</summary>
    private static bool CategoryEnabled(Pawn p, bool humans, bool animals, bool mechs)
    {
        if (PawnCategory.Mechs.Includes(p))   return mechs;
        if (PawnCategory.Animals.Includes(p)) return animals;
        if (PawnCategory.Humans.Includes(p))  return humans;
        return true; // anything uncategorized (shouldn't happen) is kept
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Save loop
    // ─────────────────────────────────────────────────────────────────────────

    private static void SaveColonyPawns(List<Pawn> pawns, string folderType, bool humans, bool animals, bool mechs, bool merge)
    {
        try
        {
            var dir = SaveLoadUtility.SaveFolderForItemType(folderType);

            // Overwrite: wipe the whole leaf folder (clean snapshot). Merge: wipe only the files of
            // the categories we're re-saving, keeping the rest untouched.
            ClearColonyFolder(dir, humans, animals, mechs, merge);

            // Seed used names with files that survived a merge (other categories), so a new pawn
            // never clobbers a kept file of a different category.
            var usedNames = new HashSet<string>();
            try { foreach (var f in dir.GetFiles("*.xml")) usedNames.Add(Path.GetFileNameWithoutExtension(f.Name)); }
            catch { }

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
    /// Make the colony folder a clean slate before a re-save.
    ///
    /// OVERWRITE (merge = false): delete the WHOLE leaf folder and recreate it empty. Deleting the
    /// directory outright is far more reliable than removing files one by one — a single awkward or
    /// locked file used to survive, so the old colony's blueprints lingered and a later load pulled
    /// old + new pawns together ("the old colony showed up on top"). This also clears stale portraits
    /// and any leftovers from a previous, larger save.
    ///
    /// MERGE (merge = true): only delete the files of the categories being re-saved, keep the rest.
    /// Every delete is guarded so one locked file can't abort the save.
    /// </summary>
    private static void ClearColonyFolder(DirectoryInfo dir, bool humans, bool animals, bool mechs, bool merge)
    {
        try
        {
            if (!merge)
            {
                if (dir.Exists) dir.Delete(true);
                Directory.CreateDirectory(dir.FullName);
                dir.Refresh();
                return;
            }

            foreach (var file in dir.GetFiles())
            {
                if (file.Extension != ".xml" && file.Extension != ".png") continue;

                // Only remove files whose category is being re-saved; keep the others intact.
                var xml = file.Extension == ".xml"
                    ? file
                    : new FileInfo(Path.ChangeExtension(file.FullName, ".xml"));
                if (!FileIsSelectedCategory(xml, humans, animals, mechs)) continue;

                try { file.Delete(); }
                catch (Exception ex) { Log.Warning($"[Pawn Editor] Could not delete '{file.Name}': {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] ClearColonyFolder failed: {ex.Message}");
        }
    }

    /// <summary>Read a saved blueprint's category (from its kindDef's race) so a MERGE save only
    /// replaces the selected categories. Unknown/missing kind => treated as not-selected (kept).</summary>
    private static bool FileIsSelectedCategory(FileInfo xmlFile, bool humans, bool animals, bool mechs)
    {
        try
        {
            if (xmlFile == null || !xmlFile.Exists) return false;
            var doc = new XmlDocument();
            doc.Load(xmlFile.FullName);
            var kindName = doc.DocumentElement?.SelectSingleNode("kindDef")?.Attributes?["defName"]?.Value;
            var race = kindName.NullOrEmpty() ? null
                : DefDatabase<PawnKindDef>.GetNamedSilentFail(kindName)?.race?.race;
            if (race == null) return false;
            if (race.IsMechanoid) return mechs;
            if (race.Animal)      return animals;
            return humans;
        }
        catch { return false; }
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
