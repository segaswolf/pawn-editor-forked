using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

[StaticConstructorOnStartup]
public class ListingMenu_Trait : ListingMenu<ListingMenu_Trait.TraitInfo>
{
    private static readonly List<TraitInfo> items;
    private static readonly Func<TraitInfo, string> labelGetter = t => t?.TraitDegreeData.LabelCap;
    private static readonly Func<TraitInfo, Pawn, string> descGetter = (t, p) => t?.TraitDegreeData.description.Formatted(p.Named("PAWN")).AdjustedFor(p);
    private static readonly List<Filter<TraitInfo>> filters;

	static ListingMenu_Trait()
	{
		items = DefDatabase<TraitDef>.AllDefs
		.SelectMany(traitDef =>
				traitDef.degreeDatas
					.Where(degree => !IsBlockedTrait(traitDef, degree))
					.Select(degree => new TraitInfo(traitDef, degree)))
		.ToList();
	
		filters = GetFilters();
	}

    public ListingMenu_Trait(Pawn pawn) : base(items, labelGetter, b => TryAdd(b, pawn),
        "PawnEditor.Choose".Translate() + " " + "Trait".Translate().ToLower(),
        b => descGetter(b, pawn), null, filters, pawn) { }

    private static AddResult TryAdd(TraitInfo traitInfo, Pawn pawn)
    {
        if (pawn.kindDef.disallowedTraits.NotNullAndContains(traitInfo.Trait.def)
         || pawn.kindDef.disallowedTraitsWithDegree.NotNullAndAny(t => t.def == traitInfo.Trait.def && t.degree == traitInfo.TraitDegreeData.degree)
         || (pawn.kindDef.requiredWorkTags != WorkTags.None
          && (traitInfo.Trait.def.disabledWorkTags & pawn.kindDef.requiredWorkTags) != WorkTags.None))
            return "PawnEditor.TraitDisallowedByKind".Translate(traitInfo.Trait.Label, pawn.kindDef.labelPlural);

        if (pawn.story.traits.allTraits.FirstOrDefault(tr => traitInfo.Trait.def.ConflictsWith(tr)) is { } trait)
            return "PawnEditor.TraitConflicts".Translate(traitInfo.Trait.Label, trait.Label);

        if (pawn.story.traits.allTraits.Any(tr => tr.def == traitInfo.Trait.def && tr.Degree == traitInfo.TraitDegreeData.degree))
            return "PawnEditor.AlreadyHas".Translate("Trait".Translate().ToLower(), traitInfo.Trait.Label);

        if (pawn.WorkTagIsDisabled(traitInfo.Trait.def.requiredWorkTags))
            return "PawnEditor.TraitWorkDisabled".Translate(pawn.Name.ToStringShort, traitInfo.Trait.def.requiredWorkTags.LabelTranslated(),
                traitInfo.Trait.Label);

        if (traitInfo.Trait.def.requiredWorkTypes?.FirstOrDefault(pawn.WorkTypeIsDisabled) is { } workType)
            return "PawnEditor.TraitWorkDisabled".Translate(pawn.Name.ToStringShort, workType.label, traitInfo.Trait.Label);

        if (HARCompat.Active && HARCompat.EnforceRestrictions && !HARCompat.CanGetTrait(traitInfo, pawn))
            return "PawnEditor.HARRestrictionViolated".Translate(pawn.Named("PAWN"), pawn.def.label.Named("RACE"), "PawnEditor.Wear".Named("VERB"),
                traitInfo.Trait.Label.Named("ITEM"));

        var newTrait = new Trait(traitInfo.Trait.def, traitInfo.TraitDegreeData.degree);
        ApplyTraitDeltaAndRefresh(pawn, () => pawn.story.traits.GainTrait(newTrait));
        return true;
    }

    public static void RemoveTraitAndRefresh(Pawn pawn, Trait trait)
    {
        if (pawn == null || trait == null)
            return;

        ApplyTraitDeltaAndRefresh(pawn, () => pawn.story.traits.RemoveTrait(trait, true));
    }

