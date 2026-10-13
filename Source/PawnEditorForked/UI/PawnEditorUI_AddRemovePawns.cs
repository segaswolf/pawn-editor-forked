using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PawnEditor;

// Adding and deleting pawns, pregame and in game, and where a new pawn spawns.
public static partial class PawnEditor
{
    private const int SpawnRadiusNearColonist = 10;
    private const int SpawnRadiusNearCenter = 30;
    private const int SpawnRadiusAnywhere = 999;

    private static void ConfirmDeletePawn(Pawn pawn)
    {
        if (pawn == null || pawn.Discarded) return;
        Find.WindowStack.Add(new Dialog_Confirm("PawnEditor.ReallyDelete".Translate(pawn.NameShortColored), "ConfirmDeletePawn",
            () => DeletePawn(pawn), true));
    }

    private static void DeletePawn(Pawn pawn)
    {
        var removed = Pregame ? TryRemoveStartingPawn(pawn, out var nextPawn) : TryRemoveGamePawn(pawn, out nextPawn);
        if (!removed) return;

        selectedPawn = nextPawn;
        DoRecache();
        RefreshTabs();
        NotifyColonistBarsDirty();
        Notify_PointsUsed();
    }

    private static bool TryRemoveStartingPawn(Pawn pawn, out Pawn nextPawn)
    {
        var humans = selectedCategory == PawnCategory.Humans;
        var pawns = humans ? Find.GameInitData.startingAndOptionalPawns : StartingThingsManager.GetPawns(selectedCategory);
        var index = pawns.IndexOf(pawn);
        nextPawn = PawnSelectedAfterRemoving(pawn, pawns, index);
        if (index < 0) return false;

        if (humans)
            RemoveStartingColonist(pawn, pawns, index);
        else
            StartingThingsManager.RemovePawn(selectedCategory, pawn);
        pawn.Discard(true);
        return true;
    }

    private static void RemoveStartingColonist(Pawn pawn, List<Pawn> pawns, int index)
    {
        Find.GameInitData.startingPossessions.Remove(pawn);
        if (index < Find.GameInitData.startingPawnCount)
            Find.GameInitData.startingPawnCount = Math.Max(0, Find.GameInitData.startingPawnCount - 1);
        pawns.RemoveAt(index);
    }

    private static bool TryRemoveGamePawn(Pawn pawn, out Pawn nextPawn)
    {
        var (pawns, sections, _) = PawnList.GetLists();
        var index = pawns.IndexOf(pawn);
        nextPawn = PawnSelectedAfterRemoving(pawn, pawns, index);
        if (index < 0) return false;

        PawnList.OnDelete(pawn);
        pawns.RemoveAt(index);
        sections.RemoveAt(index);
        return true;
    }

    /// <summary>Keeps the current selection, unless it is the pawn being removed: then its neighbour.</summary>
    private static Pawn PawnSelectedAfterRemoving(Pawn removed, List<Pawn> pawns, int index)
    {
        if (selectedPawn != removed || index < 0) return selectedPawn;
        return pawns.Count > 1 ? pawns.Get(index + 1) : null;
    }

    private static void RefreshTabs()
    {
        TabWorker<Pawn>.Notify_OpenedDialog();
        TabWorker_Gear.ClearCaches();
    }

    /// <summary>Opens the "add pawn" menu: by kind, from a saved blueprint, or (humans) a backer.</summary>
    public static void AddPawn(PawnCategory category)
    {
        var options = new List<FloatMenuOption>
        {
            new("PawnEditor.Add.PawnKind".Translate(),
                () => Find.WindowStack.Add(new ListingMenu_PawnKindDef(category, kind => AddPawnOfKind(kind, category)))),
            new("PawnEditor.Add.Saved".Translate(category.Label()),
                () => BlueprintLoadUtility.LoadPawnBlueprint(category.ToString(), newPawn => AddPawn(newPawn, category).HandleResult()))
        };

        if (category == PawnCategory.Humans)
            options.Add(new("PawnEditor.Add.Backer".Translate(), () => Find.WindowStack.Add(new ListingMenu<PawnBio>(
                SolidBioDatabase.allBios, bio => bio.name.ToStringFull, bio => AddBackerPawn(bio, category), "Add backer pawn"))));

        Find.WindowStack.Add(new FloatMenu(options));
    }

    private static PawnGenerationContext GenerationContext =>
        Pregame ? PawnGenerationContext.PlayerStarter : PawnGenerationContext.NonPlayer;

    private static AddResult AddPawnOfKind(PawnKindDef kind, PawnCategory category) =>
        AddPawn(PawnGenerator.GeneratePawn(new(kind, selectedFaction, GenerationContext, forceGenerateNewPawn: true)), category);

