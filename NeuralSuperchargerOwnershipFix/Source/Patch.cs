using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;

namespace NeuralSuperchargerOwnershipFix
{
    public sealed class Bootstrap : Mod
    {
        public Bootstrap(ModContentPack content) : base(content)
        {
            try
            {
                var harmony = new Harmony("localfix.neuralsuperchargerownership");
                harmony.PatchAll();
                Log.Message("[NeuralSuperchargerOwnershipFix] Loaded.");
            }
            catch (Exception ex)
            {
                Log.Error($"[NeuralSuperchargerOwnershipFix] Failed to load: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_CompNeuralSuperchargerOwnership_PostExposeData
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName("NeuralSuperchargerOwnership.Comps.CompNeuralSuperchargerOwnership");
            if (t == null) yield break;

            var m = AccessTools.Method(t, "PostExposeData");
            if (m != null) yield return m;
        }

        static void Prefix(object __instance)
        {
            try
            {
                var t = __instance.GetType();
                var ownedNeuralSuperchargersField = AccessTools.Field(t, "ownedNeuralSuperchargers");
                if (ownedNeuralSuperchargersField == null) return;

                var list = ownedNeuralSuperchargersField.GetValue(__instance) as List<Building>;
                if (list == null || list.Count == 0) return;

                var uniqueList = new List<Building>();
                foreach (var b in list)
                {
                    if (b != null && !uniqueList.Contains(b))
                    {
                        uniqueList.Add(b);
                    }
                }

                ownedNeuralSuperchargersField.SetValue(__instance, uniqueList);
            }
            catch (Exception ex)
            {
                Log.Warning($"[NeuralSuperchargerOwnershipFix] Failed to deduplicate neural superchargers: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_CompAssignableToPawn_NeuralSupercharger_PostExposeData
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName("NeuralSuperchargerOwnership.Comps.CompAssignableToPawn_NeuralSupercharger");
            if (t == null) yield break;

            var m = AccessTools.Method(t, "PostExposeData");
            if (m != null) yield return m;
        }

        static void Prefix(object __instance)
        {
            try
            {
                var t = __instance.GetType();
                var assignedPawnsField = AccessTools.Field(t, "assignedPawns");
                if (assignedPawnsField == null) return;

                var list = assignedPawnsField.GetValue(__instance) as List<Pawn>;
                if (list == null || list.Count == 0) return;

                var uniqueList = new List<Pawn>();
                foreach (var p in list)
                {
                    if (p != null && !uniqueList.Contains(p))
                    {
                        uniqueList.Add(p);
                    }
                }

                assignedPawnsField.SetValue(__instance, uniqueList);
            }
            catch (Exception ex)
            {
                Log.Warning($"[NeuralSuperchargerOwnershipFix] Failed to deduplicate assigned pawns: {ex.Message}");
            }
        }
    }
}
