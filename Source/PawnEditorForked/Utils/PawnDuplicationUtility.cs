using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Stable pawn duplication for Pawn Editor Forked.
///
/// Split across four partial files — each with a single responsibility:
///
///   PawnDuplicationUtility.cs          (this file)
///     Orchestrator: public entry point, PawnGenerator request, finalization.
///
///   PawnDuplicationUtility_Identity.cs
///     Who the pawn IS: backstory, traits, genes, appearance, style, skills, needs.
///
///   PawnDuplicationUtility_Health.cs
///     Physical state: hediffs, abilities, apparel, weapons.
///
///   PawnDuplicationUtility_Social.cs
///     Place in the world: social relations (3-pass), work priorities,
///     royal titles, records, inventory.
///
/// Design rule: every CopyDup_* method must stay in sync with its SaveLoad
/// counterpart in PawnBlueprintSaveLoad.cs. Add a field to one → add it to the other.
/// </summary>
public static partial class PawnEditor
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Public entry point
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a stable duplicate of <paramref name="source"/> and returns it.
    /// Falls back to the vanilla Anomaly duplicator if the primary strategy fails,
    /// and returns <paramref name="source"/> unchanged as a last resort (no crash).
    /// </summary>
    public static Pawn CreateStableDuplicateOrSelf(Pawn source)
    {
        if (source == null) return null;

        Pawn duplicate = null;

        // Strategy 1: field-by-field copy — full control, no LoadID issues
        try
        {
            duplicate = DuplicatePawnStandalone(source);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Standalone duplication failed, trying Anomaly fallback. {ex.Message}");
        }

        // Strategy 2: vanilla Anomaly duplicator (requires Anomaly DLC)
        if (duplicate == null)
        {
            try
            {
                if (ModsConfig.AnomalyActive)
                {
                    var getter    = AccessTools.PropertyGetter(typeof(Find), "PawnDuplicator");
                    var duplicator = getter?.Invoke(null, null);
                    var method    = duplicator == null
                        ? null
                        : AccessTools.Method(duplicator.GetType(), "Duplicate", new[] { typeof(Pawn) });
                    duplicate = method?.Invoke(duplicator, new object[] { source }) as Pawn;
                }
            }
            catch (Exception ex) { Log.Warning($"[Pawn Editor] Anomaly duplicator also failed. {ex.Message}"); }
        }

        // Final fallback: return source unchanged — avoids a hard crash
        if (duplicate == null)
        {
            Log.Warning("[Pawn Editor] All duplication strategies failed, returning source pawn.");
            return source;
        }

        RemoveAnomalyDuplicateLink(duplicate);
        FinalizeSpawnState(duplicate);
        return duplicate;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Core generation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a fresh pawn via PawnGenerator matching the source's
    /// age/gender/xenotype/faction, then runs all CopyDup_* helpers.
    ///
    /// Copy order matters:
    ///   Genes first      — can force hair/skin/body type changes.
    ///   Appearance after — overrides those changes back to source values.
    ///   Ideo last        — other copies can trigger ideo recalculation.
    /// </summary>
    private static Pawn DuplicatePawnStandalone(Pawn source)
    {
        // Age: chronological must be >= biological (generator constraint)
        float bioAge   = source.ageTracker.AgeBiologicalYearsFloat;
        float chronAge = source.ageTracker.AgeChronologicalYearsFloat;
        if (chronAge < bioAge) chronAge = bioAge;

        // Xenotype — validated against active defs to survive mod list changes
        XenotypeDef    xenotype       = null;
        CustomXenotype customXenotype = null;
        if (ModsConfig.BiotechActive && source.genes != null)
        {
            xenotype       = source.genes.Xenotype;
            customXenotype = source.genes.CustomXenotype;
        }
        if (xenotype != null && !DefDatabase<XenotypeDef>.AllDefsListForReading.Contains(xenotype))
        {
            Log.Warning($"[Pawn Editor] Xenotype '{xenotype.defName}' not in active defs, falling back to Baseliner.");
            xenotype       = XenotypeDefOf.Baseliner;
            customXenotype = null;
        }

        var kindDef = source.kindDef ?? PawnKindDefOf.Colonist;
        var faction = source.Faction;
        var ideo    = ModsConfig.IdeologyActive ? source.Ideo : null;

        // forceGenerateNewPawn=true         → unique ThingID
        // canGeneratePawnRelations=false     → we copy relations in CopyDup_Relations
        // forceNoGear=true                  → we copy apparel/weapons in CopyDup_Apparel
        var request = new PawnGenerationRequest(
            kind: kindDef,
            faction: faction,
            context: PawnGenerationContext.NonPlayer,
            forceGenerateNewPawn: true,
            canGeneratePawnRelations: false,
            allowFood: true,
            allowAddictions: true,
            fixedBiologicalAge: bioAge,
            fixedChronologicalAge: chronAge,
            fixedGender: source.gender,
            fixedIdeo: ideo,
            forbidAnyTitle: true,
            forceNoGear: true
        );
        request.ForcedXenotype       = xenotype;
        request.ForcedCustomXenotype = customXenotype;
        request.ForceNoIdeoGear      = true;
        request.CanGeneratePawnRelations = false;

        Pawn newPawn = PawnGenerator.GeneratePawn(request);

        // Some xenotype generators override fixedGender — force it back
        if (newPawn.gender != source.gender)
            newPawn.gender = source.gender;

        // Clones keep the original name (vanilla Obelisk behavior)
        if (source.Name != null)
            newPawn.Name = NameTriple.FromString(source.Name.ToString());

        // Biotech fields not covered by the generation request
        if (ModsConfig.BiotechActive && source.genes != null && newPawn.genes != null)
        {
            newPawn.ageTracker.growthPoints = source.ageTracker.growthPoints;
            newPawn.ageTracker.vatGrowTicks = source.ageTracker.vatGrowTicks;
            newPawn.genes.xenotypeName      = source.genes.xenotypeName;
            newPawn.genes.iconDef           = source.genes.iconDef;
        }

        // --- Copy all attributes (implementations in the other partial files) ---
        CopyDup_StoryAndTraits(source, newPawn);   // Identity
        CopyDup_Genes(source, newPawn);            // Identity — must be before Appearance
        CopyDup_Appearance(source, newPawn);       // Identity — overrides gene-forced visuals
        CopyDup_Style(source, newPawn);            // Identity
        CopyDup_Skills(source, newPawn);           // Identity
        CopyDup_Hediffs(source, newPawn);          // Health
        CopyDup_Needs(source, newPawn);            // Identity (non-social memories)
        CopyDup_Abilities(source, newPawn);        // Health
        CopyDup_Apparel(source, newPawn);          // Health
        CopyDup_WorkPriorities(source, newPawn);   // Social
        CopyDup_Relations(source, newPawn);        // Social (social memories included)
        CopyDup_RoyalTitles(source, newPawn);      // Social
        CopyDup_Records(source, newPawn);          // Social
        CopyDup_Inventory(source, newPawn);        // Social
        FacialAnimCompat.CopyFacialData(source, newPawn);

        // Miscellaneous fields too small for their own CopyDup_ method
        if (source.guest != null && newPawn.guest != null)
            newPawn.guest.Recruitable = source.guest.Recruitable;
        if (source.story?.favoriteColor != null)
            newPawn.story.favoriteColor = source.story.favoriteColor;

        // Ideo certainty — MUST be last: other copies trigger ideo recalculation
        if (ModsConfig.IdeologyActive && source.ideo != null && newPawn.ideo != null)
        {
            try
            {
                if (source.Ideo != null) newPawn.ideo.SetIdeo(source.Ideo);
                newPawn.ideo.certaintyInt = source.ideo.Certainty;
            }
            catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup ideo certainty: {ex.Message}"); }
        }

        return newPawn;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Finalization helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes the Anomaly duplicate-tracker link from the clone so the game
    /// does not apply DuplicateSickness or other Anomaly mechanics to it.
    /// </summary>
    private static void RemoveAnomalyDuplicateLink(Pawn pawn)
    {
        if (pawn == null) return;
        try
        {
            // duplicateOf = int.MinValue signals "no original" to the Anomaly system
            var trackerField     = AccessTools.Field(typeof(Pawn), "duplicate");
            var tracker          = trackerField?.GetValue(pawn);
            var duplicateOfField = tracker == null ? null : AccessTools.Field(tracker.GetType(), "duplicateOf");
            duplicateOfField?.SetValue(tracker, int.MinValue);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] ClearDuplicateTracker: {ex.Message}"); }

        try
        {
            var sickness = pawn.health?.hediffSet?.hediffs?.FirstOrDefault(h =>
                h?.def?.defName?.IndexOf("DuplicateSickness", StringComparison.OrdinalIgnoreCase) >= 0);
            if (sickness != null) pawn.health.RemoveHediff(sickness);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] RemoveDuplicateSickness: {ex.Message}"); }
    }

    /// <summary>
    /// Called after all CopyDup_* operations complete.
    /// Triggers work-type recalculation and graphics initialization.
    /// </summary>
    private static void FinalizeSpawnState(Pawn pawn)
    {
        if (pawn == null) return;
        try { pawn.Notify_DisabledWorkTypesChanged(); } catch { }
        try { EnsurePawnGraphicsInitialized(pawn); }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] EnsurePawnGraphicsInitialized: {ex.Message}"); }
    }

    /// <summary>
    /// Appends an index suffix to avoid name collisions with existing pawns.
    /// Currently DISABLED — vanilla Obelisk behavior keeps the original name.
    /// Re-enable by calling from DuplicatePawnStandalone if unique names are needed.
    /// </summary>
    private static void EnsureUniqueCloneName(Pawn pawn)
    {
        if (pawn?.Name == null) return;
        string baseName  = pawn.Name.ToStringFull;
        string candidate = baseName;
        int    index     = 2;
        while (PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead
               .Any(p => p != pawn && p.Name != null && p.Name.ToStringFull == candidate))
            candidate = $"{baseName} {index++}";
        if (candidate != baseName)
        {
            pawn.Name = NameTriple.FromString(candidate);
            Log.Warning($"[Pawn Editor] Name collision resolved: clone renamed to '{candidate}'.");
        }
    }
}
