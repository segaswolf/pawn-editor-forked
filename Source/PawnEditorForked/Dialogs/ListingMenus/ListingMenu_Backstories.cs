using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

[StaticConstructorOnStartup]
public class ListingMenu_Backstories : ListingMenu<BackstoryDef>
{
    private static readonly Dictionary<BackstorySlot, List<BackstoryDef>> backstoriesBySlot = new();

    static ListingMenu_Backstories()
    {
        var items = DefDatabase<BackstoryDef>.AllDefsListForReading;
        backstoriesBySlot.Add(BackstorySlot.Adulthood, items.Where(b => b.slot == BackstorySlot.Adulthood).ToList());
        backstoriesBySlot.Add(BackstorySlot.Childhood, items.Where(b => b.slot == BackstorySlot.Childhood).ToList());
    }

    public ListingMenu_Backstories(Pawn pawn, BackstorySlot backstorySlot) : base(backstoriesBySlot[backstorySlot], b => b.TitleFor(pawn.gender).CapitalizeFirst(), b => TryAdd(b, pawn),
        "PawnEditor.Choose".Translate() + " " + "Backstory".Translate().ToLower(),
        b => DoToolTipFor(b, pawn), null, GetFilters(), pawn)
    {
    }

    /// <summary>
    /// Same class of bug as the one that poisoned TexPawnEditor: ToDictionary throws on a NULL key and
    /// on a DUPLICATE key, and with a big modlist both are easy to hit (a blank spawn category, two
    /// skills from different mods sharing a label). One throw here kills the whole backstory picker for
    /// something purely cosmetic. So we build defensively: skip blanks, keep the first of each
    /// duplicate, and never take the menu down over a filter dropdown.
    /// </summary>
    private static Dictionary<string, Func<BackstoryDef, bool>> SafeFilterDict<T>(
        IEnumerable<T> source, Func<T, string> labelGetter, Func<T, Func<BackstoryDef, bool>> predicateGetter)
    {
        var result = new Dictionary<string, Func<BackstoryDef, bool>>();
        foreach (var entry in source)
        {
            if (entry == null) continue;
            var label = labelGetter(entry);
            if (label.NullOrEmpty() || result.ContainsKey(label)) continue;
            result[label] = predicateGetter(entry);
        }

        return result;
    }

    private static AddResult TryAdd(BackstoryDef backstoryDef, Pawn pawn)
    {
        // Capture the backstories BEFORE the change so we can compute the skill delta.
        var oldChildhood = pawn.story.Childhood;
        var oldAdulthood = pawn.story.Adulthood;

        if (backstoryDef.slot == BackstorySlot.Childhood)
            pawn.story.Childhood = backstoryDef;
        else if (!pawn.ageTracker.Adult)
            return "PawnEditor.NoAdultOnChild".Translate(backstoryDef.LabelCap);
        else
            pawn.story.Adulthood = backstoryDef;

        // Re-base skills: shift each skill by the difference between the old and new backstory
        // gains, preserving the player's manual adjustments. Passions are untouched, so the
        // long-standing "passions randomize on backstory change" issue stays fixed without
        // also wiping the backstory's skill contribution (which the old save/restore did).
        BackstoryUtility.ApplyBackstorySkillDelta(pawn, oldChildhood, oldAdulthood);

        pawn.Notify_DisabledWorkTypesChanged();

        // Life Lessons resolves a pawn's proficiencies from their backstory. Changing the
        // backstory here doesn't go through LL's normal hooks, so reinitialize explicitly:
        // this re-resolves which proficiencies the new backstory grants (adding/removing as
        // needed) and recalculates the stat/skill modifiers they provide.
        if (LifeLessonsCompat.Active)
        {
            LifeLessonsCompat.ReinitializeComp(pawn);
            LifeLessonsCompat.RefreshModifiers(pawn);
        }

        PawnEditor.Notify_PointsUsed();
        return true;
    }

