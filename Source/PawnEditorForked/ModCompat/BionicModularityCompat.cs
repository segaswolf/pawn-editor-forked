using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;

namespace PawnEditor;

/// <summary>
/// Support for Bionic Modularity (MrKociak.YABEMBionicModularity), which is a SECOND and completely
/// different modular system from the EBSG one handled in <see cref="ModularModulesCompat" />.
///
/// Verified in the decompiled assembly (C:\Git\Dev Notes\BionicModularity_decompiled):
///   HOST   = any HediffDef carrying the modExtension BionicModularity.DefExtension_ModularHediff
///            { isModular = true }.
///   MODULE = a hediff added by a recipe whose worker is BionicModularity.Recipe_InstallModule. That
///            worker's GetPartsToApplyOn only offers a part if some hediff ON THAT PART carries the
///            extension, and the part is listed in the recipe's appliedOnFixedBodyParts.
/// No slots involved: slots are EBSG's model, not this one.
///
/// It also replaces, more generally, what the community patch "Compatibility Patch for Integrated
/// Implants and Bionic Modularity" (Blonc.ImplantModularityPatch) does in XML. See
/// <see cref="MarkModularPartsAsHosts" /> for the what and the why.
///
/// Everything is reflection by type name: with Bionic Modularity absent, this whole class is inert.
/// </summary>
[StaticConstructorOnStartup]
public static class BionicModularityCompat
{
    private const string ExtensionTypeName = "BionicModularity.DefExtension_ModularHediff";
    private const string InstallWorkerName = "BionicModularity.Recipe_InstallModule";
    private const string PatchPackageId = "Blonc.ImplantModularityPatch";

    private static readonly Type ExtensionType = AccessTools.TypeByName(ExtensionTypeName);

    // Module hediff -> the body parts its own install surgery targets. Recipe_InstallModule requires
    // BOTH a modular host on the part AND that the part is listed in appliedOnFixedBodyParts. I only
    // checked the host at first, which is why a "power fist module" was being offered on a modular JAW:
    // the jaw IS a valid host, it just isn't a place a fist goes.
    private static Dictionary<HediffDef, List<BodyPartDef>> modules;

    public static bool Active => ExtensionType != null;

    static BionicModularityCompat()
    {
        MarkModularPartsAsHosts();
    }

    /// <summary>
    /// Makes modular prosthetics from OTHER modular frameworks count as valid install sites for Bionic
    /// Modularity's modules.
    ///
    /// The community XML patch does this by hardcoding one defName: it adds the extension to Integrated
    /// Implants' abstract LTS_ModularBodyPartBase, so every "(modular)" limb inherits it. Ours keys off
    /// the CAPABILITY instead of the name: any hediff that declares EBSG modular slots is, by
    /// definition, a modular part, so it gets marked. That covers Integrated Implants the same way and
    /// also every other mod using that framework, present or future, without a list to maintain.
    ///
    /// If the player already has the community patch installed we do NOTHING: their patch is the
    /// authority and doubling up would be noise.
    /// </summary>
    private static void MarkModularPartsAsHosts()
    {
        if (!Active) return;

        if (ModsConfig.IsActive(PatchPackageId))
        {
            Log.Message("[Pawn Editor] Bionic Modularity compatibility patch detected, leaving modular "
                        + "part marking to it.");
            return;
        }

        try
        {
            var marked = 0;
            foreach (var hediffDef in DefDatabase<HediffDef>.AllDefsListForReading)
            {
                if (!DeclaresModularSlots(hediffDef) || HasHostExtension(hediffDef)) continue;

                var extension = (DefModExtension)Activator.CreateInstance(ExtensionType);
                AccessTools.Field(ExtensionType, "isModular")?.SetValue(extension, true);

                hediffDef.modExtensions ??= new List<DefModExtension>();
                hediffDef.modExtensions.Add(extension);
                marked++;
            }

            if (marked > 0)
                Log.Message($"[Pawn Editor] Marked {marked} modular prosthetic(s) as valid Bionic Modularity "
                            + "install sites. Install the community compatibility patch if you would rather "
                            + "that mod handled it.");
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Could not mark modular parts for Bionic Modularity: {ex.Message}");
        }
    }

