using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

public partial class TabWorker_Bio_Humanlike
{
    // First age vanilla checks for a growth-moment trait.
    private const int FirstGrowthCheckAge = 3;
    // Bound for the ungated trait fill, so heavy trait modlists can't loop forever.
    private const int MaxTraitFillAttempts = 20;
    // With Biotech, vanilla only gives sexuality traits from this age.
    private const int SexualityTraitAge = 13;

    public override IEnumerable<FloatMenuOption> GetRandomizationOptions(Pawn pawn)
    {
        // The template's xenotype comes from the pawn's kind and faction: mostly baseliner for a player colony.
        yield return new("PawnEditor.AllFactionXenotype".Translate(), () => RandomizeAll(pawn));
        // Full reroll that keeps the pawn's xenotype (def or custom).
        yield return new("PawnEditor.AllKeepXenotype".Translate(), () => RandomizeAll(pawn, keepXenotype: true));
        yield return new("Appearance".Translate(), () => RandomizeAppearance(pawn));
        yield return new("Traits".Translate(), () => RandomizeTraits(pawn));
        // Separate option, so the plain "Traits" reroll never touches backstory-forced traits.
        if (HasBackstoryForcedTraits(pawn) || ProgressionEducationCompat.HasProficiencies(pawn))
            yield return new("PawnEditor.TraitsIncludingForced".Translate(), () => RandomizeTraits(pawn, true));
        yield return new("Skills".Translate(), () => RandomizeSkills(pawn));
        yield return new("Backstory".Translate(), () => RandomizeBackstory(pawn));
        if (VAspirECompat.Active && VAspirECompat.GetFulfillmentNeed(pawn) is { } fulfillment)
            yield return new("PawnEditor.Aspirations".Translate(), () => VAspirECompat.ReinitializeAspirations(fulfillment));
    }

    /// <summary>Rerolls the pawn in place: same pawn, name, faction, relations, gear and health.</summary>
    public static void RandomizeAll(Pawn pawn, bool keepXenotype = false)
    {
        var template = GenerateTemplate(pawn);
        try
        {
            if (!keepXenotype) CopyGenes(template, pawn);
            if (template.gender != pawn.gender) SetGender(pawn, template.gender);
            pawn.ageTracker.AgeBiologicalTicks = template.ageTracker.AgeBiologicalTicks;
            pawn.ageTracker.AgeChronologicalTicks = template.ageTracker.AgeChronologicalTicks;
        }
        finally
        {
            template.Discard(true);
        }

        RandomizeBackstory(pawn);
        RandomizeTraits(pawn);
        RandomizeSkills(pawn); // after backstory and traits, which both affect skills
        RandomizeAppearance(pawn);
    }

    /// <summary>A throwaway pawn of the same kind and life stage, used as the source of random values.</summary>
    private static Pawn GenerateTemplate(Pawn pawn)
    {
        var request = new PawnGenerationRequest(pawn.kindDef, pawn.Faction)
        {
            AllowedDevelopmentalStages = pawn.DevelopmentalStage,
            CanGeneratePawnRelations = false,
            ForbidAnyTitle = true,
        };
        return PawnGenerator.GeneratePawn(request);
    }

    /// <summary>Replaces the pawn's xenotype and its genes with the template's. Quirk-granted genes stay.</summary>
    private static void CopyGenes(Pawn from, Pawn to)
    {
        if (!ModsConfig.BiotechActive || from.genes == null || to.genes == null) return;
        try
        {
            GrantedGenes.RemoveAllExceptGranted(to);
            to.genes.SetXenotypeDirect(from.genes.Xenotype);
            AddGenesSafely(to, from.genes.Endogenes.Select(g => g.def).ToList(), false, "randomized endogenes");
            AddGenesSafely(to, from.genes.Xenogenes.Select(g => g.def).ToList(), true, "randomized xenogenes");
            to.genes.xenotypeName = from.genes.xenotypeName;
            to.genes.iconDef = from.genes.iconDef;
        }
        catch (Exception ex)
        {
            Log.Error($"[Pawn Editor] Randomizing the xenotype failed: {ex.Message}");
        }
    }

