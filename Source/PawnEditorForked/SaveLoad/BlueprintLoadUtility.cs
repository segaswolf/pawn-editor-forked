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
                PawnBlueprintSaveLoad.SaveBlueprint(pawn, path);
                Messages.Message($"Pawn Editor: Saved '{pawn.LabelCap}' as blueprint.", MessageTypeDefOf.TaskCompletion, false);
            }
            catch (Exception ex)
            {
                Log.Error($"[Pawn Editor] Blueprint save failed: {ex}");
                Messages.Message("Pawn Editor: Failed to save blueprint. Check log.", MessageTypeDefOf.RejectInput, false);
            }
        }, pawn.LabelShort));
    }
}
