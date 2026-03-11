using System;
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

        foreach (var hediff in src.health.hediffSet.hediffs)
        {
            if (!hediff.def.duplicationAllowed) continue;
            if (hediff.def == null || !DefDatabase<HediffDef>.AllDefsListForReading.Contains(hediff.def))
            {
                Log.Warning($"[Pawn Editor] Skipping missing hediff: {hediff.def?.defName ?? "null"}");
                continue;
            }
            // Skip parts that don't exist on the new pawn's body
            if (hediff.Part != null && !dst.health.hediffSet.HasBodyPart(hediff.Part)) continue;
            // Non-organic implants are restored below via RestorePart
            if ((hediff is Hediff_AddedPart || hediff is Hediff_Implant) && !hediff.def.organicAddedBodypart) continue;

            try
            {
                var copy = HediffMaker.MakeHediff(hediff.def, dst, hediff.Part);
                copy.CopyFrom(hediff);
                dst.health.hediffSet.AddDirect(copy);
            }
            catch (Exception ex) { Log.Warning($"[Pawn Editor] Skipping hediff {hediff.def?.defName}: {ex.Message}"); }
        }

        // Restore body parts that had non-organic implants (bionics, prosthetics)
        foreach (var hediff in src.health.hediffSet.hediffs)
        {
            if (hediff is Hediff_AddedPart && !hediff.def.organicAddedBodypart && hediff.Part != null)
            {
                try { dst.health.RestorePart(hediff.Part, null, checkStateChange: false); }
                catch (Exception ex)
                {
                    if (Prefs.DevMode) Log.Warning($"[Pawn Editor] RestorePart mismatch (safe): {ex.Message}");
                }
            }
        }
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

        // Add abilities present on src but missing on dst
        foreach (var ability in src.abilities.abilities)
            if (dst.abilities.GetAbility(ability.def) == null)
                dst.abilities.GainAbility(ability.def);

        // Remove abilities present on dst but not on src
        var dstAbilities = dst.abilities.abilities;
        for (int i = dstAbilities.Count - 1; i >= 0; i--)
            if (src.abilities.GetAbility(dstAbilities[i].def) == null)
                dst.abilities.RemoveAbility(dstAbilities[i].def);

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
