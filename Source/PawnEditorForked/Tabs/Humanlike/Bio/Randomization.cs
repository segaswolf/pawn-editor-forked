using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

public partial class TabWorker_Bio_Humanlike
{
    public override IEnumerable<FloatMenuOption> GetRandomizationOptions(Pawn pawn)
    {
        yield return new("PawnEditor.All".Translate(), () => RandomizeAll(pawn));
        // "Keep xenotype": same full randomize, but the new pawn keeps the original's species/
        // xenotype (def or custom). Useful for "give me a fresh pawn but still a Veldrak".
        yield return new("PawnEditor.AllKeepXenotype".Translate(), () => RandomizeAll(pawn, keepXenotype: true));
        yield return new("Appearance".Translate(), () => RandomizeAppearance(pawn));
        // yield return new("PawnEditor.Shape".Translate(), () => RandomizeShape(pawn));
        yield return new("Relations".Translate(), () => RandomizeRelations(pawn));
        yield return new("Traits".Translate(), () => RandomizeTraits(pawn));
        yield return new("Skills".Translate(), () => RandomizeSkills(pawn));
        yield return new("Backstory".Translate(), () => RandomizeBackstory(pawn));
        if (VAspirECompat.Active)
        {
            var fulfillment = VAspirECompat.GetFulfillmentNeed(pawn);
            if (fulfillment != null)
                yield return new("PawnEditor.Aspirations".Translate(), () => VAspirECompat.ReinitializeAspirations(fulfillment));
        }
    }

    public static void RandomizeAll(Pawn pawn, bool keepXenotype = false)
    {
        if (!PawnEditor.Pregame)
        {
            // Capture the old pawn's FULL xenotype identity before deleting it. Capturing only the
            // XenotypeDef loses named/modded xenotypes whose def is Baseliner (e.g. "Veldrak" =
            // Baseliner + a xenotypeName + Saurid genes) — those need their actual genes + name + icon,
            // not just the def, or the reroll drops back to a plain baseliner.
            XenotypeDef keptXenotype = null;
            CustomXenotype keptCustom = null;
            List<GeneDef> keptEndogenes = null, keptXenogenes = null;
            string keptXenoName = null;
            XenotypeIconDef keptIcon = null;
            if (keepXenotype && ModsConfig.BiotechActive && pawn.genes != null)
            {
                keptCustom    = pawn.genes.CustomXenotype;
                keptXenotype  = pawn.genes.Xenotype;
                keptXenoName  = pawn.genes.xenotypeName;
                keptIcon      = pawn.genes.iconDef;
                keptEndogenes = pawn.genes.Endogenes.Select(g => g.def).Where(d => d != null).ToList();
                keptXenogenes = pawn.genes.Xenogenes.Select(g => g.def).Where(d => d != null).ToList();
            }

            // Delete
            var oldPawn = pawn;
            var position = oldPawn.Position;
            var map = oldPawn.Map;
            PawnEditor.PawnList.OnDelete(oldPawn);

            // Replace. Force a REAL XenotypeDef at generation (Sanguophage, a modded species...) so it
            // comes back with the right def + genes. The Baseliner-named case (Veldrak) and the custom
            // case are rebuilt after generation instead, since their identity is a set of genes.
            bool realDef = keptXenotype != null && keptXenotype != XenotypeDefOf.Baseliner && keptCustom == null;
            var request = new PawnGenerationRequest(pawn.kindDef, PawnEditor.selectedFaction);
            if (realDef) request.ForcedXenotype = keptXenotype;
            pawn = PawnGenerator.GeneratePawn(request);

            if (keepXenotype && ModsConfig.BiotechActive && pawn.genes != null)
            {
                if (keptCustom != null)
                    ApplyCustomXenotypeTo(pawn, keptCustom);                       // user-built custom
                else if (realDef)
                {
                    // Def already forced; restore the display name/icon (named variants of a real def).
                    if (!keptXenoName.NullOrEmpty()) pawn.genes.xenotypeName = keptXenoName;
                    if (keptIcon != null) pawn.genes.iconDef = keptIcon;
                }
                else
                    ApplyKeptGenes(pawn, keptEndogenes, keptXenogenes, keptXenoName, keptIcon); // baseliner-named
            }

            PawnEditor.AddPawn(pawn, PawnEditor.selectedCategory).HandleResult();
            if (!PawnEditor.Pregame && map != null)
            {
                GenSpawn.Spawn(pawn, position, map);
                PawnEditor.PawnList.UpdateCache(PawnEditor.selectedFaction, PawnEditor.selectedCategory);
            }

            TabWorker_FactionOverview.RecachePawns(PawnEditor.selectedFaction);
        }
        else
        {
            // Pregame path uses vanilla's randomizer. It doesn't expose a keep-xenotype option, so
            // for now keepXenotype has no effect here (the whole pawn including xenotype is rerolled).
            var index = StartingPawnUtility.PawnIndex(pawn);
            StartingPawnUtility.RandomizePawn(index);
        }
    }

