using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// v3.1 — Colony load (the load half of the portable colony feature).
///
/// Loads every pawn blueprint in a colony folder as a fresh clone and re-links them to EACH OTHER,
/// never to originals still in the game. Passes:
///   1) Build every clone (cross-pawn sections deferred) and map its saved ThingID -> the new clone.
///   2) Add each clone to the colony.
///   3) Resolve every cross-pawn reference (bonds, master, overseer) THROUGH the remap, so a saved
///      bond to "Alexandra" points at the loaded CLONE of Alexandra, not the original.
///
/// The remap is keyed on ThingID (unique) so identical names (three "Alexandra"s) never collide, and
/// dangling references (a partner left out of the save) just drop with a warning — the pawn still
/// loads. This is the counterpart to <see cref="ColonySaveUtility"/>.
/// </summary>
public static class ColonyLoadUtility
{
    /// <summary>
    /// Load an entire colony folder (e.g. "Colony/&lt;faction&gt;/&lt;settlement&gt;").
    /// <paramref name="replace"/> = REPLACE mode: a loaded pawn that matches an existing one by its
    /// saved ThingID replaces that original (removed) instead of adding a duplicate clone; pawns with
    /// no match are added as new. Off = always add as new clones.
    /// </summary>
    public static void LoadColony(string folderType, bool replace = false,
        bool humans = true, bool animals = true, bool mechs = true)
    {
        LongEventHandler.QueueLongEvent(
            () => PawnEditorProfiler.Measure("SaveLoad.LoadColony", PawnEditorProfiler.Cadence.PerAction,
                () => LoadColonyInner(folderType, replace, humans, animals, mechs)),
            "PawnEditor.LoadingColony", doAsynchronously: false, null);
    }

