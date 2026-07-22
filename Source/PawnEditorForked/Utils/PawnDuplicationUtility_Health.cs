using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Health copy methods.
/// Covers the physical state of the pawn:
/// hediffs (injuries, implants, diseases), abilities, apparel and weapons.
/// </summary>
public static partial class PawnEditor
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Hediffs
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// SAFEGUARD, run after every duplication: compares the clone's health against the original's and
    /// reports both directions.
    ///
    /// Why this exists: duplication and blueprint save/load are two SEPARATE implementations of the
    /// same idea, kept in sync BY HAND (see the design rule at the top of PawnDuplicationUtility). Every
    /// time we fixed one side, the other quietly stayed behind: psycasts worked on duplicate but not on
    /// load, hediff clearing existed on duplicate but not on load, the bionic jaw bug was duplicate-only.
    /// This is the counterpart of AuditLoadedHediffs, so a divergence between the two paths shows up in
    /// the log by itself instead of arriving as a bug report about a clone with a destroyed arm.
    ///
    /// Purely informational: it never modifies either pawn.
    /// </summary>
    private static void AuditDuplicatedHediffs(Pawn source, Pawn clone)
    {
        try
        {
            if (source?.health?.hediffSet?.hediffs == null || clone?.health?.hediffSet?.hediffs == null) return;

            var expected = source.health.hediffSet.hediffs
                .Where(h => h?.def != null).Select(h => h.def.defName).ToList();
            var actual = clone.health.hediffSet.hediffs
                .Where(h => h?.def != null).Select(h => h.def.defName).ToList();

            // Count-aware on both sides: five missing fingers must not collapse into one entry.
            var pool = new List<string>(actual);
            var missing = expected.Where(defName => !pool.Remove(defName)).ToList();

            var expectedPool = new List<string>(expected);
            var extra = actual.Where(defName => !expectedPool.Remove(defName)).ToList();

            if (missing.Count > 0)
                Log.Warning($"[Pawn Editor] Duplicating '{source.LabelShortCap}': {missing.Count} hediff(s) did "
                            + $"NOT make it to the clone: {string.Join(", ", missing)}");

            if (extra.Count > 0)
                Log.Warning($"[Pawn Editor] Duplicating '{source.LabelShortCap}': the clone has {extra.Count} "
                            + $"hediff(s) the original does not: {string.Join(", ", extra)}");
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] AuditDuplicatedHediffs: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies hediffs that vanilla marks as safe to duplicate
    /// (hediff.def.duplicationAllowed == true).
    ///
    /// Non-organic implants (bionics) are handled separately via RestorePart
    /// rather than copying the hediff directly, because bionics work by
    /// removing the natural body part — not by adding a hediff on top of it.
    /// </summary>
    private static void CopyDup_Hediffs(Pawn src, Pawn dst)
    {
        if (src.health?.hediffSet == null || dst.health?.hediffSet == null) return;

        dst.health.hediffSet.hediffs.Clear();

        // Modules (arm blade, analyzer...) sit ON a modular prosthetic and are NOT implants, so they'd
        // be copied in this first loop and then wiped by the implants' RestorePart below. Defer them to
        // a third pass, after the host part exists. Same fix as the blueprint load path.
        var deferredModules = new List<Hediff>();

        foreach (var hediff in src.health.hediffSet.hediffs)
        {
            // duplicationAllowed=false is meant to stop RANDOM generation of a hediff, not to stop
            // duplicating a pawn that already has it. So still skip non-duplicable WHOLE-BODY/temporary
            // hediffs, but KEEP installed parts (attached to a body part and behaving like a prosthetic/
            // implant), so e.g. an "advanced bionic jaw" with duplicationAllowed=false transfers.
            var installedPart = hediff.Part != null
                && (hediff.def.countsAsAddedPartOrImplant || hediff.def.spawnThingOnRemoved != null || hediff.def.addedPartProps != null);
            if (!hediff.def.duplicationAllowed && !installedPart) continue;
            if (hediff.def == null || !DefDatabase<HediffDef>.AllDefsListForReading.Contains(hediff.def))
            {
                Log.Warning($"[Pawn Editor] Skipping missing hediff: {hediff.def?.defName ?? "null"}");
                continue;
            }
            // Skip parts that don't exist on the new pawn's body
            if (hediff.Part != null && !dst.health.hediffSet.HasBodyPart(hediff.Part)) continue;
            // Non-organic implants are restored below via RestorePart
            if ((hediff is Hediff_AddedPart || hediff is Hediff_Implant) && !hediff.def.organicAddedBodypart) continue;

            if (BionicModularityCompat.IsModule(hediff.def) || ModularModulesCompat.GetModule(hediff.def) != null)
            {
                deferredModules.Add(hediff);
                continue;
            }

            try
            {
                var copy = HediffMaker.MakeHediff(hediff.def, dst, hediff.Part);
                copy.CopyFrom(hediff);
                dst.health.hediffSet.AddDirect(copy);
            }
            catch (Exception ex) { Log.Warning($"[Pawn Editor] Skipping hediff {hediff.def?.defName}: {ex.Message}"); }
        }

        // Add non-organic implants and prosthetics to the clone.
        // CRITICAL: Sort by body part depth (deepest/leaf parts FIRST).
        // RestorePart on a parent part removes hediffs on child parts.
        // Without sorting, adding a skull implant would wipe already-added eye implants.
        var implants = src.health.hediffSet.hediffs
            .Where(h => (h is Hediff_AddedPart || h is Hediff_Implant) && !h.def.organicAddedBodypart && h.Part != null)
            .OrderByDescending(h => GetBodyPartDepth(h.Part))
            .ToList();

        foreach (var hediff in implants)
        {
            try
            {
                // Don't RestorePart if this part OR ANY OF ITS CHILD parts already has a placed implant.
                // RestorePart wipes child parts too, so restoring a parent (e.g. the neck's neural stack)
                // was silently deleting an already-added child implant (e.g. the bionic jaw on the head).
                bool wouldClobber = dst.health.hediffSet.hediffs.Any(h =>
                    (h is Hediff_AddedPart || h is Hediff_Implant) && h.Part != null && PartIsAtOrUnder(h.Part, hediff.Part));
                if (!wouldClobber)
                    dst.health.RestorePart(hediff.Part, null, checkStateChange: false);

                var copy = HediffMaker.MakeHediff(hediff.def, dst, hediff.Part);
                copy.Severity = hediff.Severity;
                dst.health.hediffSet.AddDirect(copy);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Pawn Editor] Failed to copy implant {hediff.def?.defName} on {hediff.Part?.Label}: {ex.Message}");
            }
        }

        // Third pass: modules, now that their host prosthetic exists and its RestorePart has run.
        foreach (var hediff in deferredModules)
        {
            try
            {
                if (hediff.Part != null && !dst.health.hediffSet.HasBodyPart(hediff.Part)) continue;
                var copy = HediffMaker.MakeHediff(hediff.def, dst, hediff.Part);
                copy.CopyFrom(hediff);
                dst.health.hediffSet.AddDirect(copy);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Pawn Editor] Failed to copy module {hediff.def?.defName} on {hediff.Part?.Label}: {ex.Message}");
            }
        }
    }

    /// <summary>True if <paramref name="part"/> is <paramref name="ancestor"/> or a descendant of it —
    /// i.e. a RestorePart(ancestor) would remove <paramref name="part"/>'s hediffs.</summary>
    private static bool PartIsAtOrUnder(BodyPartRecord part, BodyPartRecord ancestor)
    {
        for (var p = part; p != null; p = p.parent)
            if (p == ancestor) return true;
        return false;
    }

    /// <summary>
    /// Returns the depth of a body part in the body tree.
    /// Root = 0, torso = 1, arm = 2, hand = 3, finger = 4, etc.
    /// Used to sort implants leaf-first so RestorePart on parents
    /// doesn't wipe already-added child implants.
    /// </summary>
    private static int GetBodyPartDepth(BodyPartRecord part)
    {
        int depth = 0;
        var current = part;
        while (current?.parent != null)
        {
            depth++;
            current = current.parent;
        }
        return depth;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Abilities
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies abilities, then removes any that come from royal titles.
    /// Royal-title-granted abilities are re-added by CopyDup_RoyalTitles when
    /// the titles themselves are copied, so we strip them here to avoid doubles.
    /// </summary>
    private static void CopyDup_Abilities(Pawn src, Pawn dst)
    {
        if (src.abilities?.abilities == null || dst.abilities == null) return;

        // Clear all existing abilities on dst first to avoid loadID conflicts
        // PawnGenerator may have created abilities that would clash with the ones we're about to copy
        var existingAbilities = dst.abilities.abilities.Select(a => a.def).ToList();
        foreach (var abilityDef in existingAbilities)
            dst.abilities.RemoveAbility(abilityDef);

        // Add all abilities from src
        foreach (var ability in src.abilities.abilities)
            dst.abilities.GainAbility(ability.def);

        // Strip abilities granted by royal titles — CopyDup_RoyalTitles re-adds them
        if (src.royalty != null)
            foreach (var title in src.royalty.AllTitlesForReading)
                foreach (var granted in title.def.grantedAbilities)
                    if (dst.abilities.GetAbility(granted) != null)
                        dst.abilities.RemoveAbility(granted);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Apparel and Weapons
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies worn apparel and equipped weapons, preserving hit points,
    /// quality, lock state, and colorable comp colors.
    /// </summary>
    private static void CopyDup_Apparel(Pawn src, Pawn dst)
    {
        // ── Apparel ──
        if (src.apparel?.WornApparel != null && dst.apparel != null)
        {
            foreach (var worn in src.apparel.WornApparel.ToList())
            {
                if (worn?.def == null) continue;
                try
                {
                    var copy = (Apparel)ThingMaker.MakeThing(worn.def, worn.Stuff);
                    copy.HitPoints = worn.HitPoints;

                    if (worn.TryGetQuality(out var quality))
                        copy.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);

                    try
                    {
                        var srcColor = worn.TryGetComp<CompColorable>();
                        var dstColor = copy.TryGetComp<CompColorable>();
                        if (srcColor != null && dstColor != null && srcColor.Active)
                            dstColor.SetColor(srcColor.Color);
                    }
                    catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup apparel color: {ex.Message}"); }

                    dst.apparel.Wear(copy, dropReplacedApparel: false, locked: src.apparel.IsLocked(worn));
                }
                catch (Exception ex) { Log.Warning($"[Pawn Editor] Skipping apparel {worn.def?.defName}: {ex.Message}"); }
            }
        }

        // ── Equipment (weapons) ──
        if (src.equipment?.AllEquipmentListForReading != null && dst.equipment != null)
        {
            foreach (var equip in src.equipment.AllEquipmentListForReading.ToList())
            {
                if (equip?.def == null) continue;
                try
                {
                    var copy = (ThingWithComps)ThingMaker.MakeThing(equip.def, equip.Stuff);
                    copy.HitPoints = equip.HitPoints;

                    if (equip.TryGetQuality(out var quality))
                        copy.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);

                    dst.equipment.AddEquipment(copy);
                }
                catch (Exception ex) { Log.Warning($"[Pawn Editor] Skipping equipment {equip.def?.defName}: {ex.Message}"); }
            }
        }
    }
}
