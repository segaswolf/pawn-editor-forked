using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;

namespace PawnEditor;

/// <summary>
/// Support for "module" hediffs: pieces that do NOT go on a body part by themselves, but slot into an
/// already installed modular prosthetic (arm blade, dart gun, eye modules...).
///
/// Verified against Integrated Implants / EBSG Framework defs (workshop 3223443793, 1.6,
/// Mods/MedicalSystemExpansion2Absent/Defs/HediffDefs/Hediffs_EBSGModules.xml and
/// Hediffs_EBSGModularParts.xml):
///   HOST   = HediffDef with comp EBSGFramework.HediffCompProperties_Modular
///            -> slots[].slotID  (e.g. "LTS_ArmModuleSlot" on LTS_ModularBionicArm / LTS_ModularArchotechArm)
///   MODULE = ThingDef with comp EBSGFramework.CompProperties_UseEffectHediffModule
///            -> hediffs[]   (the hediff the module grants, e.g. LTS_ModuleArm_ArmBlade)
///            -> slotIDs[]   (the slots it needs, e.g. "LTS_ArmModuleSlot")
///            -> excludeIDs[] (modules it cannot coexist with)
/// The module def itself declares NO body part and NO surgery recipe, which is exactly why the picker
/// used to dump it on "whole body".
///
/// Everything here is read from DEF DATA through reflection by TYPE NAME, so we neither reference nor
/// require EBSG Framework: if the mod is absent, the map is simply empty and nothing changes.
/// </summary>
public static class ModularModulesCompat
{
    public class ModuleInfo
    {
        public readonly HashSet<string> SlotIds = new();
        public readonly List<HediffDef> Hosts = new();
        public readonly List<HediffDef> Conflicts = new();
    }

    private static Dictionary<HediffDef, ModuleInfo> modules;

    /// <summary>Builds the map once, lazily. Never throws: on failure the feature just stays off.</summary>
    private static void EnsureBuilt()
    {
        if (modules != null) return;
        modules = new Dictionary<HediffDef, ModuleInfo>();

        try
        {
            // 1) Which hediffs PROVIDE which slots.
            var slotProviders = new Dictionary<string, List<HediffDef>>();
            foreach (var hediffDef in DefDatabase<HediffDef>.AllDefsListForReading)
            {
                if (hediffDef.comps == null) continue;
                foreach (var comp in hediffDef.comps)
                {
                    if (comp == null || comp.GetType().Name != "HediffCompProperties_Modular") continue;
                    foreach (var slotId in ReadSlotIds(comp))
                    {
                        if (!slotProviders.TryGetValue(slotId, out var list))
                            slotProviders[slotId] = list = new List<HediffDef>();
                        if (!list.Contains(hediffDef)) list.Add(hediffDef);
                    }
                }
            }

            if (slotProviders.Count == 0) return;

            // 2) Which hediffs are MODULES, and which slots they need.
            foreach (var thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (thingDef.comps == null) continue;
                foreach (var comp in thingDef.comps)
                {
                    if (comp == null || comp.GetType().Name != "CompProperties_UseEffectHediffModule") continue;

                    var grantedHediffs = ReadList<HediffDef>(comp, "hediffs");
                    if (grantedHediffs.Count == 0) continue;

                    var slotIds = ReadList<string>(comp, "slotIDs");
                    var excludeIds = ReadList<string>(comp, "excludeIDs");

                    foreach (var granted in grantedHediffs)
                    {
                        if (granted == null) continue;
                        if (!modules.TryGetValue(granted, out var info))
                            modules[granted] = info = new ModuleInfo();

                        foreach (var slotId in slotIds)
                        {
                            if (slotId.NullOrEmpty() || !info.SlotIds.Add(slotId)) continue;
                            if (slotProviders.TryGetValue(slotId, out var hosts))
                                foreach (var host in hosts)
                                    if (!info.Hosts.Contains(host))
                                        info.Hosts.Add(host);
                        }

                        // excludeIDs are module ids; they match the module HediffDef defName in the
                        // defs we checked, so resolve them silently and skip whatever doesn't.
                        foreach (var excludeId in excludeIds)
                        {
                            if (excludeId.NullOrEmpty()) continue;
                            var conflict = DefDatabase<HediffDef>.GetNamedSilentFail(excludeId);
                            if (conflict != null && conflict != granted && !info.Conflicts.Contains(conflict))
                                info.Conflicts.Add(conflict);
                        }
                    }
                }
            }

            // A module with no resolvable host is useless to us: drop it so we don't block the user with
            // a requirement we cannot explain.
            foreach (var key in modules.Where(kv => kv.Value.Hosts.Count == 0).Select(kv => kv.Key).ToList())
                modules.Remove(key);
        }
        catch (Exception ex)
        {
            // Say it out loud instead of silently behaving differently.
            Log.Warning($"[Pawn Editor] Could not map modular module hediffs, falling back to the normal "
                        + $"body part resolution: {ex.Message}");
            modules = new Dictionary<HediffDef, ModuleInfo>();
        }
    }

    private static IEnumerable<string> ReadSlotIds(object compProps)
    {
        var slots = AccessTools.Field(compProps.GetType(), "slots")?.GetValue(compProps) as IEnumerable;
        if (slots == null) yield break;
        foreach (var slot in slots)
        {
            if (slot == null) continue;
            if (AccessTools.Field(slot.GetType(), "slotID")?.GetValue(slot) is string id && !id.NullOrEmpty())
                yield return id;
        }
    }

    private static List<T> ReadList<T>(object compProps, string fieldName)
    {
        var result = new List<T>();
        if (AccessTools.Field(compProps.GetType(), fieldName)?.GetValue(compProps) is IEnumerable raw)
            foreach (var entry in raw)
                if (entry is T typed)
                    result.Add(typed);
        return result;
    }

    /// <summary>Null if the hediff is a normal one; the requirement info if it is a slot-in module.</summary>
    public static ModuleInfo GetModule(HediffDef hediffDef)
    {
        if (hediffDef == null) return null;
        EnsureBuilt();
        return modules.TryGetValue(hediffDef, out var info) ? info : null;
    }

    /// <summary>The modular parts the pawn ALREADY has installed that can host this module.</summary>
    public static List<Hediff> HostsOnPawn(Pawn pawn, ModuleInfo info)
    {
        var result = new List<Hediff>();
        if (pawn?.health?.hediffSet?.hediffs == null || info == null) return result;
        foreach (var hediff in pawn.health.hediffSet.hediffs)
            if (hediff?.Part != null && info.Hosts.Contains(hediff.def))
                result.Add(hediff);
        return result;
    }

    /// <summary>Human readable "install one of these first" list, capped so the message stays readable.</summary>
    public static string RequirementLabel(ModuleInfo info)
    {
        if (info == null || info.Hosts.Count == 0) return "";
        var names = info.Hosts.Select(h => h.LabelCap.ToString()).Distinct().ToList();
        const int max = 4;
        return names.Count <= max
            ? names.ToCommaList(true)
            : string.Join(", ", names.Take(max)) + ", ...";
    }

    /// <summary>A conflicting module already sitting on the same part, if any.</summary>
    public static Hediff ConflictOnPart(Pawn pawn, ModuleInfo info, BodyPartRecord part)
    {
        if (pawn?.health?.hediffSet?.hediffs == null || info == null || info.Conflicts.Count == 0) return null;
        return pawn.health.hediffSet.hediffs
            .FirstOrDefault(h => h?.Part == part && info.Conflicts.Contains(h.def));
    }
}
