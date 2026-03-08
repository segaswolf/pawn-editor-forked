using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;

namespace StockpileRankingSafePatch
{
    public sealed class Bootstrap : Mod
    {
        public Bootstrap(ModContentPack content) : base(content)
        {
            try
            {
                var harmony = new Harmony("local.stockpilerankingsafepatch");
                harmony.PatchAll();
                Log.Message("[StockpileRankingSafePatch] Loaded.");
            }
            catch (Exception ex)
            {
                Log.Error($"[StockpileRankingSafePatch] Failed to load: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_RankComp_DetermineUsedFilter
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName("Stockpile_Ranking.RankComp");
            if (t == null) yield break;
            
            var m = AccessTools.Method(t, "DetermineUsedFilter");
            if (m != null) yield return m;
        }

        static bool Prefix(object __instance, List<ThingFilter> ranks)
        {
            if (ranks == null || ranks.Count == 0)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class Patch_RankComp_DetermineUsedFilters
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName("Stockpile_Ranking.RankComp");
            if (t == null) yield break;
            
            var m = AccessTools.Method(t, "DetermineUsedFilters");
            if (m != null) yield return m;
        }

        static bool Prefix(object __instance)
        {
            try
            {
                var t = AccessTools.TypeByName("Stockpile_Ranking.RankComp");
                var field = AccessTools.Field(t, "rankedSettings");
                if (field == null) return true;

                var rankedSettings = field.GetValue(__instance) as Dictionary<StorageSettings, List<ThingFilter>>;
                if (rankedSettings == null || rankedSettings.Count == 0)
                {
                    return false;
                }
            }
            catch
            {
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class Patch_RankComp_GameComponentTick
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName("Stockpile_Ranking.RankComp");
            if (t == null) yield break;
            
            var m = AccessTools.Method(t, "GameComponentTick");
            if (m != null) yield return m;
        }

        static bool Prefix(object __instance)
        {
            try
            {
                var t = __instance.GetType();
                var dirtyField = AccessTools.Field(t, "dirty");
                if (dirtyField == null) return true;

                var dirty = (bool)dirtyField.GetValue(__instance);
                if (!dirty)
                {
                    return false;
                }
            }
            catch
            {
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class Patch_RankComp_LoadedGame
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName("Stockpile_Ranking.RankComp");
            if (t == null) yield break;
            
            var m = AccessTools.Method(t, "LoadedGame");
            if (m != null) yield return m;
        }

        static bool Prefix(object __instance)
        {
            try
            {
                var t = __instance.GetType();
                var field = AccessTools.Field(t, "rankedSettings");
                if (field == null) return true;

                var rankedSettings = field.GetValue(__instance) as Dictionary<StorageSettings, List<ThingFilter>>;
                if (rankedSettings == null || rankedSettings.Count == 0)
                {
                    return false;
                }
            }
            catch
            {
            }
            return true;
        }
    }
}
