using System.Collections.Generic;
using Verse;

namespace PawnEditor;

/// <summary>
/// Leveled hediffs (repair probe, jammer kit...) keep their level in a field, and the game rewrites the severity
/// from it every tick. Two of the same def on the same part merge on add (Hediff.TryMergeWith), adding up
/// severities but not levels. So blueprints and duplicating add leveled hediffs here, stacking the level the
/// way the game does when a second probe goes in. Reported by ExoticButts (repair probe x6 back as x1, 3 Nov).
/// </summary>
public static class LeveledHediffUtility
{
    /// <summary>
    /// Adds <paramref name="hediff"/> at <paramref name="level"/>. Returns false for hediffs that aren't leveled,
    /// which the caller adds as usual.
    /// <para>If the same def is already on that part, what happens depends on who put it there:</para>
    /// <list type="bullet">
    /// <item>An earlier entry of this same load (<paramref name="addedThisPass"/>): stack, like a second repair
    /// probe going in. Syliki's blueprint has two probe entries that make one x3.</item>
    /// <item>Anything else, typically another hediff's side effect (adding the psylink makes VPE create its
    /// psycast hediff): take the saved level instead of adding to it. Stacking there doubled VPE's level
    /// (Sylvia: 6 came back as 12, 12 Nov fixture tests).</item>
    /// </list>
    /// </summary>
    public static bool TryAddLeveled(Pawn pawn, Hediff hediff, int level, ISet<Hediff> addedThisPass)
    {
        if (hediff is not Hediff_Level leveled) return false;
        level = System.Math.Max(1, level);

        foreach (var existing in pawn.health.hediffSet.hediffs)
        {
            if (existing is Hediff_Level same && existing.def == hediff.def && existing.Part == hediff.Part)
            {
                same.level = addedThisPass.Contains(existing) ? same.level + level : level;
                same.Severity = same.level;
                addedThisPass.Add(existing);
                return true;
            }
        }

        // The field directly, not SetLevelTo: some subclasses (psylink) grant things in ChangeLevel.
        leveled.level = level;
        leveled.Severity = level;
        pawn.health.hediffSet.AddDirect(leveled);
        addedThisPass.Add(leveled);
        return true;
    }

    /// <summary>The level to save: the field, since the severity only catches up on the next tick.</summary>
    public static int? LevelOf(Hediff hediff) => hediff is Hediff_Level leveled ? leveled.level : null;
}
