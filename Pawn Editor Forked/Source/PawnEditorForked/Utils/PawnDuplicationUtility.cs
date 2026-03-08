using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Clean pawn duplication based on the vanilla Obelisk approach (GameComponent_PawnDuplicator).
/// Generates a fresh pawn via PawnGenerator and copies attributes one by one.
/// No serialization/deserialization → no broken LoadIDs, no orphaned map references.
/// Works with or without any DLC.
/// </summary>
public static partial class PawnEditor
{
    /// <summary>
    /// Creates a stable duplicate of the source pawn. The duplicate is a brand-new pawn
    /// with a unique ThingID but identical story, traits, genes, skills, appearance, etc.
    /// If the source pawn itself is being loaded for the first time (not a clone operation),
    /// returns source unchanged.
    /// </summary>
    public static Pawn CreateStableDuplicateOrSelf(Pawn source)
    {
        if (source == null) return null;

        Pawn duplicate = null;

        // Strategy 1: Standalone duplication (full control over what gets copied)
        try
        {
            duplicate = DuplicatePawnStandalone(source);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Standalone duplication failed, trying Anomaly fallback. {ex.Message}");
        }

        // Strategy 2: Try vanilla Anomaly duplicator as fallback
        if (duplicate == null)
        {
            try
            {
                if (ModsConfig.AnomalyActive)
                {
                    var pawnDuplicatorGetter = AccessTools.PropertyGetter(typeof(Find), "PawnDuplicator");
                    var pawnDuplicator = pawnDuplicatorGetter?.Invoke(null, null);
                    var duplicateMethod = pawnDuplicator == null
                        ? null
                        : AccessTools.Method(pawnDuplicator.GetType(), "Duplicate", new[] { typeof(Pawn) });
                    duplicate = duplicateMethod?.Invoke(pawnDuplicator, new object[] { source }) as Pawn;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Pawn Editor] Anomaly duplicator also failed. {ex.Message}");
            }
        }

        // Final fallback: return source as-is (not ideal but prevents crash)
        if (duplicate == null)
        {
            Log.Warning("[Pawn Editor] All duplication strategies failed, returning source pawn.");
            return source;
        }

        RemoveAnomalyDuplicateLink(duplicate);
        FinalizeSpawnState(duplicate);
        return duplicate;
    }

    // ─────────────────────────────────────────────────────────────
    //  Core duplication — mirrors GameComponent_PawnDuplicator.Duplicate()
    // ─────────────────────────────────────────────────────────────

