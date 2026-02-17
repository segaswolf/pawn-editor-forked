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

            value = Scribe.loader.curParent switch
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
                Thing => Find.UniqueIDsManager.GetNextThingID(),
                _ => -1
            };

            if (value == -1)
            {
                Log.Error($"Unrecognized item in ID reassignment: {Scribe.loader.curParent}");
                return true;
            }
        }

        return true;
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