	private static readonly HashSet<string> blockedTraitLabels = new()
	{
		"Warcasket",
		"Shellcasket",
		"Mech warcasket",
		"Herculean"
	};
	
	private static bool IsBlockedTrait(TraitDef traitDef, TraitDegreeData degree)
	{
		if (traitDef == null || degree == null)
			return false;
	
		string label = degree.LabelCap?.ToString() ?? degree.label ?? traitDef.label ?? traitDef.defName;
		return blockedTraitLabels.Contains(label);
	}
	
    private static Dictionary<SkillDef, int> GetTraitSkillBonuses(Pawn pawn)
    {
        var result = new Dictionary<SkillDef, int>();

        if (pawn?.story?.traits?.allTraits == null)
            return result;

        foreach (var trait in pawn.story.traits.allTraits)
        {
            if (trait?.CurrentData?.skillGains == null)
                continue;

            foreach (var gain in trait.CurrentData.skillGains)
            {
                if (gain?.skill == null)
                    continue;

                if (!result.ContainsKey(gain.skill))
                    result[gain.skill] = 0;

                result[gain.skill] += gain.amount;
            }
        }

        return result;
    }

    private static void ApplyTraitDeltaAndRefresh(Pawn pawn, Action mutateTraits)
    {
        if (pawn == null || mutateTraits == null)
            return;

        var before = GetTraitSkillBonuses(pawn);

        mutateTraits();

        var after = GetTraitSkillBonuses(pawn);

        var allSkills = new HashSet<SkillDef>(before.Keys);
        allSkills.UnionWith(after.Keys);

        foreach (var skillDef in allSkills)
        {
            int oldBonus = before.TryGetValue(skillDef, out var oldValue) ? oldValue : 0;
            int newBonus = after.TryGetValue(skillDef, out var newValue) ? newValue : 0;
            int delta = newBonus - oldBonus;

            if (delta == 0)
                continue;

            SkillRecord record = pawn.skills?.GetSkill(skillDef);
            if (record == null)
                continue;

            record.levelInt += delta;

            if (record.levelInt < 0)
                record.levelInt = 0;
            else if (record.levelInt > 20)
                record.levelInt = 20;
        }

        RefreshPawnAfterTraitChange(pawn);
        PawnEditor.Notify_PointsUsed();
    }

    private static void RefreshPawnAfterTraitChange(Pawn pawn)
    {
        if (pawn == null)
            return;

        try
        {
            pawn.Notify_DisabledWorkTypesChanged();
        }
        catch
        {
        }

        try
        {
            pawn.workSettings?.Notify_DisabledWorkTypesChanged();
        }
        catch
        {
        }

        try
        {
            pawn.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty();
        }
        catch
        {
        }

        try
        {
            pawn.skills?.DirtyAptitudes();
        }
        catch
        {
        }

        try
        {
            pawn.drawer?.renderer?.SetAllGraphicsDirty();
        }
        catch
        {
        }
    }

    private static List<Filter<TraitInfo>> GetFilters()
    {
        var list = new List<Filter<TraitInfo>>();

        var modSourceDict =
            LoadedModManager.runningMods
               .Where(m => m.AllDefs.OfType<TraitDef>().Any())
               .ToDictionary<ModContentPack, string, Func<TraitInfo, bool>>(m => m.Name, m => traitInfo =>
                    traitInfo.Trait.def.modContentPack?.Name == m.Name);
        list.Add(new Filter_Dropdown<TraitInfo>("Source".Translate(), modSourceDict, false, "PawnEditor.SourceDesc".Translate()));

        return list;
    }

    public class TraitInfo
    {
        public readonly Trait Trait;
        public readonly TraitDegreeData TraitDegreeData;

        public TraitInfo(TraitDef traitDef, TraitDegreeData degree)
        {
            TraitDegreeData = degree;
            Trait = new Trait(traitDef, degree.degree);
        }
    }
}
