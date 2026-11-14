using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Copy a pawn's traits and paste them onto another (requested by Blackthorn, Steam, 11 Nov).
/// </summary>
/// <remarks>
/// <para>Pasting replaces the target's own traits with the copied ones, Progression proficiencies included (a pasted
/// "fluent speech" replaces "non-speaker"). Traits that aren't the pawn's own are neither copied nor touched:
/// granted by a gene, Isekai rank and titles (they follow the level) and the ones the trait picker blocks.</para>
/// <para>The player has the last word on fights (Segas, 14 Nov): <see cref="Plan"/> lists every copied trait that
/// clashes with one of the target's, or that a target gene would suppress, and the editor asks before pasting.
/// For clashes with the target's own traits there are two answers for all of them at once, keep the target's or
/// use the copied; a clash with a gene trait always keeps the gene's (changing it means changing the gene).</para>
/// </remarks>
public static class TraitClipboard
{
    private static readonly List<(TraitDef def, int degree)> copied = new();
    private static string copiedFrom;

    public static bool HasTraits => copied.Count > 0;
    public static string CopiedFrom => copiedFrom;

    /// <summary>What pasting onto a pawn would run into. Empty lists: nothing to ask.</summary>
    public sealed class PastePlan
    {
        /// <summary>A copied trait against one of the target's own: the player picks which stays.</summary>
        public readonly List<(TraitDef def, int degree, Trait rival)> OwnClashes = new();
        /// <summary>A copied trait against a gene, Isekai or blocked trait: the target's stays.</summary>
        public readonly List<(TraitDef def, int degree, Trait rival)> FixedClashes = new();
        /// <summary>A copied trait a gene of the target will suppress: it goes on, but inactive.</summary>
        public readonly List<(TraitDef def, int degree, Gene gene)> Suppressed = new();

        public bool NeedsAsking => OwnClashes.Count > 0 || FixedClashes.Count > 0 || Suppressed.Count > 0;
    }

    /// <summary>The traits that make up a pawn's "loadout": everything but the ones listed above.</summary>
    public static bool IsOwn(Trait trait) =>
        trait?.def != null && trait.sourceGene == null
        && !IsekaiLevelingCompat.IsSystemTrait(trait.def)
        && !ListingMenu_Trait.IsBlockedTrait(trait.def, trait.CurrentData);

    public static int Copy(Pawn pawn)
    {
        copied.Clear();
        copiedFrom = pawn?.Name?.ToStringShort ?? pawn?.LabelShortCap;
        var traits = pawn?.story?.traits?.allTraits;
        if (traits != null)
            copied.AddRange(traits.Where(IsOwn).Select(t => (t.def, t.Degree)));
        return copied.Count;
    }

    public static PastePlan Plan(Pawn pawn)
    {
        var plan = new PastePlan();
        var traits = pawn?.story?.traits?.allTraits;
        if (traits == null) return plan;

        foreach (var (def, degree) in copied)
        {
            // Same def at another degree (industrious vs lazy) fights too; the game only checks different defs.
            foreach (var rival in traits.Where(t => def.ConflictsWith(t) || (t.def == def && t.Degree != degree)))
                (IsOwn(rival) ? plan.OwnClashes : plan.FixedClashes).Add((def, degree, rival));

            var suppressor = pawn.genes?.GenesListForReading.FirstOrDefault(g =>
                g.Active && g.def.suppressedTraits != null && g.def.suppressedTraits.Any(x => x.def == def && x.degree == degree));
            if (suppressor != null) plan.Suppressed.Add((def, degree, suppressor));
        }
        return plan;
    }

    /// <param name="keepTargetOnClash">For clashes with the target's own traits: keep the target's (true) or use
    /// the copied (false). Clashes with gene traits always keep the gene's.</param>
    /// <returns>The copied traits that didn't go on, each with the reason.</returns>
    public static List<string> Paste(Pawn pawn, bool keepTargetOnClash)
    {
        var skipped = new List<string>();
        var traits = pawn?.story?.traits;
        if (traits == null || copied.Count == 0) return skipped;

        var plan = Plan(pawn);
        var keptRivals = keepTargetOnClash ? plan.OwnClashes.Select(c => c.rival).ToHashSet() : new HashSet<Trait>();

        foreach (var trait in traits.allTraits.Where(t => IsOwn(t) && !keptRivals.Contains(t)).ToList())
            ListingMenu_Trait.RemoveTraitAndRefresh(pawn, trait);

        foreach (var (def, degree) in copied)
        {
            var data = def.degreeDatas?.FirstOrDefault(d => d.degree == degree);
            if (data == null) continue;
            if (keepTargetOnClash && plan.OwnClashes.Any(c => c.def == def && c.degree == degree))
            {
                skipped.Add("PawnEditor.TraitKeptTargets".Translate(data.LabelCap));
                continue;
            }
            var result = ListingMenu_Trait.TryAdd(new ListingMenu_Trait.TraitInfo(def, data), pawn);
            if (!result.IsSuccess())
                skipped.Add(result is FailureInfo { Reason: { } reason } && !reason.NullOrEmpty() ? reason : data.LabelCap.ToString());
        }

        // The on-screen message fades; the log keeps the whole picture to check afterwards.
        var kept = traits.allTraits.Where(t => !IsOwn(t)).Select(t => t.LabelCap.ToString()).ToList();
        var own = traits.allTraits.Where(IsOwn).Select(t => t.LabelCap.ToString()).ToList();
        Log.Message($"[Pawn Editor] Pasted the traits of {copiedFrom} onto {pawn.Name?.ToStringShort ?? pawn.LabelShortCap}. "
                    + $"Now: {Join(own)}. Left as they were (genes, mod systems, blocked): {Join(kept)}. Skipped: {Join(skipped)}.");
        return skipped;
    }

    // Mods color some labels (<color=...>Rank F</color>): the log gets plain text.
    private static string Join(List<string> items) => items.Count == 0 ? "none" : string.Join(", ", items.Select(i => i.StripTags()));
}
