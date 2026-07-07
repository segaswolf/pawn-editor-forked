using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

// Partial — Save/Load menu items and Randomization options.
public static partial class PawnEditor
{
    private static IEnumerable<SaveLoadItem> GetSaveLoadItems()
    {
        if (showFactionInfo)
            yield return new SaveLoadItem<Faction>("PawnEditor.Selected".Translate(), selectedFaction, new()
            {
                LoadLabel = "PawnEditor.LoadFaction".Translate()
            });
        else
        {
            IntVec3 pos = default;
            Map map = null;
            Rot4 rot = default;
            ThingOwner parent = null;
            Faction originalFaction = null;

            // ── Duplicate pawn (in-memory clone) ──
            yield return new SaveItem("PawnEditor.DuplicatePawn".Translate(), () =>
            {
                var stableClone = CreateStableDuplicateOrSelf(selectedPawn);
                AddPawn(stableClone, selectedCategory).HandleResult();
                try { EnsurePawnGraphicsInitialized(stableClone); } catch { }
                try { stableClone.Drawer?.renderer?.SetAllGraphicsDirty(); } catch { }
                try { PortraitsCache.SetDirty(stableClone); } catch { }
                try { GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(stableClone); } catch { }
                FacialAnimCompat.CopyFacialData(selectedPawn, stableClone);
                NotifyColonistBarsDirty();
                try { Find.ColonistBar?.MarkColonistsDirty(); } catch { }
            });

            // ── Save: Blueprint only (no Scribe) ──
            yield return new SaveItem("Save".Translate() + " " + "PawnEditor.Selected".Translate().ToLower() + " " + "PawnEditor.Pawn".Translate().ToLower(), () =>
                BlueprintLoadUtility.SavePawnBlueprint(selectedPawn, selectedCategory.ToString()));

            // ── Load: Replace selected pawn ──
            yield return new LoadItem("PawnEditor.LoadPawnReplace".Translate(), () =>
            {
                if (selectedPawn == null) return;
                originalFaction = selectedPawn.Faction;
                if (selectedPawn.Spawned)
                {
                    pos = selectedPawn.Position;
                    rot = selectedPawn.Rotation;
                    map = selectedPawn.Map;
                }
                else if (selectedPawn.SpawnedOrAnyParentSpawned)
                {
                    parent = selectedPawn.holdingOwner;
                }

                var oldPawn = selectedPawn;

                BlueprintLoadUtility.LoadPawnBlueprintReplace(oldPawn, selectedCategory.ToString(), newPawn =>
                {
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        if (Pregame)
                        {
                            Find.GameInitData.startingPossessions.Remove(oldPawn);
                            var idx = Find.GameInitData.startingAndOptionalPawns.IndexOf(oldPawn);
                            if (idx >= 0)
                                Find.GameInitData.startingAndOptionalPawns[idx] = newPawn;
                            Find.GameInitData.startingPossessions[newPawn] = new();
                        }

                        if (oldPawn.Spawned) oldPawn.DeSpawn();

                        try
                        {
                            if (!Pregame && Find.WorldPawns?.Contains(oldPawn) == true)
                                Find.WorldPawns.RemoveAndDiscardPawnViaGC(oldPawn);
                        }
                        catch (System.Exception ex) { Log.Warning($"[Pawn Editor] RemoveAndDiscardPawnViaGC: {ex.Message}"); }

                        if (map != null)
                        {
                            GenSpawn.Spawn(newPawn, pos, map, rot, WipeMode.VanishOrMoveAside, true);
                            try { newPawn.Notify_Teleported(); }
                            catch (System.Exception ex) { Log.Warning($"[Pawn Editor] Notify_Teleported: {ex.Message}"); }
                        }
                        else if (parent != null)
                        {
                            parent.TryAdd(newPawn, false);
                        }

                        if (!Pregame && originalFaction != null && newPawn.Faction != originalFaction)
                            newPawn.SetFaction(originalFaction);

                        selectedPawn = newPawn;
                        try { EnsurePawnGraphicsInitialized(newPawn); } catch { }
                        try { newPawn.Drawer?.renderer?.SetAllGraphicsDirty(); } catch { }
                        try { newPawn.Notify_DisabledWorkTypesChanged(); } catch { }
                        NotifyColonistBarsDirty();
                        try { Find.ColonistBar?.MarkColonistsDirty(); } catch { }

                        try { PawnList.UpdateCache(selectedFaction, selectedCategory); } catch { }
                        try { CheckChangeTabGroup(); } catch { }
                    });
                });
            });

            // ── Load: As new clone ──
            yield return new LoadItem("PawnEditor.LoadPawnAsClone".Translate(), () =>
            {
                BlueprintLoadUtility.LoadPawnBlueprint(selectedCategory.ToString(), newPawn =>
                {
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        AddPawn(newPawn, selectedCategory).HandleResult();
                        try { newPawn.Drawer?.renderer?.SetAllGraphicsDirty(); } catch { }
                        try { newPawn.Notify_DisabledWorkTypesChanged(); } catch { }
                        NotifyColonistBarsDirty();
                        try { Find.ColonistBar?.MarkColonistsDirty(); } catch { }
                    });
                });
            });
        }

        if (Pregame)
            yield return new SaveLoadItem<StartingThingsManager.StartingPreset>("PawnEditor.StartingPreset".Translate().CapitalizeFirst(), new());
        else
        {
            // v3.1 — Portable colony save/load: pawns only (no map, no research), tolerant of missing
            // mods/versions. Replaces the old full-map Scribe colony save/load (removed), which was
            // frail across versions/mods and didn't pair with the portable save.
            yield return new SaveItem("PawnEditor.SaveColonyPawns".Translate(), ColonySaveUtility.SaveColony);
            yield return new LoadItem("PawnEditor.LoadColonyPawns".Translate(), ShowLoadColonyMenu);
        }

        if (curTab != null)
            if (showFactionInfo)
                foreach (var item in curTab.GetSaveLoadItems(selectedFaction))
                    yield return item;
            else
                foreach (var item in curTab.GetSaveLoadItems(selectedPawn))
                    yield return item;
    }

    // v3.1 — Pick which saved colony to load. Lists each colony folder (faction / settlement) that
    // holds blueprints; loading runs the two-pass remap orchestrator (ColonyLoadUtility).
    private static void ShowLoadColonyMenu()
    {
        var colonies = ColonyLoadUtility.GetSavedColonies();
        if (colonies.Count == 0)
        {
            Messages.Message("PawnEditor.NoSavedColonies".Translate(), MessageTypeDefOf.RejectInput, false);
            return;
        }

        var options = colonies
            .Select(c => new FloatMenuOption(c.label, () => ColonyLoadUtility.LoadColony(c.folderType)))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private static IEnumerable<FloatMenuOption> GetRandomizationOptions()
    {
        if (curTab == null) return System.Linq.Enumerable.Empty<FloatMenuOption>();
        if (showFactionInfo)
        {
            return curTab.GetRandomizationOptions(selectedFaction);
        }
        else
        {
            List<FloatMenuOption> options = curTab.GetRandomizationOptions(selectedPawn).Select(option => new FloatMenuOption("PawnEditor.Randomize".Translate() + " " + option.Label.ToLower(), () =>
            {
                lastRandomization = option;
                option.action();
                Notify_PointsUsed();
            })).ToList();
            return options as IEnumerable<FloatMenuOption>;
        }
    }
}