    private static List<Filter<BackstoryDef>> GetFilters()
    {
        var list = new List<Filter<BackstoryDef>>();

        list.Add(new Filter_Toggle<BackstoryDef>("PawnEditor.ShuffableOnly".Translate(), item => item.shuffleable, true,
            "PawnEditor.ShuffableOnlyDesc".Translate()));

        // var backstorySlotDict = Enum.GetValues(typeof(BackstorySlot))
        //     .Cast<BackstorySlot>()
        //     .ToDictionary<BackstorySlot, string, Func<BackstoryDef, bool>>(bs => bs.ToString(), bs => bd => bd.slot == bs);
        // list.Add(
        //     new Filter_Dropdown<BackstoryDef>("PawnEditor.BackstorySlot".Translate(), backstorySlotDict, false, "PawnEditor.BackstorySlotDesc".Translate()));

        for (var i = 0; i < 5; i++)
        {
            var spawnCategoriesDict = SafeFilterDict(
                DefDatabase<BackstoryDef>.AllDefs.SelectMany(bd => bd.spawnCategories).Distinct(),
                sc => sc.ConvertCamelCase(),
                sc => bd => bd.spawnCategories.Contains(sc));
            list.Add(
                new Filter_Dropdown<BackstoryDef>("PawnEditor.BackstoryType".Translate(), spawnCategoriesDict, false, "PawnEditor.BackstoryTypeDesc".Translate()));
        }

        for (var i = 0; i < 5; i++)
        {
            var skillGainDict = SafeFilterDict(
                DefDatabase<SkillDef>.AllDefs.Where(sd => backstoriesBySlot
                    .SelectMany(p => p.Value).Any(bd => bd.skillGains.Any(sg => sg.skill == sd))),
                sd => sd.skillLabel.CapitalizeFirst(),
                sd => bd => bd.skillGains.Any(sg => sg.skill == sd && sg.amount > 0));
            list.Add(new Filter_Dropdown<BackstoryDef>("PawnEditor.SkillGain".Translate(), skillGainDict, false, "PawnEditor.SkillGainDesc".Translate()));
        }

        list.Add(new Filter_Toggle<BackstoryDef>("PawnEditor.WorkDisables".Translate(), item => item.workDisables == WorkTags.None, false,
            "PawnEditor.WorkDisables".Translate()));

        for (var i = 0; i < 5; i++)
        {
            var workDict = SafeFilterDict(
                DefDatabase<BackstoryDef>.AllDefs.SelectMany(bd => bd.DisabledWorkTypes).Distinct(),
                dwt => dwt.ToStringSafe().ConvertCamelCase(),
                dwt => bd => bd.DisabledWorkTypes.Contains(dwt));
            list.Add(new Filter_Dropdown<BackstoryDef>("PawnEditor.DisabledWorkTypes".Translate(), workDict, false, "PawnEditor.DisabledWorkTypesDesc".Translate()));
        }

        list.Add(new Filter_Toggle<BackstoryDef>("PawnEditor.SkillLose".Translate(), item => item.skillGains.All(sg => sg.amount > 0), false,
            "PawnEditor.SkillLose".Translate()));

        for (var i = 0; i < 5; i++)
        {
            var skillGainDict = SafeFilterDict(
                DefDatabase<SkillDef>.AllDefs.Where(sd => backstoriesBySlot
                    .SelectMany(p => p.Value).Any(bd => bd.skillGains.Any(sg => sg.skill == sd))),
                sd => sd.skillLabel.CapitalizeFirst(),
                sd => bd => bd.skillGains.Any(sg => sg.skill == sd && sg.amount < 0));
            list.Add(new Filter_Dropdown<BackstoryDef>("PawnEditor.SkillLoses".Translate(), skillGainDict, false, "PawnEditor.SkillLosesDesc".Translate()));
        }

        list.Add(new Filter_ModSource<BackstoryDef>());

        return list;
    }

    private static string DoToolTipFor(BackstoryDef backstoryDef, Pawn pawn)
    {
        string output = backstoryDef.FullDescriptionFor(pawn).Resolve();
        string cats = string.Join(", ", backstoryDef.spawnCategories.Select(sc => sc.ConvertCamelCase()));
        output += "\n\n" + "PawnEditor.Categories".Translate().CapitalizeFirst() + ": \n" + cats;
        return output;
    }
}