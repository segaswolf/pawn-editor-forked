using System.Linq;
using RimWorld;
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
}
