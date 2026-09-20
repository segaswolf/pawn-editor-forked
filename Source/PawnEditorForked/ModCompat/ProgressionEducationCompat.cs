using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Support for Progression: Education (ferny.progressioneducation).
///
/// That mod expresses a pawn's proficiencies as ordinary traits, but they are not interchangeable:
/// they belong to TRACKS, and within a track only one TIER applies at a time. Verified from its defs
/// (1.6/Defs/ProficiencyDefs/Proficiencies.xml):
///
///   ProgressionEducation.ProficiencyDef      -> a track ("weapon", "vehicle", "speech") with a
///                                               &lt;tiers&gt; list, ordered from lowest to highest
///   ProgressionEducation.ProficiencyTierDef  -> one tier, whose &lt;traitDef&gt; is the actual trait
///                                               (e.g. speech: PE_MuteProficiency ->
///                                               PE_BasicSpeechProficiency -> PE_FluentSpeechProficiency)
///
/// Those traits have commonality 0, so RimWorld's own generator never rolls them: a plain trait reroll
/// can only ever drop them, never produce a sensible replacement. This class rerolls them the way the
/// mod means them to work — swapping the pawn's tier WITHIN its own track, so a mute pawn can come back
/// fluent, but never ends up with, say, a vehicle tier instead of a speech one.
///
/// All reflection by type name: with the mod absent, everything here is inert.
/// </summary>
public static class ProgressionEducationCompat
{
    private const string ProficiencyDefTypeName = "ProgressionEducation.ProficiencyDef";
    private const string TierDefTypeName = "ProgressionEducation.ProficiencyTierDef";

    private static readonly Type ProficiencyDefType = AccessTools.TypeByName(ProficiencyDefTypeName);
    private static readonly Type TierDefType = AccessTools.TypeByName(TierDefTypeName);

    /// <summary>track -> the trait of each tier in it, in the mod's own order.</summary>
    private static List<List<TraitDef>> tracks;

    public static bool Active => ProficiencyDefType != null && TierDefType != null;

    private static void EnsureBuilt()
    {
        if (tracks != null) return;
        tracks = new List<List<TraitDef>>();
        if (!Active) return;

        try
        {
            var tiersField = AccessTools.Field(ProficiencyDefType, "tiers");
            var traitDefField = AccessTools.Field(TierDefType, "traitDef");
            if (tiersField == null || traitDefField == null)
            {
                Log.Warning("[Pawn Editor] Progression: Education compatibility could not read its proficiency "
                            + "defs; proficiency rerolling is disabled.");
                return;
            }

            foreach (var proficiency in AllDefsOf(ProficiencyDefType))
            {
                if (tiersField.GetValue(proficiency) is not IEnumerable tierList) continue;

                var traitsInTrack = new List<TraitDef>();
                foreach (var tier in tierList)
                {
                    // A tier can be absent entirely (its <li> has a MayRequire for a mod you don't run).
                    if (tier == null) continue;
                    if (traitDefField.GetValue(tier) is TraitDef trait && !traitsInTrack.Contains(trait))
                        traitsInTrack.Add(trait);
                }

                // A single-tier track has nothing to reroll between.
                if (traitsInTrack.Count > 1) tracks.Add(traitsInTrack);
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Progression: Education compatibility failed to map proficiency tracks: {ex.Message}");
            tracks = new List<List<TraitDef>>();
        }
    }

    /// <summary>DefDatabase&lt;T&gt;.AllDefsListForReading for a type only known at runtime.</summary>
    private static IEnumerable<object> AllDefsOf(Type defType)
    {
        var prop = typeof(DefDatabase<>).MakeGenericType(defType)
            .GetProperty("AllDefsListForReading", BindingFlags.Public | BindingFlags.Static);
        return prop?.GetValue(null) is IEnumerable defs ? defs.Cast<object>() : Enumerable.Empty<object>();
    }

    /// <summary>True if this trait is one of the mod's proficiency traits.</summary>
    public static bool IsProficiencyTrait(TraitDef def)
    {
        if (!Active || def == null) return false;
        EnsureBuilt();
        return tracks.Any(track => track.Contains(def));
    }

    /// <summary>
    /// The proficiency traits the pawn already has that belong to the SAME track as the given one.
    ///
    /// The mod's trait defs declare no conflictingTraits/exclusionTags at all (checked: zero in
    /// Traits.xml) — exclusivity lives in its own progression logic. So nothing stops the editor from
    /// handing a pawn both "mute" and "fluent speech" at once, which is nonsense. Callers use this to
    /// clear the old tier when the player picks a new one.
    /// </summary>
    public static List<Trait> GetSameTrackTraits(Pawn pawn, TraitDef adding)
    {
        var result = new List<Trait>();
        if (!Active || pawn?.story?.traits?.allTraits == null || adding == null) return result;
        EnsureBuilt();

        var track = tracks.FirstOrDefault(t => t.Contains(adding));
        if (track == null) return result;

        foreach (var trait in pawn.story.traits.allTraits)
            if (trait?.def != null && trait.def != adding && track.Contains(trait.def))
                result.Add(trait);

        return result;
    }

    /// <summary>True if the pawn currently holds at least one proficiency trait we could reroll.</summary>
    public static bool HasProficiencies(Pawn pawn)
    {
        if (!Active || pawn?.story?.traits?.allTraits == null) return false;
        EnsureBuilt();
        return pawn.story.traits.allTraits.Any(t => t?.def != null && IsProficiencyTrait(t.def));
    }

    /// <summary>
    /// For every proficiency track the pawn has a trait in, swap that trait for a random OTHER tier of
    /// the same track. Tracks the pawn has nothing in are left alone — we reroll what the pawn has, we
    /// don't hand out new proficiencies it was never meant to have.
    /// </summary>
    public static void RandomizeProficiencies(Pawn pawn)
    {
        if (!Active || pawn?.story?.traits == null) return;
        EnsureBuilt();

        try
        {
            foreach (var track in tracks)
            {
                var current = pawn.story.traits.allTraits.FirstOrDefault(t => t?.def != null && track.Contains(t.def));
                if (current == null) continue;

                // Pick a different tier; if the track only had this one available, leave it be.
                var candidates = track.Where(def => def != current.def).ToList();
                if (candidates.Count == 0) continue;

                var replacement = candidates.RandomElement();

                // Carry over how the old tier was flagged instead of inventing it: if the pawn's
                // proficiency was scenario-forced, its replacement should be too, and if it wasn't,
                // marking the new one as forced could stop the mod from progressing it later.
                var wasScenForced = current.ScenForced;

                pawn.story.traits.RemoveTrait(current, true);
                if (!pawn.story.traits.HasTrait(replacement))
                    pawn.story.traits.GainTrait(new Trait(replacement, 0, wasScenForced));
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Could not reroll Progression: Education proficiencies: {ex.Message}");
        }
    }
}
