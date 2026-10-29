using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// What the randomizers and the appearance editor change on a pawn, captured so it can be put back.
/// Not covered: mod-only data (HAR, Facial Animation) and anything outside these fields. Gradient Hair is covered.
/// Genes granted by other mods (<see cref="GrantedGenes"/>) are neither captured nor touched on restore,
/// nor are Isekai RPG Leveling rank and title traits (they follow the pawn's level).
/// </summary>
public sealed class PawnSnapshot
{
    // The private cache, not the SkinColorBase getter, which can add a gene as a side effect.
    private static readonly AccessTools.FieldRef<Pawn_StoryTracker, Color?> SkinColorBaseField =
        AccessTools.FieldRefAccess<Pawn_StoryTracker, Color?>("skinColorBase");

    public readonly Pawn Pawn;

    private readonly Faction faction;
    private readonly Gender gender;
    private readonly long ageBiologicalTicks;
    private readonly long ageChronologicalTicks;

    private readonly BackstoryDef childhood;
    private readonly BackstoryDef adulthood;
    private readonly List<(TraitDef def, int degree, bool scenForced)> traits;
    private readonly List<(SkillDef def, int level, Passion passion, float xp)> skills;
    private readonly List<VSECompat.ExpertiseSnapshot> expertise; // null without VSE expertise

    private readonly HeadTypeDef headType;
    private readonly BodyTypeDef bodyType;
    private readonly HairDef hairDef;
    private readonly Color hairColor;
    private readonly Color? skinColorBase;
    private readonly Color? skinColorOverride;
    private readonly FurDef furDef;
    private readonly ColorDef favoriteColor;
    private readonly BeardDef beardDef;
    private readonly TattooDef faceTattoo;
    private readonly TattooDef bodyTattoo;

    private readonly XenotypeDef xenotype;
    private readonly string xenotypeName;
    private readonly XenotypeIconDef xenotypeIcon;
    private readonly List<GeneDef> endogenes;
    private readonly List<GeneDef> xenogenes;

    private readonly bool hasGradient;
    private readonly bool gradientEnabled;
    private readonly Color gradientColorB;
    private readonly StyleItemDef gradientPattern;

    public PawnSnapshot(Pawn pawn)
    {
        Pawn = pawn;
        faction = pawn.Faction;
        gender = pawn.gender;
        ageBiologicalTicks = pawn.ageTracker.AgeBiologicalTicks;
        ageChronologicalTicks = pawn.ageTracker.AgeChronologicalTicks;

        var story = pawn.story;
        if (story != null)
        {
            childhood = story.Childhood;
            adulthood = story.Adulthood;
            // Gene-granted traits come back with the genes.
            traits = story.traits.allTraits.Where(IsRestorable)
                .Select(t => (t.def, t.Degree, t.ScenForced)).ToList();
            headType = story.headType;
            bodyType = story.bodyType;
            hairDef = story.hairDef;
            hairColor = story.HairColor;
            skinColorBase = SkinColorBaseField(story);
            skinColorOverride = story.skinColorOverride;
            furDef = story.furDef;
            favoriteColor = story.favoriteColor;
        }

        skills = pawn.skills?.skills.Select(s => (s.def, s.levelInt, s.passion, s.xpSinceLastLevel)).ToList();
        // The Skills column can edit VSE expertise, so Cancel has to undo it too.
        if (VSECompat.Active && VSECompat.HasExpertiseSupport) expertise = VSECompat.GetExpertiseData(pawn);

        if (pawn.style != null)
        {
            beardDef = pawn.style.beardDef;
            faceTattoo = pawn.style.FaceTattoo;
            bodyTattoo = pawn.style.BodyTattoo;
        }

        if (ModsConfig.BiotechActive && pawn.genes != null)
        {
            xenotype = pawn.genes.Xenotype;
            xenotypeName = pawn.genes.xenotypeName;
            xenotypeIcon = pawn.genes.iconDef;
            endogenes = GrantedGenes.OwnGeneDefs(pawn, xenogenes: false);
            xenogenes = GrantedGenes.OwnGeneDefs(pawn, xenogenes: true);
        }

        hasGradient = GradientHairCompat.TryGet(pawn, out gradientEnabled, out gradientColorB);
        if (hasGradient) gradientPattern = GradientHairCompat.CurrentPattern(pawn);
    }

