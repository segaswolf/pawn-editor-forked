using System;
using System.IO;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Bridges PawnBlueprintSaveLoad with the existing Pawn Editor UI.
/// Supports both the new Blueprint format and legacy Scribe files.
/// </summary>
public static class BlueprintLoadUtility
{
    /// <summary>
    /// Shows file picker → detects format → loads pawn.
    /// Blueprint files use the new safe loader. Legacy files fall back to Scribe.
    /// </summary>
    public static void LoadPawnBlueprint(string typePostfix, Action<Pawn> callback)
    {
        var type = typeof(Pawn).Name;
        var folder = typePostfix.NullOrEmpty() ? type : Path.Combine(type, typePostfix);

        Find.WindowStack.Add(new Dialog_PawnEditorFiles_Load(folder, path =>
        {
            Pawn pawn = null;

            if (PawnBlueprintSaveLoad.IsBlueprintFile(path))
            {
                // New format
                try { pawn = PawnBlueprintSaveLoad.LoadBlueprint(path); }
                catch (Exception ex) { Log.Error($"[Pawn Editor] Blueprint load failed: {ex}"); }
            }

            if (pawn == null)
            {
                // Legacy format — fall back to Scribe + Obelisk clone
                Log.Message("[Pawn Editor] Using legacy Scribe loader for non-blueprint file.");
                var legacyPawn = new Pawn();
                SaveLoadUtility.LoadItem(legacyPawn, p =>
                {
                    var clone = PawnEditor.CreateStableDuplicateOrSelf(p);
                    callback?.Invoke(clone);
                    try { PawnEditor.Notify_PointsUsed(); } catch { }
                }, typePostfix: typePostfix);
                return;
            }

            callback?.Invoke(pawn);
            try { PawnEditor.Notify_PointsUsed(); } catch { }
            try { Patch_TacticalGroups.ResetErrorCounter(); } catch { }
        }));
    }

    /// <summary>
    /// Blueprint-based replace. Loads a pawn and assigns the faction of the existing pawn.
    /// </summary>
    public static void LoadPawnBlueprintReplace(Pawn existingPawn, string typePostfix, Action<Pawn> onLoaded)
    {
        var type = typeof(Pawn).Name;
        var folder = typePostfix.NullOrEmpty() ? type : Path.Combine(type, typePostfix);

        Find.WindowStack.Add(new Dialog_PawnEditorFiles_Load(folder, path =>
        {
            Pawn pawn = null;

            if (PawnBlueprintSaveLoad.IsBlueprintFile(path))
            {
                try { pawn = PawnBlueprintSaveLoad.LoadBlueprint(path); }
                catch (Exception ex) { Log.Error($"[Pawn Editor] Blueprint replace failed: {ex}"); }
            }

            if (pawn == null)
            {
                // Legacy fallback
                Log.Message("[Pawn Editor] Using legacy Scribe loader for replace.");
                SaveLoadUtility.LoadItem(existingPawn, p =>
                {
                    onLoaded?.Invoke(p);
                    try { PawnEditor.Notify_PointsUsed(); } catch { }
                }, typePostfix: typePostfix);
                return;
            }

            if (existingPawn?.Faction != null && pawn.Faction != existingPawn.Faction)
                pawn.SetFaction(existingPawn.Faction);

            onLoaded?.Invoke(pawn);
            try { PawnEditor.Notify_PointsUsed(); } catch { }
            try { Patch_TacticalGroups.ResetErrorCounter(); } catch { }
        }));
    }

    /// <summary>
    /// Save a pawn in Blueprint format. Shows the file picker dialog.
    /// </summary>
    public static void SavePawnBlueprint(Pawn pawn, string typePostfix)
    {
        var type = typeof(Pawn).Name;
        var folder = typePostfix.NullOrEmpty() ? type : Path.Combine(type, typePostfix);

        Find.WindowStack.Add(new Dialog_PawnEditorFiles_Save(folder, path =>
        {
            try
            {
                // Clicking "Overwrite" on another pawn's file passes THAT file's name here, so a blueprint
                // could end up named after a different pawn than it contains (load "Jin", get Grifyn). A
                // blueprint's filename IS its identity, so retarget the save to the CURRENT pawn's name
                // and remove the old file we overwrote (and its portrait PNG). Not done in the generic
                // dialog because that one also saves non-pawn items where the slot name is intentional.
                var finalPath = RetargetToPawnName(path, pawn);

                PawnBlueprintSaveLoad.SaveBlueprint(pawn, finalPath);
                Messages.Message($"Pawn Editor: Saved '{pawn.LabelCap}' as blueprint.", MessageTypeDefOf.TaskCompletion, false);
            }
            catch (Exception ex)
            {
                Log.Error($"[Pawn Editor] Blueprint save failed: {ex}");
                Messages.Message("Pawn Editor: Failed to save blueprint. Check log.", MessageTypeDefOf.RejectInput, false);
            }
        }, pawn.LabelShort));
    }

    /// <summary>
    /// Returns the path a blueprint should actually be written to, so the FILE NAME matches the pawn.
    /// If the picker handed us a path whose name already matches the pawn (typed-in save, or overwriting
    /// the pawn's own file), it's returned untouched. If it points at a DIFFERENT pawn's file (per-row
    /// "Overwrite"), we build a fresh path from the pawn's name and delete the old file plus its portrait
    /// PNG. Sanitised and de-duplicated so we never collide with an unrelated existing blueprint.
    /// </summary>
    private static string RetargetToPawnName(string clickedPath, Pawn pawn)
    {
        try
        {
            var dir = Path.GetDirectoryName(clickedPath);
            if (dir.NullOrEmpty()) return clickedPath;

            var desiredName = SanitizeFileName(pawn.LabelShort);
            if (desiredName.NullOrEmpty()) return clickedPath;

            var clickedName = Path.GetFileNameWithoutExtension(clickedPath);
            // Already correct (typed the name, or overwriting this pawn's own file): leave it alone.
            if (string.Equals(clickedName, desiredName, StringComparison.OrdinalIgnoreCase))
                return clickedPath;

            // Pick a free name. If "Grifyn.xml" is taken by an UNRELATED blueprint, don't clobber it —
            // fall back to "Grifyn 2", etc. The old clicked file is removed below regardless.
            var targetPath = Path.Combine(dir, desiredName + ".xml");
            var n = 2;
            while (File.Exists(targetPath) && !PathsEqual(targetPath, clickedPath))
                targetPath = Path.Combine(dir, $"{desiredName} {n++}.xml");

            // Remove the file we were told to overwrite, and its portrait, so no stale duplicate lingers.
            TryDelete(clickedPath);
            TryDelete(Path.ChangeExtension(clickedPath, ".png"));

            return targetPath;
        }
        catch (Exception ex)
        {
            // If anything about the rename goes wrong, fall back to the original behaviour rather than
            // failing the save. A misnamed file is annoying; a lost save is worse.
            Log.Warning($"[Pawn Editor] Could not retarget blueprint filename, saving as picked: {ex.Message}");
            return clickedPath;
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] Could not delete old blueprint file '{path}': {ex.Message}"); }
    }

    private static string SanitizeFileName(string name)
    {
        if (name.NullOrEmpty()) return name;
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