    /// <summary>Adds each gene, skipping nulls (removed mods). One failing gene doesn't stop the rest.</summary>
    private static void AddGenesSafely(Pawn pawn, List<GeneDef> geneDefs, bool xenogene, string context)
    {
        if (geneDefs == null) return;
        foreach (var geneDef in geneDefs)
        {
            if (geneDef == null) continue;
            try { pawn.genes.AddGene(geneDef, xenogene); }
            catch (Exception ex) { Log.Warning($"[Pawn Editor] Skipped gene {geneDef.defName} ({context}): {ex.Message}"); }
        }
    }

    public static void RandomizeBackstory(Pawn pawn)
    {
        // A new backstory rerolls skills: keep the passions and levels the user set.
        var skills = SnapshotSkills(pawn);
        if (pawn.story.adulthood != null) RefillBackstorySlot(pawn, BackstorySlot.Adulthood);
        if (pawn.story.childhood != null) RefillBackstorySlot(pawn, BackstorySlot.Childhood);
        RestoreSkills(pawn, skills);
    }

    private static void RefillBackstorySlot(Pawn pawn, BackstorySlot slot)
    {
        var factionDef = pawn.Faction.def;
        PawnBioAndNameGenerator.FillBackstorySlotShuffled(pawn, slot,
            PawnBioAndNameGenerator.GetBackstoryCategoryFiltersFor(pawn, factionDef), factionDef);
    }

    private static Dictionary<SkillDef, (Passion passion, int level)> SnapshotSkills(Pawn pawn)
    {
        var snapshot = new Dictionary<SkillDef, (Passion passion, int level)>();
        if (pawn.skills?.skills == null) return snapshot;
        foreach (var record in pawn.skills.skills)
            if (record?.def != null)
                snapshot[record.def] = (record.passion, record.levelInt);
        return snapshot;
    }

    private static void RestoreSkills(Pawn pawn, Dictionary<SkillDef, (Passion passion, int level)> snapshot)
    {
        if (pawn.skills?.skills == null) return;
        foreach (var record in pawn.skills.skills)
        {
            if (record?.def == null || !snapshot.TryGetValue(record.def, out var saved)) continue;
            record.passion = saved.passion;
            record.levelInt = saved.level;
        }

        if (VSECompat.Active) VSECompat.SyncPassionHediffs(pawn);
    }

    public static void RandomizeAppearance(Pawn pawn)
    {
        RandomizeStyleItems(pawn);
        RandomizeSkinColorGene(pawn);
        RandomizeHairColorGene(pawn); // after skin: hair color depends on it
        RandomizeShape(pawn);
    }

    private static void RandomizeStyleItems(Pawn pawn)
    {
        pawn.story.hairDef = PawnStyleItemChooser.RandomHairFor(pawn);
        if (!ModLister.IdeologyInstalled) return;
        pawn.style.FaceTattoo = PawnStyleItemChooser.RandomTattooFor(pawn, TattooType.Face);
        pawn.style.BodyTattoo = PawnStyleItemChooser.RandomTattooFor(pawn, TattooType.Body);
        pawn.style.beardDef = PawnStyleItemChooser.RandomBeardFor(pawn);
    }

    private static void RandomizeSkinColorGene(Pawn pawn)
    {
        if (pawn.genes.GetMelaninGene() is { } oldDef && pawn.genes.GetGene(oldDef) is { } oldGene) pawn.genes.RemoveGene(oldGene);
        if (PawnSkinColors.RandomSkinColorGene(pawn) is { } newDef) pawn.genes.AddGene(newDef, false);
    }