    private static Pawn DuplicatePawnStandalone(Pawn source)
    {
        // ── Build generation request (clean pawn, no gear, no relations) ──
        float bioAge = source.ageTracker.AgeBiologicalYearsFloat;
        float chronAge = source.ageTracker.AgeChronologicalYearsFloat;
        if (chronAge < bioAge) chronAge = bioAge;

        XenotypeDef xenotype = null;
        CustomXenotype customXenotype = null;
        if (ModsConfig.BiotechActive && source.genes != null)
        {
            xenotype = source.genes.Xenotype;
            customXenotype = source.genes.CustomXenotype;
        }

        // ── Validate xenotype (may come from a mod that's no longer active) ──
        if (xenotype != null && !DefDatabase<XenotypeDef>.AllDefsListForReading.Contains(xenotype))
        {
            Log.Warning($"[Pawn Editor] Xenotype '{xenotype.defName}' not found in active defs, falling back to Baseliner.");
            xenotype = XenotypeDefOf.Baseliner;
            customXenotype = null;
        }

        // ── Validate kindDef (may come from a mod no longer active) ──
        var kindDef = source.kindDef ?? PawnKindDefOf.Colonist;

        // ── Validate faction ──
        var faction = source.Faction;

        // ── Validate ideo ──
        Ideo ideo = null;
        if (ModsConfig.IdeologyActive)
            ideo = source.Ideo; // null is fine — generator handles it

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
        request.ForcedXenotype = xenotype;
        request.ForcedCustomXenotype = customXenotype;
        request.ForceNoIdeoGear = true;
        request.CanGeneratePawnRelations = false;

        Pawn newPawn = PawnGenerator.GeneratePawn(request);

        // Force gender (PawnGenerator may override fixedGender for some xenotypes)
        if (newPawn.gender != source.gender)
            newPawn.gender = source.gender;

        // ── Copy name ──
        if (source.Name != null)
            newPawn.Name = NameTriple.FromString(source.Name.ToString());

        // ── Biotech-specific fields ──
        if (ModsConfig.BiotechActive && source.genes != null && newPawn.genes != null)
        {
            newPawn.ageTracker.growthPoints = source.ageTracker.growthPoints;
            newPawn.ageTracker.vatGrowTicks = source.ageTracker.vatGrowTicks;
            newPawn.genes.xenotypeName = source.genes.xenotypeName;
            newPawn.genes.iconDef = source.genes.iconDef;
        }

        // ── Copy everything ──
        CopyDup_StoryAndTraits(source, newPawn);
        CopyDup_Genes(source, newPawn);        // Genes first — they can force hair/body/skin
        CopyDup_Appearance(source, newPawn);   // Appearance after genes to override back
        CopyDup_Style(source, newPawn);
        CopyDup_Skills(source, newPawn);
        CopyDup_Hediffs(source, newPawn);
        CopyDup_Needs(source, newPawn);
        CopyDup_Abilities(source, newPawn);
        CopyDup_Apparel(source, newPawn);
        CopyDup_WorkPriorities(source, newPawn);
        CopyDup_Relations(source, newPawn);
        CopyDup_RoyalTitles(source, newPawn);
        CopyDup_Records(source, newPawn);
        CopyDup_Inventory(source, newPawn);
        FacialAnimCompat.CopyFacialData(source, newPawn);

        // ── Guest status ──
        if (source.guest != null && newPawn.guest != null)
            newPawn.guest.Recruitable = source.guest.Recruitable;

        // ── Favorite color (safe Def reference, not mutation) ──
        if (source.story?.favoriteColor != null)
            newPawn.story.favoriteColor = source.story.favoriteColor;

        // ── Ideo certainty (MUST be last — other copy steps can trigger ideo recalculation) ──
        if (ModsConfig.IdeologyActive && source.ideo != null && newPawn.ideo != null)
        {
            try
            {
                if (source.Ideo != null)
                    newPawn.ideo.SetIdeo(source.Ideo);
                newPawn.ideo.certaintyInt = source.ideo.Certainty;
            }
            catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup ideo certainty: {ex.Message}"); }
        }

        // Clones keep the same name as the original (vanilla Obelisk behavior)
        // EnsureUniqueCloneName(newPawn); // Disabled: clones are clones

        return newPawn;
    }

    // ─────────────────────────────────────────────────────────────
    //  Copy helpers — prefixed CopyDup_ to avoid collision
    // ─────────────────────────────────────────────────────────────

    private static void CopyDup_StoryAndTraits(Pawn src, Pawn dst)
    {
        if (src.story == null || dst.story == null) return;

        dst.story.Childhood = src.story.Childhood; // null-safe, game handles missing backstories
        dst.story.Adulthood = src.story.Adulthood; // null is valid for children

        dst.story.traits.allTraits.Clear();
        foreach (var trait in src.story.traits.allTraits)
        {
            // Skip gene-granted traits — CopyDup_Genes will re-add them
            if (ModsConfig.BiotechActive && trait.sourceGene != null) continue;
            if (trait?.def == null || !DefDatabase<TraitDef>.AllDefsListForReading.Contains(trait.def))
            {
                Log.Warning($"[Pawn Editor] Skipping missing trait: {trait?.def?.defName ?? "null"}");
                continue;
            }
            dst.story.traits.GainTrait(new Trait(trait.def, trait.Degree, trait.ScenForced));
        }
    }

