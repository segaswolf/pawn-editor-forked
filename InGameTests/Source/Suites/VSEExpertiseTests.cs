using System.Collections.Generic;
using System.Linq;
using PawnEditor;
using RimWorld;
using Verse;

namespace PawnEditorInGameTests;

/// <summary>
/// Vanilla Skills Expanded expertise, as the Skills column edits it: VSE's own rules decide what may be
/// picked, switching keeps the level, and Cancel undoes it. Skipped when VSE is not loaded.
/// The limit checks read VSE's "max expertise" setting (1 to 75) from the profile instead of assuming one.
/// </summary>
public static class VSEExpertiseTests
{
    [InGameTest]
    public static void Expertise_IsEditable()
    {
        RequireVse();
        Check.That(VSECompat.ExpertiseDefs.Count > 1, $"only {VSECompat.ExpertiseDefs.Count} expertise defs found");
    }

    [InGameTest]
    public static void Tooltip_ShowsWhatItChanges()
    {
        RequireVse();
        Check.That(VSECompat.ExpertiseDefs.Any(def => !VSECompat.ExpertiseEffects(def, 1).NullOrEmpty()),
            "no expertise reported its effects (ExpertiseDef.Effects not found?)");
    }

    [InGameTest]
    public static void LowSkill_IsRefused()
    {
        RequireVse();
        var pawn = TestPawns.Colonist();
        var def = UsableExpertise(pawn).First();
        PrepareSkills(pawn, 0);

        Check.That(!VSECompat.CanApplyExpertise(def, pawn, out var reason), $"{def.defName} was allowed with its skill at 0");
        Check.That(!reason.NullOrEmpty(), "VSE refused without giving a reason to show in the menu");
    }

    [InGameTest]
    public static void Limit_FollowsVseSetting()
    {
        RequireVse();
        var max = RequireReadableLimit();
        var pawn = TestPawns.Colonist();
        var count = FillToLimit(pawn);

        Check.That(count <= max, $"the pawn got {count} expertise with VSE's limit at {max}");
        Check.Requires(count == max, $"only {count} of {max} could be taken (one per skill unless VSE allows overlap)");
        Check.That(VSECompat.ExpertiseDefs.All(def => !VSECompat.CanApplyExpertise(def, pawn, out _)), $"VSE still allowed more at its limit of {max}");
    }

    [InGameTest]
    public static void Level_StaysWithinVseRange()
    {
        RequireVse();
        var pawn = TestPawns.Colonist();
        PrepareSkills(pawn, 20);
        var def = UsableExpertise(pawn).First();
        VSECompat.AddExpertise(pawn, def);

        VSECompat.SetExpertiseLevel(pawn, def, VSECompat.MaxExpertiseLevel + 5);
        Check.Equal(VSECompat.MaxExpertiseLevel, VSECompat.CurrentExpertise(pawn)[0].level, "level above the maximum");
        VSECompat.SetExpertiseLevel(pawn, def, -3);
        Check.Equal(0, VSECompat.CurrentExpertise(pawn)[0].level, "level below zero");
    }

    [InGameTest]
    public static void Cancel_UndoesExpertise()
    {
        RequireVse();
        var pawn = TestPawns.Colonist();
        PrepareSkills(pawn, 20);
        var snapshot = new PawnSnapshot(pawn);

        VSECompat.AddExpertise(pawn, UsableExpertise(pawn).First());
        Check.Equal(1, VSECompat.CurrentExpertise(pawn).Count, "expertise before Cancel");

        snapshot.Restore();
        Check.Equal(0, VSECompat.CurrentExpertise(pawn).Count, "expertise after Cancel");
    }