    private static void RandomizeHairColorGene(Pawn pawn)
    {
        if (pawn.genes.GetHairColorGene() is { } oldDef && pawn.genes.GetGene(oldDef) is { } oldGene) pawn.genes.RemoveGene(oldGene);
        if (PawnHairColors.RandomHairColorGene(pawn.story.SkinColorBase) is { } newDef)
        {
            pawn.genes.AddGene(newDef, false);
            return;
        }

        pawn.story.HairColor = PawnHairColors.RandomHairColor(pawn, pawn.story.SkinColorBase, pawn.ageTracker.AgeBiologicalYears);
        Log.Error("No hair color gene for " + pawn.LabelShort + ". Getting random color as fallback.");
    }

    /// <summary>Rerolls body and head type. Sex is left alone.</summary>
    public static void RandomizeShape(Pawn pawn)
    {
        var headTypes = HeadTypeCandidates(pawn);
        pawn.story.bodyType = PawnGenerator.GetBodyTypeFor(pawn);
        pawn.story.headType = headTypes.RandomElement();
        PawnEditor.RefreshPawnGraphics(pawn);
    }

    private static IEnumerable<HeadTypeDef> HeadTypeCandidates(Pawn pawn)
    {
        var headTypes = DefDatabase<HeadTypeDef>.AllDefs.Where(h => h.gender != pawn.gender.Opposite() && h.randomChosen);
        if (!HARCompat.Active) return headTypes;
        // HAR rejects head types whose gender doesn't match the pawn.
        return HARCompat.FilterHeadTypes(headTypes, pawn).Where(type => type.gender == Gender.None || type.gender == pawn.gender);
    }

    /// <summary>Traits required by the pawn's kindDef or backstories (not gene-granted ones).</summary>
    private static List<TraitRequirement> GetTraitRequirements(Pawn pawn) =>
        (pawn.kindDef.forcedTraits ?? Enumerable.Empty<TraitRequirement>()).Concat(pawn.story.AllBackstories.SelectMany(
                backstory => backstory.forcedTraits ?? Enumerable.Empty<BackstoryTrait>(),
                (_, backstoryTrait) => new TraitRequirement { def = backstoryTrait.def, degree = backstoryTrait.degree }))
            .ToList();

    private static bool IsRequiredTrait(Trait trait, List<TraitRequirement> requirements) =>
        requirements.Any(req => req.def == trait.def && req.degree == trait.degree);

    private static bool HasBackstoryForcedTraits(Pawn pawn)
    {
        if (pawn?.story?.traits?.allTraits == null) return false;
        var requirements = GetTraitRequirements(pawn);
        return pawn.story.traits.allTraits.Any(trait => trait.sourceGene == null && IsRequiredTrait(trait, requirements));
    }

    /// <param name="includeForced">Also reroll kindDef/backstory-forced traits. Gene-granted traits are always kept.</param>
    private static void RandomizeTraits(Pawn pawn, bool includeForced = false)
    {
        var traits = pawn.story.traits;
        var kept = KeptTraits(pawn, includeForced);
        // Snapshot before removing, so an empty reroll can put them back.
        var previousTraits = traits.allTraits.Except(kept).ToList();
        foreach (var trait in previousTraits) traits.RemoveTrait(trait, true);

        // Gene and proficiency traits don't count towards the target: with Progression: Education a pawn can
        // already hold three proficiency traits, and counting them meant no trait was ever rolled.
        var uncounted = kept.Count(trait => trait.sourceGene != null || ProgressionEducationCompat.IsProficiencyTrait(trait.def)
                                            || IsekaiLevelingCompat.IsSystemTrait(trait.def));
        var targetCount = uncounted + Mathf.Min(GrowthUtility.GrowthMomentAges.Length, PawnGenerator.TraitsCountRange.RandomInRange);
        GainGrowthMomentTraits(pawn, targetCount);
        FillRemainingTraits(pawn, targetCount);
        RestoreTraitsIfNoneRolled(pawn, previousTraits, kept.Count);

        // Proficiencies are schooling, not personality: only the forced reroll touches them (within their track).
        if (includeForced) ProgressionEducationCompat.RandomizeProficiencies(pawn);
        AddSexualityTraits(pawn);
    }