    private static AddResult AddBackerPawn(PawnBio bio, PawnCategory category)
    {
        // With "No faction" selected there is no faction kind to use: fall back to a plain colonist (#23).
        var kind = selectedFaction?.def.basicMemberKind ?? PawnKindDefOf.Colonist;
        var pawn = PawnGenerator.GeneratePawn(new(kind, selectedFaction, GenerationContext,
            forceGenerateNewPawn: true, fixedBirthName: bio.name.First, fixedLastName: bio.name.Last,
            fixedGender: GenderOf(bio.gender)));
        pawn.Name = bio.name;
        pawn.story.Childhood = bio.childhood;
        pawn.story.Adulthood = bio.adulthood;
        return AddPawn(pawn, category);
    }

    private static Gender? GenderOf(GenderPossibility possibility) => possibility switch
    {
        GenderPossibility.Male => Gender.Male,
        GenderPossibility.Female => Gender.Female,
        GenderPossibility.Either => null,
        _ => throw new ArgumentOutOfRangeException(nameof(possibility), possibility, null)
    };

    public static AddResult AddPawn(Pawn addedPawn, PawnCategory category) =>
        new ConditionalInfo(CanUsePoints(addedPawn), new SuccessInfo(() => CompleteAdd(addedPawn, category)));

    private static void CompleteAdd(Pawn addedPawn, PawnCategory category)
    {
        if (!Pregame && selectedFaction != null && addedPawn.Faction != selectedFaction)
            addedPawn.SetFaction(selectedFaction);

        if (Pregame)
            AddStartingPawn(addedPawn, category);
        else
            AddGamePawn(addedPawn, category);

        PawnList.UpdateCache(selectedFaction, category);
        NotifyColonistBarsDirty();
        TabWorker_AnimalMech.Notify_PawnAdded(category);
        Notify_PointsUsed();
        Select(addedPawn);
    }

    private static void AddStartingPawn(Pawn pawn, PawnCategory category)
    {
        if (category == PawnCategory.Humans)
        {
            Find.GameInitData.startingAndOptionalPawns.Add(pawn);
            Find.GameInitData.startingPossessions.Add(pawn, new());
            return;
        }

        StartingThingsManager.AddPawn(category, pawn);
        if (category == PawnCategory.Mechs)
            AssignStartingOverseer(pawn);
    }

    /// <summary>FIX #018: gives a starting mech an overseer, or it spawns rogue.</summary>
    private static void AssignStartingOverseer(Pawn mech)
    {
        if (mech.GetOverseer() != null) return;

        var mechanitor = Find.GameInitData.startingAndOptionalPawns
            .FirstOrDefault(p => p.RaceProps.Humanlike && MechanitorUtility.IsMechanitor(p));
        if (mechanitor != null)
            mech.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mechanitor);
        else
            Messages.Message("PawnEditor.MechNoOverseer".Translate(mech.LabelCap), MessageTypeDefOf.CautionInput, false);
    }

    /// <summary>Player colonists spawn on the current map; everyone else goes to the world.</summary>
    private static void AddGamePawn(Pawn pawn, PawnCategory category)
    {
        pawn.teleporting = true;
        var spawnsOnMap = category == PawnCategory.Humans && selectedFaction == Faction.OfPlayer && Find.CurrentMap != null;
        var spawned = spawnsOnMap && Diagnostics.Run("Spawning the added pawn", pawn, () => SpawnOnCurrentMap(pawn), fallback: false);
        if (!spawned)
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
        pawn.teleporting = false;
    }

    private static bool SpawnOnCurrentMap(Pawn pawn)
    {
        GenSpawn.Spawn(pawn, FindSafeSpawnCell(Find.CurrentMap), Find.CurrentMap);
        Diagnostics.Run("Initializing the spawned pawn's graphics", pawn, () =>
        {
            pawn.Notify_Teleported();
            EnsurePawnGraphicsInitialized(pawn);
        });
        // VE Hussar Giant: genes ran PostAdd before spawn, with no renderer yet. Dirtying the caches now
        // applies the gene's visual offset without a reload.
        Diagnostics.Run("Refreshing the spawned pawn's portrait", pawn, () => PortraitsCache.SetDirty(pawn));
        Diagnostics.Run("Refreshing the spawned pawn's texture atlas", pawn, () => GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn));
        return true;
    }

    /// <summary>
    /// A standable cell to spawn a pawn: near a free colonist, else near the map center, else any
    /// unfogged standable cell, else anywhere.
    /// </summary>
    public static IntVec3 FindSafeSpawnCell(Map map)
    {
        var colonists = map.mapPawns?.FreeColonistsSpawned;
        if (colonists is { Count: > 0 }
            && CellFinder.TryRandomClosewalkCellNear(colonists.RandomElement().Position, map, SpawnRadiusNearColonist, out var nearColonist))
            return nearColonist;

        if (CellFinder.TryRandomClosewalkCellNear(map.Center, map, SpawnRadiusNearCenter, out var nearCenter))
            return nearCenter;

        if (CellFinder.TryFindRandomCellNear(map.Center, map, SpawnRadiusAnywhere, c => c.Standable(map) && !c.Fogged(map), out var anywhere))
            return anywhere;

        return CellFinder.RandomCell(map);
    }
}