    /// <summary>A hediff that publishes EBSG modular slots: that is what makes it a modular part.</summary>
    private static bool DeclaresModularSlots(HediffDef hediffDef) =>
        hediffDef?.comps != null
        && hediffDef.comps.Any(c => c != null && c.GetType().Name == "HediffCompProperties_Modular");

    private static bool HasHostExtension(HediffDef hediffDef) =>
        hediffDef?.modExtensions != null
        && hediffDef.modExtensions.Any(e => e != null && ExtensionType.IsInstanceOfType(e));

    /// <summary>True if this hediff, once installed, can host Bionic Modularity modules.</summary>
    public static bool IsHost(HediffDef hediffDef)
    {
        if (!Active || hediffDef?.modExtensions == null) return false;
        foreach (var extension in hediffDef.modExtensions)
        {
            if (extension == null || !ExtensionType.IsInstanceOfType(extension)) continue;
            if (AccessTools.Field(ExtensionType, "isModular")?.GetValue(extension) is bool isModular && !isModular) continue;
            return true;
        }
        return false;
    }

    private static void EnsureModulesMapped()
    {
        if (modules != null) return;
        modules = new Dictionary<HediffDef, List<BodyPartDef>>();

        foreach (var recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
        {
            if (recipe?.addsHediff == null || recipe.workerClass?.FullName != InstallWorkerName) continue;

            if (!modules.TryGetValue(recipe.addsHediff, out var parts))
                modules[recipe.addsHediff] = parts = new List<BodyPartDef>();

            if (recipe.appliedOnFixedBodyParts == null) continue;
            foreach (var partDef in recipe.appliedOnFixedBodyParts)
                if (partDef != null && !parts.Contains(partDef))
                    parts.Add(partDef);
        }
    }

    /// <summary>True if this hediff is a Bionic Modularity module (needs a host part to be installed).</summary>
    public static bool IsModule(HediffDef hediffDef)
    {
        if (!Active || hediffDef == null) return false;
        EnsureModulesMapped();
        return modules.ContainsKey(hediffDef);
    }

    /// <summary>True if the pawn has ANY modular host part, regardless of which module we're placing.</summary>
    public static bool HasAnyHost(Pawn pawn)
    {
        if (!Active || pawn?.health?.hediffSet?.hediffs == null) return false;
        foreach (var hediff in pawn.health.hediffSet.hediffs)
            if (hediff?.Part != null && IsHost(hediff.def))
                return true;
        return false;
    }

    /// <summary>
    /// The body parts this module is meant for, spelled out. The first version of the "needs a host"
    /// message just said "install a modular arm, leg, eye and so on", which is useless: a player looking
    /// at a pawn that already HAS a modular eye reads it as a bug, when the module actually wants a
    /// modular nose or stomach. Name the part and the message becomes actionable.
    /// </summary>
    public static string RequiredPartsLabel(HediffDef moduleDef)
    {
        EnsureModulesMapped();
        if (moduleDef == null || !modules.TryGetValue(moduleDef, out var partDefs) || partDefs.NullOrEmpty())
            return "";

        return partDefs.Select(p => p.label.NullOrEmpty() ? p.defName : p.label)
            .Distinct().ToCommaList(false);
    }

    /// <summary>
    /// Where this specific module can go: a part that carries a modular host AND that the module's own
    /// surgery actually targets. Both halves matter, exactly like Recipe_InstallModule checks them.
    /// </summary>
    public static List<BodyPartRecord> PartsWithHost(Pawn pawn, HediffDef moduleDef)
    {
        var result = new List<BodyPartRecord>();
        if (!Active || pawn?.health?.hediffSet?.hediffs == null) return result;

        EnsureModulesMapped();
        modules.TryGetValue(moduleDef, out var allowedPartDefs);

        foreach (var hediff in pawn.health.hediffSet.hediffs)
        {
            if (hediff?.Part == null || !IsHost(hediff.def) || result.Contains(hediff.Part)) continue;
            // A recipe with no fixed parts declared means "anywhere there's a host", so only filter
            // when the module actually says where it belongs.
            if (!allowedPartDefs.NullOrEmpty() && !allowedPartDefs.Contains(hediff.Part.def)) continue;
            result.Add(hediff.Part);
        }

        return result;
    }
}