    /// <summary>Traits the reroll must not remove.</summary>
    private static HashSet<Trait> KeptTraits(Pawn pawn, bool includeForced)
    {
        var requirements = GetTraitRequirements(pawn);
        return pawn.story.traits.allTraits
            .Where(trait => trait.sourceGene != null
                // Proficiency traits have commonality 0: the generator could never roll them back.
                || ProgressionEducationCompat.IsProficiencyTrait(trait.def)
                // Isekai rank and title traits follow the level; they have commonality 0 too.
                || IsekaiLevelingCompat.IsSystemTrait(trait.def)
                || (!includeForced && IsRequiredTrait(trait, requirements)))
            .ToHashSet();
    }

    /// <summary>One trait per growth birthday the pawn has passed, like vanilla.</summary>
    private static void GainGrowthMomentTraits(Pawn pawn, int targetCount)
    {
        var traits = pawn.story.traits;
        var age = pawn.ageTracker.AgeBiologicalYears;
        for (var year = FirstGrowthCheckAge; year <= age && traits.allTraits.Count < targetCount; year++)
        {
            if (!GrowthUtility.IsGrowthBirthday(year)) continue;
            var trait = PawnGenerator.GenerateTraitsFor(pawn, 1, null, true).FirstOrFallback();
            if (trait != null) traits.GainTrait(trait);
        }
    }

    /// <summary>Fills the rest without the birthday gate: young pawns, or modlists where candidates keep conflicting.</summary>
    private static void FillRemainingTraits(Pawn pawn, int targetCount)
    {
        var traits = pawn.story.traits;
        for (var attempt = 0; attempt < MaxTraitFillAttempts && traits.allTraits.Count < targetCount; attempt++)
        {
            var trait = PawnGenerator.GenerateTraitsFor(pawn, 1, null, true).FirstOrFallback();
            if (trait != null && !traits.allTraits.Any(t => t.def == trait.def)) traits.GainTrait(trait);
        }
    }

    /// <summary>If the reroll added nothing beyond the kept traits, gives back the old ones instead of leaving none.</summary>
    private static void RestoreTraitsIfNoneRolled(Pawn pawn, List<Trait> previousTraits, int keptCount)
    {
        var traits = pawn.story.traits;
        if (traits.allTraits.Count > keptCount || previousTraits.Count == 0) return;
        foreach (var trait in previousTraits)
            traits.GainTrait(new Trait(trait.def, trait.Degree, trait.ScenForced));
        Messages.Message("Pawn Editor: couldn't generate new traits for this pawn, kept the existing ones.",
            MessageTypeDefOf.RejectInput, false);
    }

    /// <summary>The sexuality step vanilla runs after generating traits.</summary>
    private static void AddSexualityTraits(Pawn pawn)
    {
        if (PawnGenerator.HasSexualityTrait(pawn)) return;

        if (LovePartnerRelationUtility.HasAnyLovePartnerOfTheSameGender(pawn)
            || LovePartnerRelationUtility.HasAnyExLovePartnerOfTheSameGender(pawn))
            pawn.story.traits.GainTrait(new(TraitDefOf.Gay, PawnGenerator.RandomTraitDegree(TraitDefOf.Gay)));

        if (!ModsConfig.BiotechActive || pawn.ageTracker.AgeBiologicalYears >= SexualityTraitAge)
            PawnGenerator.TryGenerateSexualityTraitFor(pawn, true);
    }

    private static void RandomizeSkills(Pawn pawn)
    {
        foreach (var skillRecord in pawn.skills.skills) skillRecord.passion = Passion.None;
        PawnGenerator.GenerateSkills(pawn, new(pawn.kindDef, pawn.Faction));
        // GenerateSkills sets vanilla passions; drop any orphan VSE passion hediffs.
        if (VSECompat.Active) VSECompat.SyncPassionHediffs(pawn);
    }
}