    /// <summary>
    /// Applies a saved CustomXenotype to a freshly generated pawn, mirroring the guarded logic in
    /// Dialog_AppearanceEditor.ApplyCustomXenotype (skip null genes from removed mods; never let a
    /// bad gene abort the rest). Static so RandomizeAll can call it.
    /// </summary>
    private static void ApplyCustomXenotypeTo(Pawn pawn, CustomXenotype customXenotype)
    {
        if (pawn?.genes == null || customXenotype == null) return;
        try
        {
            if (!pawn.IsBaseliner()) pawn.genes.SetXenotype(XenotypeDefOf.Baseliner);
            pawn.genes.xenotypeName = customXenotype.name;
            pawn.genes.iconDef = customXenotype.IconDef;
            if (customXenotype.genes != null)
            {
                foreach (var geneDef in customXenotype.genes)
                {
                    if (geneDef == null) continue; // gene from a removed mod
                    try { pawn.genes.AddGene(geneDef, !customXenotype.inheritable); }
                    catch (Exception ex) { Log.Warning($"[Pawn Editor] Skipped a gene re-applying xenotype '{customXenotype.name}': {ex.Message}"); }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[Pawn Editor] Failed to re-apply custom xenotype '{customXenotype.name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Rebuild a captured xenotype from its actual GENES (+ name + icon) onto a freshly generated
    /// pawn. For "named baseliner" xenotypes (e.g. a VRE "Veldrak": genes.Xenotype == Baseliner but
    /// with a xenotypeName and specific genes) the def alone is meaningless, so we reset to baseliner
    /// and re-add the exact endo/xenogenes the original had. Mirrors the guarded logic in
    /// ApplyCustomXenotypeTo (skip null genes from removed mods; never let one bad gene abort the rest).
    /// </summary>
    private static void ApplyKeptGenes(Pawn pawn, List<GeneDef> endogenes, List<GeneDef> xenogenes,
        string xenotypeName, XenotypeIconDef iconDef)
    {
        if (pawn?.genes == null) return;
        try
        {
            pawn.genes.SetXenotype(XenotypeDefOf.Baseliner);

            if (endogenes != null)
                foreach (var geneDef in endogenes)
                {
                    if (geneDef == null) continue;
                    try { pawn.genes.AddGene(geneDef, xenogene: false); }
                    catch (Exception ex) { Log.Warning($"[Pawn Editor] keep-xenotype endogene skip: {ex.Message}"); }
                }

            if (xenogenes != null)
                foreach (var geneDef in xenogenes)
                {
                    if (geneDef == null) continue;
                    try { pawn.genes.AddGene(geneDef, xenogene: true); }
                    catch (Exception ex) { Log.Warning($"[Pawn Editor] keep-xenotype xenogene skip: {ex.Message}"); }
                }

            if (!xenotypeName.NullOrEmpty()) pawn.genes.xenotypeName = xenotypeName;
            if (iconDef != null) pawn.genes.iconDef = iconDef;
        }
        catch (Exception ex)
        {
            Log.Error($"[Pawn Editor] keep-xenotype gene rebuild failed: {ex.Message}");
        }
    }

    public static void RandomizeBackstory(Pawn pawn)
    {
        // Save passions and levels before backstory change — same fix as ListingMenu_Backstories.
        var savedPassions = new Dictionary<SkillDef, Passion>();
        var savedLevels = new Dictionary<SkillDef, int>();
        if (pawn.skills?.skills != null)
        {
            foreach (var sr in pawn.skills.skills)
            {
                if (sr?.def != null)
                {
                    savedPassions[sr.def] = sr.passion;
                    savedLevels[sr.def] = sr.levelInt;
                }
            }
        }

        if (pawn.story.adulthood != null) PawnBioAndNameGenerator.FillBackstorySlotShuffled(pawn, BackstorySlot.Adulthood, PawnBioAndNameGenerator.GetBackstoryCategoryFiltersFor(pawn, pawn.Faction.def), pawn.Faction.def);
        if (pawn.story.childhood != null) PawnBioAndNameGenerator.FillBackstorySlotShuffled(pawn, BackstorySlot.Childhood, PawnBioAndNameGenerator.GetBackstoryCategoryFiltersFor(pawn, pawn.Faction.def), pawn.Faction.def);

        // Restore passions and levels.
        if (pawn.skills?.skills != null)
        {
            foreach (var sr in pawn.skills.skills)
            {
                if (sr?.def != null && savedPassions.TryGetValue(sr.def, out var passion))
                    sr.passion = passion;
                if (sr?.def != null && savedLevels.TryGetValue(sr.def, out var level))
                    sr.levelInt = level;
            }
        }
    }

    public static void RandomizeAppearance(Pawn pawn)
    {
        pawn.story.hairDef = PawnStyleItemChooser.RandomHairFor(pawn);
        if (ModLister.IdeologyInstalled)
        {
            pawn.style.FaceTattoo = PawnStyleItemChooser.RandomTattooFor(pawn, TattooType.Face);
            pawn.style.BodyTattoo = PawnStyleItemChooser.RandomTattooFor(pawn, TattooType.Body);
            pawn.style.beardDef = PawnStyleItemChooser.RandomBeardFor(pawn);
        }

        if (pawn.genes.GetMelaninGene() is { } geneDef1 && pawn.genes.GetGene(geneDef1) is { } gene1) pawn.genes.RemoveGene(gene1);
        var geneDef4 = PawnSkinColors.RandomSkinColorGene(pawn);
        if (geneDef4 != null) pawn.genes.AddGene(geneDef4, false);
        if (pawn.genes.GetHairColorGene() is { } geneDef2 && pawn.genes.GetGene(geneDef2) is { } gene2) pawn.genes.RemoveGene(gene2);
        var geneDef5 = PawnHairColors.RandomHairColorGene(pawn.story.SkinColorBase);
        if (geneDef5 != null) pawn.genes.AddGene(geneDef5, false);
        else
        {
            pawn.story.HairColor = PawnHairColors.RandomHairColor(pawn, pawn.story.SkinColorBase, pawn.ageTracker.AgeBiologicalYears);
            Log.Error("No hair color gene for " + pawn.LabelShort + ". Getting random color as fallback.");
        }

        RandomizeShape(pawn);
    }

    public static void RandomizeShape(Pawn pawn)
    {
        var headTypes = DefDatabase<HeadTypeDef>.AllDefs.Where(h => h.gender != pawn.gender.Opposite() && h.randomChosen);
        if (HARCompat.Active)
        {
            headTypes = HARCompat.FilterHeadTypes(headTypes, pawn);
            // HAR doesn't like head types not matching genders
            headTypes = headTypes.Where(type => type.gender == Gender.None || type.gender == pawn.gender);
        }

        pawn.story.bodyType = PawnGenerator.GetBodyTypeFor(pawn);
        pawn.story.headType = headTypes.RandomElement();
        // Gender intentionally NOT randomized here — RandomizeShape is about body/head, not sex.

        // Centralized, deferred, fully-guarded refresh (see PawnEditor.RefreshPawnGraphics).
        // NOTE: this path previously omitted GlobalTextureAtlasManager, so the pawn's MAP sprite
        // could keep the old body/head after a randomize until something else refreshed it. The
        // helper includes it, so the map sprite now updates too.
        PawnEditor.RefreshPawnGraphics(pawn);
    }

    private static void RandomizeTraits(Pawn pawn)
    {
        var traitRequirements = (pawn.kindDef.forcedTraits ?? Enumerable.Empty<TraitRequirement>()).Concat(pawn.story.AllBackstories.SelectMany(
                backstory => backstory.forcedTraits ?? Enumerable.Empty<BackstoryTrait>(),
                (_, backstoryTrait) => new TraitRequirement { def = backstoryTrait.def, degree = backstoryTrait.degree }))
            .ToList();
        var forcedTraits = pawn.story.traits.allTraits
            .Where(trait => trait.sourceGene != null || traitRequirements.Any(req => req.def == trait.def && req.degree == trait.degree))
            .ToHashSet();
        foreach (var trait in pawn.story.traits.allTraits.Except(forcedTraits).ToList()) pawn.story.traits.RemoveTrait(trait, true);
        var num = Mathf.Min(GrowthUtility.GrowthMomentAges.Length, PawnGenerator.TraitsCountRange.RandomInRange);
        var ageBiologicalYears = pawn.ageTracker.AgeBiologicalYears;
        var num2 = 3;
        while (num2 <= ageBiologicalYears && pawn.story.traits.allTraits.Count < num)
        {
            if (GrowthUtility.IsGrowthBirthday(num2))
            {
                var trait = PawnGenerator.GenerateTraitsFor(pawn, 1, null, true).FirstOrFallback();
                if (trait != null) pawn.story.traits.GainTrait(trait);
            }

            num2++;
        }

        if (PawnGenerator.HasSexualityTrait(pawn)) return;

        if (LovePartnerRelationUtility.HasAnyLovePartnerOfTheSameGender(pawn)
            || LovePartnerRelationUtility.HasAnyExLovePartnerOfTheSameGender(pawn))
            pawn.story.traits.GainTrait(new(TraitDefOf.Gay, PawnGenerator.RandomTraitDegree(TraitDefOf.Gay)));

        if (!ModsConfig.BiotechActive || pawn.ageTracker.AgeBiologicalYears >= 13) PawnGenerator.TryGenerateSexualityTraitFor(pawn, true);
    }

    private static void RandomizeRelations(Pawn pawn)
    {
        var request = new PawnGenerationRequest(pawn.kindDef, pawn.Faction);
        pawn.relations.ClearAllRelations();
        PawnGenerator.GeneratePawnRelations(pawn, ref request);
    }

    private static void RandomizeSkills(Pawn pawn)
    {
        foreach (var skillRecord in pawn.skills.skills) skillRecord.passion = Passion.None;
        PawnGenerator.GenerateSkills(pawn, new(pawn.kindDef, pawn.Faction));
    }
}