    [InGameTest]
    public static void List_ShowsAllowedFirst_AndKeepsTheRefused()
    {
        RequireVse();
        var pawn = TestPawns.Colonist();
        PrepareSkills(pawn, 0);
        pawn.skills.GetSkill(SkillOf(UsableExpertise(pawn).First())).Level = 20;

        var entries = ListingMenu_Expertise.BuildEntries(pawn);
        Check.That(entries.Count > 1, "the expertise list is empty");
        Check.That(entries[0].Allowed, "the first entry is refused although one skill is at 20");
        Check.That(!entries.Last().Allowed, "expertise for skills at 0 should stay listed, refused");
        Check.That(entries.SkipWhile(e => e.Allowed).All(e => !e.Allowed), "allowed and refused entries are mixed");
    }

    [InGameTest]
    public static void List_AtTheLimit_AllowsNothingMore()
    {
        RequireVse();
        var max = RequireReadableLimit();
        var pawn = TestPawns.Colonist();
        var count = FillToLimit(pawn);
        Check.Requires(count == max, $"only {count} of {max} could be taken (one per skill unless VSE allows overlap)");

        var owned = VSECompat.CurrentExpertise(pawn).Select(e => e.def).ToList();
        var entries = ListingMenu_Expertise.BuildEntries(pawn);
        Check.That(entries.All(e => !owned.Contains(e.Def)), "an owned expertise is still offered on the left");

        // Same rule at any limit: nothing can be added until one is removed.
        Check.Equal(0, entries.Count(e => e.Allowed), $"entries allowed at the limit of {max}");
    }

    [InGameTest]
    public static void List_SearchAndSort()
    {
        RequireVse();
        var pawn = TestPawns.Colonist();
        PrepareSkills(pawn, 20);
        var skill = SkillOf(UsableExpertise(pawn).First());
        var search = skill.LabelCap.ToString();

        var found = ListingMenu_Expertise.BuildEntries(pawn, search: search);
        Check.That(found.Count > 0, $"searching '{search}' found nothing");
        Check.That(found.All(e => e.Label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0), $"searching '{search}' listed entries that don't match");

        var sorted = ListingMenu_Expertise.BuildEntries(pawn, alphabetical: true).Where(e => e.Allowed).Select(e => e.Label).ToList();
        Check.That(sorted.SequenceEqual(sorted.OrderBy(label => label)), "A-Z left the allowed entries out of order");
    }

    /// <summary>The isolated test profile copies your VSE settings, so the checks read the limit instead of assuming one.</summary>
    private static int RequireReadableLimit()
    {
        var max = VSECompat.MaxExpertise;
        Check.Requires(max.HasValue, "VSE's max expertise setting could not be read");
        return max.Value;
    }

    /// <summary>Every skill at 20, then takes whatever VSE allows until it refuses. Returns how many it took.</summary>
    private static int FillToLimit(Pawn pawn)
    {
        PrepareSkills(pawn, 20);
        for (var guard = 0; guard < 100; guard++)
        {
            var next = VSECompat.ExpertiseDefs.FirstOrDefault(def => VSECompat.CanApplyExpertise(def, pawn, out _));
            if (next == null) break;
            VSECompat.AddExpertise(pawn, next);
        }
        return VSECompat.CurrentExpertise(pawn).Count;
    }

    private static void RequireVse()
    {
        Check.Requires(VSECompat.Active, "Vanilla Skills Expanded not loaded");
        Check.That(VSECompat.CanEditExpertise, "VSE is loaded but its expertise API was not found");
    }

    /// <summary>Every skill at <paramref name="level"/> with a major passion, and no expertise.</summary>
    private static void PrepareSkills(Pawn pawn, int level)
    {
        foreach (var skill in pawn.skills.skills)
        {
            skill.Level = level;
            skill.passion = Passion.Major;
        }
        VSECompat.SetExpertise(pawn, new List<VSECompat.ExpertiseSnapshot>());
    }

    /// <summary>Expertise whose skill this pawn can use (a disabled skill can't reach level 15).</summary>
    private static IEnumerable<Def> UsableExpertise(Pawn pawn) =>
        VSECompat.ExpertiseDefs.Where(def => SkillOf(def) is { } skill && !pawn.skills.GetSkill(skill).TotallyDisabled);

    private static SkillDef SkillOf(Def expertise) => VSECompat.SkillOf(expertise);
}
