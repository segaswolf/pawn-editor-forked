using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Shared utility for backstory transitions when a pawn's developmental stage changes.
/// Used by both the age field (BasicInfo) and the dev stage button (TopRightButtons)
/// to avoid duplicating the backstory add/remove logic.
/// </summary>
public static class BackstoryUtility
{
    /// <summary>
    /// Handles backstory changes when a pawn transitions between developmental stages.
    /// - Adult → Child/Baby: removes adult backstory
    /// - Child/Baby → Adult: generates a contextual adult backstory based on childhood categories
    /// Also adjusts body type and refreshes graphics.
    /// </summary>
    public static void HandleDevStageTransition(Pawn pawn, DevelopmentalStage newStage)
    {
        if (newStage != DevelopmentalStage.Adult && pawn.story.Adulthood != null)
        {
            // Transitioned to child/baby — remove adult backstory
            pawn.story.Adulthood = null;
            pawn.story.bodyType = PawnGenerator.GetBodyTypeFor(pawn);
            TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
        }
        else if (newStage == DevelopmentalStage.Adult && pawn.story.Adulthood == null)
        {
            // Transitioned to adult — generate a contextual adult backstory
            pawn.story.Adulthood = GenerateAdultBackstory(pawn);
            pawn.story.bodyType = PawnGenerator.GetBodyTypeFor(pawn);
            TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
        }
    }

    /// <summary>
    /// Generates a random adult backstory that matches the pawn's childhood categories.
    /// If no matching backstory is found, picks any shuffleable adult backstory.
    /// </summary>
    public static BackstoryDef GenerateAdultBackstory(Pawn pawn)
    {
        var allAdult = DefDatabase<BackstoryDef>.AllDefsListForReading
            .Where(bs => bs.slot == BackstorySlot.Adulthood && bs.shuffleable)
            .ToList();

        if (allAdult.Count == 0) return null;

        var childCategories = pawn.story.Childhood?.spawnCategories;
        if (childCategories != null && childCategories.Any())
        {
            var matched = allAdult
                .Where(bs => bs.spawnCategories.Any(sc => childCategories.Contains(sc)))
                .ToList();
            return matched.Any() ? matched.RandomElement() : allAdult.RandomElement();
        }

        return allAdult.RandomElement();
    }

    /// <summary>
    /// Returns the skill gain a single backstory grants for a given skill (0 if none).
    /// BackstoryDef.skillGains is deterministic (no RNG), so this is a clean lookup.
    /// </summary>
    private static int SkillGainFrom(BackstoryDef backstory, SkillDef skill)
    {
        if (backstory?.skillGains == null) return 0;
        var gain = backstory.skillGains.FirstOrDefault(sg => sg.skill == skill);
        return gain != null ? gain.amount : 0;
    }

    /// <summary>
    /// Total skill gain from both backstory slots (childhood + adulthood) for a skill.
    /// </summary>
    public static int TotalBackstoryGain(Pawn pawn, SkillDef skill)
    {
        return SkillGainFrom(pawn.story?.Childhood, skill) + SkillGainFrom(pawn.story?.Adulthood, skill);
    }

    /// <summary>
    /// Re-bases a pawn's skill levels when a backstory changes, preserving the user's manual
    /// adjustment on top of the backstory contribution. For each skill:
    ///   userAdjustment = currentLevel - oldBackstoryGain
    ///   newLevel       = newBackstoryGain + userAdjustment
    /// This means changing backstory shifts each skill by exactly the difference between the
    /// old and new backstory gains, leaving the player's own edits intact. Passions are not
    /// touched here. Levels are clamped to the valid 0..20 range.
    /// </summary>
    /// <param name="pawn">The pawn whose skills are being re-based.</param>
    /// <param name="oldChildhood">Childhood backstory before the change.</param>
    /// <param name="oldAdulthood">Adulthood backstory before the change.</param>
    public static void ApplyBackstorySkillDelta(Pawn pawn, BackstoryDef oldChildhood, BackstoryDef oldAdulthood)
    {
        if (pawn.skills?.skills == null) return;

        foreach (var sr in pawn.skills.skills)
        {
            if (sr?.def == null) continue;

            var oldGain = SkillGainFrom(oldChildhood, sr.def) + SkillGainFrom(oldAdulthood, sr.def);
            var newGain = SkillGainFrom(pawn.story?.Childhood, sr.def) + SkillGainFrom(pawn.story?.Adulthood, sr.def);
            var delta = newGain - oldGain;
            if (delta == 0) continue;

            sr.levelInt = Mathf.Clamp(sr.levelInt + delta, 0, 20);
        }
    }
}