    private static void CopyDup_Genes(Pawn src, Pawn dst)
    {
        if (!ModsConfig.BiotechActive) return;
        if (src.genes == null || dst.genes == null) return;

        // Xenogenes
        dst.genes.Xenogenes.Clear();
        var srcXeno = src.genes.Xenogenes;
        foreach (var gene in srcXeno)
        {
            if (gene?.def == null || !DefDatabase<GeneDef>.AllDefsListForReading.Contains(gene.def))
            {
                Log.Warning($"[Pawn Editor] Skipping missing xenogene: {gene?.def?.defName ?? "null"}");
                continue;
            }
            dst.genes.AddGene(gene.def, xenogene: true);
        }

        for (int i = 0; i < srcXeno.Count && i < dst.genes.Xenogenes.Count; i++)
        {
            var dstGene = dst.genes.Xenogenes[i];
            if (srcXeno[i].Overridden)
            {
                var overriderDef = srcXeno[i].overriddenByGene?.def;
                dstGene.overriddenByGene = overriderDef != null
                    ? dst.genes.GenesListForReading.FirstOrDefault(e => e.def == overriderDef)
                    : null;
            }
            else
                dstGene.overriddenByGene = null;
        }

        // Endogenes
        dst.genes.Endogenes.Clear();
        var srcEndo = src.genes.Endogenes;
        foreach (var gene in srcEndo)
        {
            if (gene?.def == null || !DefDatabase<GeneDef>.AllDefsListForReading.Contains(gene.def))
            {
                Log.Warning($"[Pawn Editor] Skipping missing endogene: {gene?.def?.defName ?? "null"}");
                continue;
            }
            dst.genes.AddGene(gene.def, xenogene: false);
        }

        for (int i = 0; i < srcEndo.Count && i < dst.genes.Endogenes.Count; i++)
        {
            var dstGene = dst.genes.Endogenes[i];
            if (srcEndo[i].Overridden)
            {
                var overriderDef = srcEndo[i].overriddenByGene?.def;
                dstGene.overriddenByGene = overriderDef != null
                    ? dst.genes.GenesListForReading.FirstOrDefault(e => e.def == overriderDef)
                    : null;
            }
            else
                dstGene.overriddenByGene = null;
        }
    }

