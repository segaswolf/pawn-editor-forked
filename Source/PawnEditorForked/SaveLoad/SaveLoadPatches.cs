using System.Collections.Generic;
using System;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace PawnEditor;

public static partial class SaveLoadUtility
{
    private static readonly Dictionary<int, int> compatibilitySeedByThingId = new();

    public static void Notify_DeepSaved(object __0)
    {
        if (!currentlyWorking) return;
        if (__0 is ILoadReferenceable referenceable) savedItems.Add(referenceable);
    }

    public static IEnumerable<CodeInstruction> FixFactionWeirdness(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var codes = instructions.ToList();
        var info1 = AccessTools.Field(typeof(Thing), nameof(Thing.factionInt));
        var idx1 = codes.FindIndex(ins => ins.LoadsField(info1)) - 2;
        var label1 = generator.DefineLabel();
        var label2 = generator.DefineLabel();
        codes.InsertRange(idx1, new[]
        {
            CodeInstruction.LoadField(typeof(SaveLoadUtility), nameof(currentlyWorking)),
            new CodeInstruction(OpCodes.Brfalse, label2),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldflda, info1),
            new CodeInstruction(OpCodes.Ldstr, "faction"),
            new CodeInstruction(OpCodes.Ldc_I4_0),
            new CodeInstruction(OpCodes.Call, ReferenceLook.MakeGenericMethod(typeof(Faction))),
            new CodeInstruction(OpCodes.Br, label1),
            new CodeInstruction(OpCodes.Nop).WithLabels(label2)
        });
        var info2 = AccessTools.Method(typeof(Dictionary<Thing, string>), nameof(Dictionary<Thing, string>.Clear));
        var idx2 = codes.FindIndex(idx1, ins => ins.Calls(info2));
        codes[idx2 + 1].labels.Add(label1);
        return codes;
    }

    public static bool ReassignLoadID(ref int value, string label)
    {
        var isLoadId = label.Equals("loadID", StringComparison.OrdinalIgnoreCase);
        var isThingIdLabel = label.Equals("id", StringComparison.OrdinalIgnoreCase) || label.Equals("thingIDNumber", StringComparison.OrdinalIgnoreCase);

        if ((isLoadId || isThingIdLabel) && Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            // Fork fix: RimWorld Thing IDs are typically saved under label "id" (Thing.thingIDNumber).
            // Only remap those when the current parent is a Thing to avoid touching unrelated int fields.
            if (isThingIdLabel && Scribe.loader.curParent is not Thing)
                return true;

            Find.UniqueIDsManager.wasLoaded = true;

            if (isThingIdLabel && Scribe.loader.curParent is Pawn loadedPawn)
            {
                var loadedThingId = value;
                if (ReferenceEquals(loadedPawn, currentItem) && currentItem is Pawn existingPawn && existingPawn.thingIDNumber > 0)
                {
                    value = existingPawn.thingIDNumber;
                    compatibilitySeedByThingId.Remove(value);
                    return true;
                }

                value = Find.UniqueIDsManager.GetNextThingID();
                RegisterCompatibilitySeed(value, loadedThingId);
                return true;
            }

            var nextId = Scribe.loader.curParent switch
            {
                Hediff => Find.UniqueIDsManager.GetNextHediffID(),
                Lord => Find.UniqueIDsManager.GetNextLordID(),
                ShipJob => Find.UniqueIDsManager.GetNextShipJobID(),
                RitualRole => Find.UniqueIDsManager.GetNextRitualRoleID(),
                StorageGroup => Find.UniqueIDsManager.GetNextStorageGroupID(),
                PassingShip => Find.UniqueIDsManager.GetNextPassingShipID(),
                TransportShip => Find.UniqueIDsManager.GetNextTransportShipID(),
                Faction => Find.UniqueIDsManager.GetNextFactionID(),
                Bill => Find.UniqueIDsManager.GetNextBillID(),
                Job => Find.UniqueIDsManager.GetNextJobID(),
                Gene => Find.UniqueIDsManager.GetNextGeneID(),
                Battle => Find.UniqueIDsManager.GetNextBattleID(),
                Ability => TryInvokeUniqueIdMethod("GetNextAbilityID"),
                Thing => Find.UniqueIDsManager.GetNextThingID(),
                _ => -1
            };

            if (nextId <= 0)
            {
                Log.Warning($"[PawnEditor] Unrecognized item in ID reassignment: {Scribe.loader.curParent}. Keeping loaded value {value}.");
                return true;
            }

            value = nextId;
        }

        return true;
    }

    private static int TryInvokeUniqueIdMethod(string methodName)
    {
        var method = AccessTools.Method(typeof(UniqueIDsManager), methodName, Type.EmptyTypes);
        if (method == null) return -1;
        try
        {
            return (int)method.Invoke(Find.UniqueIDsManager, Array.Empty<object>());
        }
        catch
        {
            return -1;
        }
    }

    private static void RegisterCompatibilitySeed(int pawnThingId, int loadedThingId)
    {
        if (pawnThingId <= 0 || loadedThingId <= 0 || pawnThingId == loadedThingId) return;
        compatibilitySeedByThingId[pawnThingId] = loadedThingId;
    }

    public static int GetCompatibilitySeedThingId(Thing thing)
    {
        if (thing is not Pawn pawn) return thing?.thingIDNumber ?? -1;
        var id = pawn.thingIDNumber;
        return compatibilitySeedByThingId.TryGetValue(id, out var seed) ? seed : id;
    }

    public static IEnumerable<CodeInstruction> UseCompatibilitySeedInCompatibilityWith(IEnumerable<CodeInstruction> instructions)
    {
        var thingIdField = AccessTools.Field(typeof(Thing), nameof(Thing.thingIDNumber));
        var replacement = AccessTools.Method(typeof(SaveLoadUtility), nameof(GetCompatibilitySeedThingId));
        foreach (var instruction in instructions)
        {
            if (instruction.LoadsField(thingIdField))
                yield return new CodeInstruction(OpCodes.Call, replacement);
            else
                yield return instruction;
        }
    }

    public static void AssignCurrentPawn(Pawn __instance)
    {
        currentPawn = __instance;
    }

    public static void ClearCurrentPawn()
    {
        currentPawn = null;
    }
}