    public bool IsValidFor(Pawn pawn) => pawn != null && pawn == Pawn && !Pawn.Discarded;

    public void Restore()
    {
        if (Pawn.Discarded) return;
        if (faction != null && Pawn.Faction != faction) Pawn.SetFaction(faction);
        Pawn.gender = gender;
        Pawn.ageTracker.AgeBiologicalTicks = ageBiologicalTicks;
        Pawn.ageTracker.AgeChronologicalTicks = ageChronologicalTicks;

        Diagnostics.Run("Restoring genes", Pawn, RestoreGenes); // before traits: genes grant traits
        Diagnostics.Run("Restoring backstory and traits", Pawn, RestoreStory);
        Diagnostics.Run("Restoring skills", Pawn, RestoreSkills);
        Diagnostics.Run("Restoring appearance", Pawn, RestoreAppearance);
        PawnEditor.RefreshPawnGraphics(Pawn);
    }

    private void RestoreGenes()
    {
        var genes = Pawn.genes;
        if (endogenes == null || genes == null || GenesUnchanged(genes)) return;

        GrantedGenes.RemoveAllExceptGranted(Pawn);
        genes.SetXenotypeDirect(xenotype);
        AddGenes(endogenes, false);
        AddGenes(xenogenes, true);
        genes.xenotypeName = xenotypeName;
        genes.iconDef = xenotypeIcon;
    }

    private bool GenesUnchanged(Pawn_GeneTracker genes) =>
        genes.Xenotype == xenotype && genes.xenotypeName == xenotypeName && genes.iconDef == xenotypeIcon
        && GrantedGenes.OwnGeneDefs(Pawn, xenogenes: false).SequenceEqual(endogenes)
        && GrantedGenes.OwnGeneDefs(Pawn, xenogenes: true).SequenceEqual(xenogenes);

    private void AddGenes(List<GeneDef> defs, bool xenogene)
    {
        foreach (var def in defs)
        {
            try { Pawn.genes.AddGene(def, xenogene); }
            catch (Exception ex) { Log.Warning($"[Pawn Editor] Undo skipped gene {def?.defName}: {ex.Message}"); }
        }
    }

    private void RestoreStory()
    {
        var story = Pawn.story;
        if (story == null || traits == null) return;
        story.Childhood = childhood;
        story.Adulthood = adulthood;

        foreach (var trait in story.traits.allTraits.Where(IsRestorable).ToList())
            story.traits.RemoveTrait(trait);
        foreach (var (def, degree, scenForced) in traits)
            story.traits.GainTrait(new Trait(def, degree, scenForced));
    }

    private static bool IsRestorable(Trait trait) =>
        trait.sourceGene == null && !IsekaiLevelingCompat.IsSystemTrait(trait.def);

    private void RestoreSkills()
    {
        if (skills == null || Pawn.skills == null) return;
        foreach (var (def, level, passion, xp) in skills)
        {
            var record = Pawn.skills.GetSkill(def);
            if (record == null) continue;
            record.levelInt = level;
            record.passion = passion;
            record.xpSinceLastLevel = xp;
        }

        if (VSECompat.Active) VSECompat.SyncPassionHediffs(Pawn);
        if (expertise != null) VSECompat.SetExpertise(Pawn, expertise);
    }

    private void RestoreAppearance()
    {
        var story = Pawn.story;
        if (story != null && traits != null)
        {
            story.headType = headType;
            story.bodyType = bodyType;
            story.hairDef = hairDef;
            story.HairColor = hairColor;
            SkinColorBaseField(story) = skinColorBase;
            story.skinColorOverride = skinColorOverride;
            story.furDef = furDef;
            story.favoriteColor = favoriteColor;
        }

        if (hasGradient)
        {
            GradientHairCompat.Set(Pawn, gradientEnabled, gradientColorB);
            if (gradientPattern != null) GradientHairCompat.SetPattern(Pawn, gradientPattern);
        }

        if (Pawn.style == null) return;
        Pawn.style.beardDef = beardDef;
        if (!ModsConfig.IdeologyActive) return;
        Pawn.style.FaceTattoo = faceTattoo;
        Pawn.style.BodyTattoo = bodyTattoo;
    }
}