    private static void CopyDup_Appearance(Pawn src, Pawn dst)
    {
        if (src.story == null || dst.story == null) return;

        dst.story.headType = src.story.headType;
        dst.story.bodyType = src.story.bodyType;
        dst.story.hairDef = src.story.hairDef;
        dst.story.HairColor = src.story.HairColor;
        dst.story.SkinColorBase = src.story.SkinColorBase;
        dst.story.skinColorOverride = src.story.skinColorOverride;
        dst.story.furDef = src.story.furDef;

        // Copy melanin (affects skin color calculation)
        try { dst.story.melanin = src.story.melanin; } catch { }

        // Force graphics recalculation after all appearance changes
        try
        {
            dst.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(dst);
            GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(dst);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup graphics refresh (appearance): {ex.Message}"); }
    }

    private static void CopyDup_Style(Pawn src, Pawn dst)
    {
        if (src.style == null || dst.style == null) return;

        dst.style.beardDef = src.style.beardDef;
        if (ModsConfig.IdeologyActive)
        {
            dst.style.BodyTattoo = src.style.BodyTattoo;
            dst.style.FaceTattoo = src.style.FaceTattoo;
        }

        // Force graphics refresh after style changes (tattoos, beard)
        try
        {
            dst.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(dst);
            GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(dst);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup graphics refresh (style): {ex.Message}"); }
    }

    private static void CopyDup_Skills(Pawn src, Pawn dst)
    {
        if (src.skills == null || dst.skills == null) return;

        dst.skills.skills.Clear();
        foreach (var skill in src.skills.skills)
        {
            var copy = new SkillRecord(dst, skill.def)
            {
                levelInt = skill.levelInt,
                passion = skill.passion,
                xpSinceLastLevel = skill.xpSinceLastLevel,
                xpSinceMidnight = skill.xpSinceMidnight
            };
            dst.skills.skills.Add(copy);
        }
    }

    private static void CopyDup_Hediffs(Pawn src, Pawn dst)
    {
        if (src.health?.hediffSet == null || dst.health?.hediffSet == null) return;

        dst.health.hediffSet.hediffs.Clear();

        foreach (var hediff in src.health.hediffSet.hediffs)
        {
            // Only copy hediffs that vanilla considers safe to duplicate
            if (!hediff.def.duplicationAllowed) continue;
            if (hediff.def == null || !DefDatabase<HediffDef>.AllDefsListForReading.Contains(hediff.def))
            {
                Log.Warning($"[Pawn Editor] Skipping missing hediff: {hediff.def?.defName ?? "null"}");
                continue;
            }
            if (hediff.Part != null && !dst.health.hediffSet.HasBodyPart(hediff.Part)) continue;
            // Skip non-organic implants (bionics etc. — they get restored below)
            if ((hediff is Hediff_AddedPart || hediff is Hediff_Implant) && !hediff.def.organicAddedBodypart) continue;

            try
            {
                var copy = HediffMaker.MakeHediff(hediff.def, dst, hediff.Part);
                copy.CopyFrom(hediff);
                dst.health.hediffSet.AddDirect(copy);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Pawn Editor] Skipping hediff {hediff.def?.defName}: {ex.Message}");
            }
        }

        // Restore body parts that had non-organic implants (bionics)
        foreach (var hediff in src.health.hediffSet.hediffs)
        {
            if (hediff is Hediff_AddedPart && !hediff.def.organicAddedBodypart && hediff.Part != null)
            {
                try { dst.health.RestorePart(hediff.Part, null, checkStateChange: false); }
                catch (Exception ex) { if (Verse.Prefs.DevMode) Log.Warning($"[Pawn Editor] RestorePart mismatch (safe): {ex.Message}"); }
            }
        }
    }

    private static void CopyDup_Needs(Pawn src, Pawn dst)
    {
        if (src.needs == null || dst.needs == null) return;

        try
        {
            dst.needs.AllNeeds.Clear();
            foreach (var srcNeed in src.needs.AllNeeds)
            {
                if (srcNeed?.def?.needClass == null) continue;
                try
                {
                    var need = (Need)Activator.CreateInstance(srcNeed.def.needClass, dst);
                    need.def = srcNeed.def;
                    dst.needs.AllNeeds.Add(need);
                    need.SetInitialLevel();
                    need.CurLevel = srcNeed.CurLevel;
                    dst.needs.BindDirectNeedFields();
                }
                catch
                {
                    // Some needs may not be constructable in current context — skip
                }
            }

            // Copy thought memories
            if (src.needs.mood?.thoughts?.memories != null && dst.needs.mood?.thoughts?.memories != null)
            {
                var dstMemories = dst.needs.mood.thoughts.memories.Memories;
                dstMemories.Clear();
                foreach (var memory in src.needs.mood.thoughts.memories.Memories)
                {
                    if (memory?.def == null) continue;
                    try
                    {
                        var copy = (Thought_Memory)ThoughtMaker.MakeThought(memory.def);
                        copy.CopyFrom(memory);
                        copy.pawn = dst;
                        dstMemories.Add(copy);
                    }
                    catch (Exception ex) { if (Verse.Prefs.DevMode) Log.Warning($"[Pawn Editor] CopyDup thought memory (safe): {ex.Message}"); }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Partial needs copy: {ex.Message}");
        }
    }

    private static void CopyDup_Abilities(Pawn src, Pawn dst)
    {
        if (src.abilities?.abilities == null || dst.abilities == null) return;

        // Add missing abilities
        foreach (var ability in src.abilities.abilities)
        {
            if (dst.abilities.GetAbility(ability.def) == null)
                dst.abilities.GainAbility(ability.def);
        }

        // Remove extra abilities
        var dstAbilities = dst.abilities.abilities;
        for (int i = dstAbilities.Count - 1; i >= 0; i--)
        {
            if (src.abilities.GetAbility(dstAbilities[i].def) == null)
                dst.abilities.RemoveAbility(dstAbilities[i].def);
        }

        // Remove abilities that come from royal titles (they belong to the source, not clone)
        if (src.royalty != null)
        {
            foreach (var title in src.royalty.AllTitlesForReading)
            {
                foreach (var grantedAbility in title.def.grantedAbilities)
                {
                    if (dst.abilities.GetAbility(grantedAbility) != null)
                        dst.abilities.RemoveAbility(grantedAbility);
                }
            }
        }
    }

    private static void CopyDup_Apparel(Pawn src, Pawn dst)
    {
        // Copy apparel
        if (src.apparel?.WornApparel != null && dst.apparel != null)
        {
            var srcApparel = src.apparel.WornApparel.ToList();
            foreach (var worn in srcApparel)
            {
                if (worn?.def == null) continue;
                try
                {
                    var copy = (Apparel)ThingMaker.MakeThing(worn.def, worn.Stuff);
                    copy.HitPoints = worn.HitPoints;
                    if (worn.TryGetQuality(out var quality))
                    {
                        var compQuality = copy.TryGetComp<CompQuality>();
                        compQuality?.SetQuality(quality, ArtGenerationContext.Outsider);
                    }
                    try
                    {
                        var srcColorComp = worn.TryGetComp<CompColorable>();
                        var dstColorComp = copy.TryGetComp<CompColorable>();
                        if (srcColorComp != null && dstColorComp != null && srcColorComp.Active)
                            dstColorComp.SetColor(srcColorComp.Color);
                    }
                    catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup apparel color: {ex.Message}"); }
                    dst.apparel.Wear(copy, dropReplacedApparel: false, locked: src.apparel.IsLocked(worn));
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Pawn Editor] Skipping apparel {worn.def?.defName}: {ex.Message}");
                }
            }
        }

        // Copy equipment (weapons)
        if (src.equipment?.AllEquipmentListForReading != null && dst.equipment != null)
        {
            var srcEquip = src.equipment.AllEquipmentListForReading.ToList();
            foreach (var equip in srcEquip)
            {
                if (equip?.def == null) continue;
                try
                {
                    var copy = (ThingWithComps)ThingMaker.MakeThing(equip.def, equip.Stuff);
                    copy.HitPoints = equip.HitPoints;
                    if (equip.TryGetQuality(out var quality))
                    {
                        var compQuality = copy.TryGetComp<CompQuality>();
                        compQuality?.SetQuality(quality, ArtGenerationContext.Outsider);
                    }
                    dst.equipment.AddEquipment(copy);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Pawn Editor] Skipping equipment {equip.def?.defName}: {ex.Message}");
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Cleanup helpers
    // ─────────────────────────────────────────────────────────────

    private static void RemoveAnomalyDuplicateLink(Pawn pawn)
    {
        if (pawn == null) return;

        // Clear duplicate tracker so the clone isn't treated as an Anomaly duplicate
        try
        {
            var trackerField = AccessTools.Field(typeof(Pawn), "duplicate");
            var tracker = trackerField?.GetValue(pawn);
            var duplicateOfField = tracker == null ? null : AccessTools.Field(tracker.GetType(), "duplicateOf");
            duplicateOfField?.SetValue(tracker, int.MinValue);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] ClearDuplicateTracker: {ex.Message}"); }

        // Remove DuplicateSickness hediff if present
        try
        {
            var sickness = pawn.health?.hediffSet?.hediffs?.FirstOrDefault(h =>
                h?.def?.defName?.IndexOf("DuplicateSickness", StringComparison.OrdinalIgnoreCase) >= 0);
            if (sickness != null)
                pawn.health.RemoveHediff(sickness);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] RemoveDuplicateSickness: {ex.Message}"); }
    }

    private static void FinalizeSpawnState(Pawn pawn)
    {
        if (pawn == null) return;

        try { pawn.Notify_DisabledWorkTypesChanged(); } catch { }

        try
        {
            EnsurePawnGraphicsInitialized(pawn);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] EnsurePawnGraphicsInitialized: {ex.Message}"); }
    }

    private static void EnsureUniqueCloneName(Pawn pawn)
    {
        if (pawn?.Name == null) return;

        string baseName = pawn.Name.ToStringFull;
        string candidate = baseName;
        int index = 2;

        while (PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.Any(p => p != pawn && p.Name != null && p.Name.ToStringFull == candidate))
            candidate = $"{baseName} {index++}";

        if (candidate != baseName)
        {
            pawn.Name = NameTriple.FromString(candidate);
            Log.Warning($"[Pawn Editor] Duplicate pawn name collision; renamed clone to '{candidate}'.");
        }
    }

    // ── Copy: Work Priorities ──

    private static void CopyDup_WorkPriorities(Pawn src, Pawn dst)
    {
        if (src.workSettings == null || dst.workSettings == null) return;
        try
        {
            dst.workSettings.EnableAndInitialize();
            foreach (var wd in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (src.WorkTypeIsDisabled(wd) || dst.WorkTypeIsDisabled(wd)) continue;
                dst.workSettings.SetPriority(wd, src.workSettings.GetPriority(wd));
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_WorkPriorities: {ex.Message}"); }
    }

    // ── Copy: Social Relations (hybrid) ──
    //
    // Three passes:
    //   1. Establish src's direct relations on dst, bidirectionally (uses the def's Worker
    //      so reflexive relations like Spouse/Lover are added on BOTH sides correctly).
    //   2. Copy dst's social memories toward others (so dst's opinion values aren't 0).
    //   3. Hybrid pass: for every OTHER pawn B that has memories ABOUT src, copy to dst
    //      the ones that are POSITIVE (baseMoodEffect > 0). This gives the clone the
    //      goodwill others had for the original, without inheriting bad history.

    private static void CopyDup_Relations(Pawn src, Pawn dst)
    {
        if (src.relations == null || dst.relations == null) return;

        // ── Pass 1: Direct relation definitions — bidirectional via def.Worker ──
        try
        {
            foreach (var rel in src.relations.DirectRelations.ToList())
            {
                if (rel.def == null || rel.otherPawn == null) continue;
                if (rel.otherPawn == src) continue; // no self-relations
                // Use the mod's extension method (RelationUtilities) so the Worker fires,
                // OnRelationCreated runs, and reflexive relations are set on BOTH pawns.
                if (!dst.relations.DirectRelationExists(rel.def, rel.otherPawn))
                    rel.def.AddDirectRelation(dst, rel.otherPawn);
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Relations (direct): {ex.Message}"); }

        // ── Pass 2: dst's own social memories toward others ──
        try
        {
            var srcMems = src.needs?.mood?.thoughts?.memories;
            var dstMems = dst.needs?.mood?.thoughts?.memories;
            if (srcMems != null && dstMems != null)
            {
                foreach (var mem in srcMems.Memories.ToList())
                {
                    // ISocialThought is the RimWorld 1.6 interface for memories with an otherPawn
                    if (!(mem is Thought_Memory memBase)) continue;
                    if (!(mem is ISocialThought socialThought)) continue;
                    var otherPawnRef = socialThought.OtherPawn();
                    if (otherPawnRef == null || otherPawnRef == src) continue;
                    if (memBase.def == null) continue;
                    try
                    {
                        var newMem = ThoughtMaker.MakeThought(memBase.def, memBase.CurStageIndex) as Thought_Memory;
                        if (newMem == null) continue;
                        newMem.age = memBase.age;
                        dstMems.TryGainMemory(newMem, otherPawnRef);
                    }
                    catch (Exception ex) { if (Verse.Prefs.DevMode) Log.Warning($"[Pawn Editor] CopyDup own memory skip: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Relations (own memories): {ex.Message}"); }

        // ── Pass 3: Hybrid — transfer positive memories OTHER pawns have about src → dst ──
        // Logic: the clone inherits goodwill, but not grudges or bad history.
        //   baseMoodEffect > 0  → copy (love, admiration, shared good moments)
        //   baseMoodEffect <= 0 → skip (fights, betrayals — the clone never did those)
        try
        {
            var allPawns = PawnBlueprintSaveLoad.GetAllReachablePawnsPublic();
            foreach (var other in allPawns)
            {
                if (other == src || other == dst) continue;
                var otherMems = other.needs?.mood?.thoughts?.memories;
                if (otherMems == null) continue;

                foreach (var mem in otherMems.Memories.ToList())
                {
                    if (!(mem is Thought_Memory memBase)) continue;
                    if (!(mem is ISocialThought socialThought)) continue;
                    if (socialThought.OtherPawn() != src) continue; // only memories ABOUT src
                    if (memBase.def == null) continue;

                    // Hybrid filter: only positive feelings transfer to the clone
                    var stage = memBase.CurStage;
                    if (stage == null || stage.baseMoodEffect <= 0) continue;

                    try
                    {
                        var newMem = ThoughtMaker.MakeThought(memBase.def, memBase.CurStageIndex) as Thought_Memory;
                        if (newMem == null) continue;
                        newMem.age = memBase.age;
                        otherMems.TryGainMemory(newMem, dst);
                    }
                    catch (Exception ex) { if (Verse.Prefs.DevMode) Log.Warning($"[Pawn Editor] CopyDup hybrid memory skip: {ex.Message}"); }
                }
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Relations (hybrid pass): {ex.Message}"); }
    }

    // ── Copy: Royal Titles (Royalty DLC) ──

    private static void CopyDup_RoyalTitles(Pawn src, Pawn dst)
    {
        if (!ModsConfig.RoyaltyActive) return;
        if (src.royalty == null || dst.royalty == null) return;
        try
        {
            // Copy psylink level
            var srcLevel = src.GetPsylinkLevel();
            var dstLevel = dst.GetPsylinkLevel();
            for (int i = dstLevel; i < srcLevel; i++)
                dst.ChangePsylinkLevel(1);

            // Copy titles
            foreach (var title in src.royalty.AllTitlesForReading)
            {
                if (title?.def == null || title.faction == null) continue;
                dst.royalty.SetTitle(title.faction, title.def, false);
            }

            // Copy favor
            foreach (var faction in Find.FactionManager.AllFactions)
            {
                var favor = src.royalty.GetFavor(faction);
                if (favor > 0)
                    dst.royalty.SetFavor(faction, favor);
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_RoyalTitles: {ex.Message}"); }
    }

    // ── Copy: Records ──

    private static void CopyDup_Records(Pawn src, Pawn dst)
    {
        if (src.records == null || dst.records == null) return;
        try
        {
            foreach (var rd in DefDatabase<RecordDef>.AllDefsListForReading)
            {
                var val = src.records.GetValue(rd);
                if (val != 0f)
                    dst.records.AddTo(rd, val - dst.records.GetValue(rd));
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Records: {ex.Message}"); }
    }

    // ── Copy: Inventory ──

    private static void CopyDup_Inventory(Pawn src, Pawn dst)
    {
        if (src.inventory?.innerContainer == null || dst.inventory?.innerContainer == null) return;
        try
        {
            dst.inventory.innerContainer.ClearAndDestroyContents();
            foreach (var thing in src.inventory.innerContainer)
            {
                if (thing?.def == null) continue;
                try
                {
                    var copy = thing.Stuff != null
                        ? ThingMaker.MakeThing(thing.def, thing.Stuff)
                        : ThingMaker.MakeThing(thing.def);
                    copy.stackCount = thing.stackCount;
                    if (thing.TryGetComp<CompQuality>() is { } srcQ && copy.TryGetComp<CompQuality>() is { } dstQ)
                        dstQ.SetQuality(srcQ.Quality, ArtGenerationContext.Outsider);
                    dst.inventory.innerContainer.TryAdd(copy);
                }
                catch (Exception ex) { if (Verse.Prefs.DevMode) Log.Warning($"[Pawn Editor] CopyDup inventory item: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Inventory: {ex.Message}"); }
    }
}