    /// <summary>
    /// List every saved colony under the Colony/ folder: each leaf folder that holds at least one
    /// blueprint. Returns (folderType, label) where folderType feeds LoadColony and label is a
    /// friendly "faction / settlement" for a menu.
    /// </summary>
    public static List<(string folderType, string label)> GetSavedColonies()
    {
        var result = new List<(string, string)>();
        try
        {
            var baseDir = new DirectoryInfo(SaveLoadUtility.BaseSaveFolder);
            var root = new DirectoryInfo(Path.Combine(baseDir.FullName, "Colony"));
            if (!root.Exists) return result;

            foreach (var dir in root.GetDirectories("*", SearchOption.AllDirectories))
            {
                var xmls = dir.GetFiles("*.xml");
                if (xmls.Length == 0) continue;

                var rel = dir.FullName.Substring(baseDir.FullName.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                var name = rel.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                if (name.StartsWith("Colony/", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring("Colony/".Length);
                name = name.Replace("/", " / ");

                // Richer menu label: "faction / settlement  (N pawns, date)".
                var date = xmls.Max(f => f.LastWriteTime).ToString("g");
                var label = "PawnEditor.ColonyEntryLabel".Translate(name, xmls.Length, date).ToString();

                result.Add((rel, label));
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] GetSavedColonies: {ex.Message}"); }
        return result;
    }

    private static void LoadColonyInner(string folderType, bool replace, bool humans, bool animals, bool mechs)
    {
        List<FileInfo> files;
        try
        {
            files = SaveLoadUtility.SaveFolderForItemType(folderType).GetFiles()
                .Where(f => f.Extension == ".xml").ToList();
        }
        catch (Exception ex)
        {
            Log.Error($"[Pawn Editor] Colony load: cannot read folder '{folderType}': {ex.Message}");
            Messages.Message("Pawn Editor: Failed to read colony folder. Check log.", MessageTypeDefOf.RejectInput, false);
            return;
        }

        if (files.Count == 0)
        {
            Messages.Message("Pawn Editor: No pawns found in that colony save.", MessageTypeDefOf.RejectInput, false);
            return;
        }

        var pairs = new List<(Pawn clone, XmlNode root, PawnCategory cat)>();

        PawnBlueprintSaveLoad.ClearLoadWarnings();
        PawnBlueprintSaveLoad.ColonyRemap = new Dictionary<string, Pawn>();
        try
        {
            // ── Pass 1: build every clone (cross-pawn refs deferred) + remap by saved ThingID ──
            foreach (var file in files)
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.Load(file.FullName);
                    var root = doc.DocumentElement;
                    if (root == null || root.Name != "PawnBlueprint") continue;
                    if (!FileCategoryEnabled(root, humans, animals, mechs)) continue;

                    var clone = PawnBlueprintSaveLoad.BuildColonyPawnFromRoot(root);
                    if (clone == null) continue;

                    var savedId = root.SelectSingleNode("savedThingID")?.InnerText?.Trim();
                    if (!savedId.NullOrEmpty())
                        PawnBlueprintSaveLoad.ColonyRemap[savedId] = clone;

                    pairs.Add((clone, root, CategoryFor(clone)));
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Pawn Editor] Colony load: failed on '{file.Name}': {ex.Message}");
                }
            }

            // ── Pass 2: add every clone to the colony. REPLACE removes the matching pawn first — either
            // the true original (its ThingID still equals the savedThingID) OR a pawn previously loaded
            // from the same save (recognized via the origin stamp), so reloading replaces instead of
            // duplicating. Every loaded pawn is then stamped with its origin so a later Replace finds it
            // too. ──
            var origins = Current.Game?.GetComponent<GameComponent_ColonyOrigins>();
            foreach (var (clone, root, cat) in pairs)
            {
                var savedId = root.SelectSingleNode("savedThingID")?.InnerText?.Trim();
                try
                {
                    if (replace && !savedId.NullOrEmpty())
                    {
                        var existing = PawnBlueprintSaveLoad.GetAllReachablePawnsPublic()
                            .FirstOrDefault(p => p != null && p != clone
                                && (p.ThingID == savedId || (origins?.CameFrom(p, savedId) ?? false)));
                        if (existing != null) RemoveExisting(existing);
                    }
                    PawnEditor.AddPawn(clone, cat).HandleResult();
                    if (!savedId.NullOrEmpty()) origins?.Record(clone, savedId);
                }
                catch (Exception ex) { Log.Warning($"[Pawn Editor] Colony load: add/replace '{clone?.LabelShortCap}': {ex.Message}"); }
            }

            // ── Pass 3: resolve cross-pawn references through the remap (clone <-> clone) ──
            foreach (var (clone, root, _) in pairs)
            {
                try { PawnBlueprintSaveLoad.ApplyRelationalSections(clone, root); }
                catch (Exception ex) { Log.Warning($"[Pawn Editor] Colony load: relations '{clone?.LabelShortCap}': {ex.Message}"); }
            }
        }
        finally
        {
            PawnBlueprintSaveLoad.ColonyRemap = null;
            PawnBlueprintSaveLoad.FlushColonyWarnings(pairs.Count);
        }

        Messages.Message($"Pawn Editor: Loaded {pairs.Count} colony pawn(s) from {folderType}.",
            MessageTypeDefOf.TaskCompletion, false);

        try { PawnEditor.Notify_PointsUsed(); } catch { }
    }

    /// <summary>Remove an existing pawn so a loaded one can replace it (REPLACE mode). Uses the mod's
    /// canonical delete, which despawns and discards it and drops it from the editor list.</summary>
    private static void RemoveExisting(Pawn pawn)
    {
        try { PawnEditor.PawnList.OnDelete(pawn); }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] Colony replace: remove existing '{pawn?.LabelShortCap}': {ex.Message}"); }
    }

    /// <summary>
    /// Whether a saved blueprint's category (from its kindDef's race) is one the user chose to load.
    /// Resolves the kindDef WITHOUT building the pawn, so filtered-out files are skipped cheaply. A
    /// missing kindDef (mod removed) is kept, to be safe.
    /// </summary>
    private static bool FileCategoryEnabled(XmlNode root, bool humans, bool animals, bool mechs)
    {
        if (humans && animals && mechs) return true; // nothing filtered out
        try
        {
            var kindName = root.SelectSingleNode("kindDef")?.Attributes?["defName"]?.Value;
            var race = kindName.NullOrEmpty() ? null
                : DefDatabase<PawnKindDef>.GetNamedSilentFail(kindName)?.race?.race;
            if (race == null) return true;
            if (race.IsMechanoid) return mechs;
            if (race.Animal)      return animals;
            return humans;
        }
        catch { return true; }
    }

    /// <summary>Which editor category a loaded pawn belongs to (drives how AddPawn integrates it).</summary>
    private static PawnCategory CategoryFor(Pawn p)
    {
        if (PawnCategory.Mechs.Includes(p)) return PawnCategory.Mechs;
        if (PawnCategory.Animals.Includes(p)) return PawnCategory.Animals;
        return PawnCategory.Humans;
    }
}
