using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace RuntimeStabilityFix
{
    public sealed class Bootstrap : Mod
    {
        public Bootstrap(ModContentPack content) : base(content)
        {
            try
            {
                var harmony = new Harmony("localfix.runtime.stability");
                harmony.PatchAll();
                Log.Message("[RuntimeStabilityFix] Loaded.");
            }
            catch (Exception ex)
            {
                Log.Error($"[RuntimeStabilityFix] Failed to load: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_HighbornSocialTick
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var m = AccessTools.Method("SocialGene.Gene_SocialAffectRandom:Tick");
            if (m != null) yield return m;
        }

        static bool Prefix(object __instance)
        {
            if (__instance == null) return false;
            try
            {
                var type = __instance.GetType();
                var pawnField = AccessTools.Field(type, "pawn") ?? AccessTools.Field(type.BaseType, "pawn");
                var pawn = pawnField?.GetValue(__instance) as Pawn;
                if (pawn?.Map == null) return false;

                var pawns = pawn.Map.mapPawns?.AllPawnsSpawned;
                if (pawns == null) return false;

                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (p != null && p != pawn && p.RaceProps != null && p.RaceProps.Humanlike && !p.Dead)
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                Log.WarningOnce($"[RuntimeStabilityFix] Suppressed Highborn Tick exception: {__exception.GetType().Name}: {__exception.Message}", 0x77A1B001);
            return null;
        }
    }

    [HarmonyPatch]
    internal static class Patch_MultiFloorsExposePrepatcherFields
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var m = AccessTools.Method("MultiFloors.HarmonyPatches.HarmonyPatch_ExposePrepatcherFields:ExposeForBillGiver");
            if (m != null) yield return m;
        }

        static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                Log.WarningOnce($"[RuntimeStabilityFix] Suppressed MultiFloors ExposeForBillGiver exception: {__exception.GetType().Name}: {__exception.Message}", 0x77A1B002);
            return null;
        }
    }

    [HarmonyPatch]
    internal static class Patch_NightmareCoreMakeGraphicData
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var m = AccessTools.Method("NightmareCore.StitchedAtlas.Patch_ThingDefGenerator_Buildings:HELPER_MakeGraphicData");
            if (m != null) yield return m;
        }

        static bool Prefix(ThingDef def, ref GraphicData __result)
        {
            if (def == null)
            {
                __result = null;
                return false;
            }
            return true;
        }

        static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                Log.WarningOnce($"[RuntimeStabilityFix] Suppressed NightmareCore HELPER_MakeGraphicData exception: {__exception.GetType().Name}: {__exception.Message}", 0x77A1B003);
            return null;
        }
    }

    [HarmonyPatch]
    internal static class Patch_MinifiedThingLabelSafety
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var mt = AccessTools.TypeByName("Verse.MinifiedThing") ?? AccessTools.TypeByName("RimWorld.MinifiedThing");
            if (mt == null) yield break;

            var m1 = AccessTools.PropertyGetter(mt, "LabelNoCount");
            if (m1 != null) yield return m1;
            var m2 = AccessTools.PropertyGetter(mt, "LabelCapNoCount");
            if (m2 != null) yield return m2;
        }

        static Exception Finalizer(Exception __exception, ref string __result)
        {
            if (__exception != null)
            {
                __result = "minified thing";
                Log.WarningOnce($"[RuntimeStabilityFix] Suppressed MinifiedThing label exception: {__exception.GetType().Name}: {__exception.Message}", 0x77A1B004);
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(Log), nameof(Log.Error), new[] { typeof(string) })]
    internal static class Patch_SuppressCurl60Spam
    {
        static bool Prefix(ref string text)
        {
            if (string.IsNullOrEmpty(text)) return true;

            if (text.IndexOf("Curl error 60", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("SSL peer certificate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Cert verify failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WidgetRow), nameof(WidgetRow.ButtonIcon), new[] { typeof(Texture2D), typeof(string), typeof(Color?), typeof(Color?), typeof(Color?), typeof(bool), typeof(float) })]
    internal static class Patch_WidgetRowButtonIconNullTex
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(ref Texture2D tex)
        {
            if (tex == null)
            {
                tex = BaseContent.BadTex;
            }
        }
    }

    [HarmonyPatch]
    internal static class Patch_GrowerHarvestHasJobOnCellSafety
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var m = AccessTools.Method("RimWorld.WorkGiver_GrowerHarvest:HasJobOnCell");
            if (m != null) yield return m;
        }

        static Exception Finalizer(Exception __exception, ref bool __result)
        {
            if (__exception != null)
            {
                __result = false;
                Log.WarningOnce($"[RuntimeStabilityFix] Suppressed WorkGiver_GrowerHarvest.HasJobOnCell exception: {__exception.GetType().Name}: {__exception.Message}", 0x77A1B006);
            }
            return null;
        }
    }

    [HarmonyPatch]
    internal static class Patch_FloatMenuGetOptionsSafety
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var m = AccessTools.Method("RimWorld.FloatMenuMakerMap:GetOptions");
            if (m != null) yield return m;
        }

        static Exception Finalizer(Exception __exception, ref List<FloatMenuOption> __result)
        {
            if (__exception != null)
            {
                Log.WarningOnce($"[RuntimeStabilityFix] Suppressed FloatMenuMakerMap.GetOptions exception: {__exception.GetType().Name}: {__exception.Message}", 0x77A1B007);
                return null;
            }

            if (__result != null)
            {
                __result.RemoveAll(o => o == null);
            }
            return null;
        }
    }

    [HarmonyPatch]
    internal static class Patch_GhostGraphicForSafety
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var m = AccessTools.Method("Verse.GhostUtility:GhostGraphicFor");
            if (m != null) yield return m;
        }

        static Exception Finalizer(Exception __exception, Graphic baseGraphic, ThingDef thingDef, Color ghostCol, ThingDef stuff, ref Graphic __result)
        {
            if (__exception == null) return null;

            try
            {
                if (__result == null && baseGraphic != null)
                {
                    __result = baseGraphic.GetColoredVersion(baseGraphic.Shader, ghostCol, Color.white);
                }

                if (__result == null && thingDef?.graphicData != null)
                {
                    __result = thingDef.graphicData.Graphic;
                }
            }
            catch
            {
            }

            Log.WarningOnce($"[RuntimeStabilityFix] Suppressed GhostUtility.GhostGraphicFor exception: {__exception.GetType().Name}: {__exception.Message}", 0x77A1B008);
            return null;
        }
    }

    [HarmonyPatch]
    internal static class Patch_DeadlifeGasSustainerStability
    {
        private static readonly string[] SpewerTypes =
        {
            "VanillaQuestsExpandedDeadlife.DeadlifeGasSpewer",
            "VanillaQuestsExpandedDeadlife.RotstinkGasSpewer"
        };

        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var tn in SpewerTypes)
            {
                var t = AccessTools.TypeByName(tn);
                if (t == null) continue;

                var mTick = AccessTools.Method(t, "Tick");
                if (mTick != null) yield return mTick;

                var mStart = AccessTools.Method(t, "StartSustainer");
                if (mStart != null) yield return mStart;

                var mDestroy = AccessTools.Method(t, "Destroy");
                if (mDestroy != null) yield return mDestroy;
            }
        }

        static bool Prefix(MethodBase __originalMethod, object __instance)
        {
            if (__instance == null || __originalMethod == null) return true;

            var name = __originalMethod.Name;
            if (name == "StartSustainer")
            {
                try
                {
                    var f = AccessTools.Field(__instance.GetType(), "gasSustainer");
                    var s = f?.GetValue(__instance);
                    if (s != null)
                    {
                        var endedGetter = AccessTools.PropertyGetter(s.GetType(), "Ended");
                        var ended = endedGetter != null && (bool)endedGetter.Invoke(s, null);
                        if (!ended)
                        {
                            return false;
                        }
                    }
                }
                catch { }
            }
            else if (name == "Destroy")
            {
                try
                {
                    var f = AccessTools.Field(__instance.GetType(), "gasSustainer");
                    var s = f?.GetValue(__instance);
                    if (s != null)
                    {
                        AccessTools.Method(s.GetType(), "End")?.Invoke(s, null);
                    }
                }
                catch { }
            }

            return true;
        }

        static void Postfix(MethodBase __originalMethod, object __instance)
        {
            if (__instance == null || __originalMethod == null) return;
            if (__originalMethod.Name != "Tick") return;

            try
            {
                var f = AccessTools.Field(__instance.GetType(), "gasSustainer");
                var s = f?.GetValue(__instance);
                if (s == null) return;

                var endedGetter = AccessTools.PropertyGetter(s.GetType(), "Ended");
                var ended = endedGetter != null && (bool)endedGetter.Invoke(s, null);
                if (!ended)
                {
                    AccessTools.Method(s.GetType(), "Maintain")?.Invoke(s, null);
                }
            }
            catch { }
        }
    }
}
