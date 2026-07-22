using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[StaticConstructorOnStartup]
// ToDo: Separate list of mechs/ animals
public class ListingMenu_Hediffs : ListingMenu<HediffDef>
{
    private static readonly List<HediffDef> items;
    // Different hediffs (from different mods, or vanilla variants) often share the SAME label, so the
    // picker looked full of duplicates with no way to tell them apart. We keep them all — they can
    // behave differently — and append a disambiguator instead. Built in the static constructor.
    private static Dictionary<HediffDef, string> labelSuffixes = new();

    // Prosthetics that ship as separate "(left)" / "(right)" defs are collapsed into ONE list entry.
    // sideVariants maps that entry to every side variant, labelOverrides holds the side-less label, and
    // currentSideVariant is the side the user picked in the footer.
    private static Dictionary<HediffDef, List<(string side, HediffDef def)>> sideVariants = new();
    private static Dictionary<HediffDef, string> labelOverrides = new();
    public static HediffDef currentSideVariant;

    private static readonly Func<HediffDef, string> labelGetter = d =>
    {
        var label = labelOverrides.TryGetValue(d, out var overridden) ? overridden : d.LabelCap.ToString();
        return labelSuffixes.TryGetValue(d, out var suffix) ? label + suffix : label;
    };
    private static readonly Func<HediffDef, string> descGetter = d =>
    {
        // Modules need a host part. Say it in the description so the user knows BEFORE clicking.
        var moduleInfo = ModularModulesCompat.GetModule(ResolveSideDef(d));
        return moduleInfo == null
            ? d.Description
            : d.Description + "\n\n" + "PawnEditor.ModuleRequires".Translate(ModularModulesCompat.RequirementLabel(moduleInfo));
    };
    private static readonly List<Filter<HediffDef>> filters;

    private static readonly HashSet<TechLevel> possibleTechLevels;
    public static readonly Dictionary<HediffDef, (List<BodyPartDef>, List<BodyPartGroupDef>)> defaultBodyParts;

    public static BodyPartRecord currentBodyPart;

    static ListingMenu_Hediffs()
    {
        defaultBodyParts = new();
        currentBodyPart = null;
        foreach (var recipe in DefDatabase<RecipeDef>.AllDefs.Where(recipe => recipe.addsHediff != null))
            AddDefaultBodyParts(recipe.addsHediff, recipe.appliedOnFixedBodyParts, recipe.appliedOnFixedBodyPartGroups);

        possibleTechLevels = new();
        foreach (var hediff in DefDatabase<HediffDef>.AllDefsListForReading)
        {
            if (hediff.spawnThingOnRemoved is { techLevel: var level })
                possibleTechLevels.Add(level);
            if (hediff.defaultInstallPart != null)
                AddDefaultBodyParts(hediff, new List<BodyPartDef> { hediff.defaultInstallPart }, null);
        }

        // Fallback for parts whose install recipe doesn't declare addsHediff (some mods install via a
        // custom recipe worker), which left them with no target body part — e.g. "Archotech arm
        // (modular)" asked "can't find valid part to mount" while the plain Archotech arm worked.
        // Link such a hediff through the ITEM it drops when removed: any recipe that consumes that item
        // and installs on fixed body parts tells us where it goes.
        var partsByItem = new Dictionary<ThingDef, (List<BodyPartDef>, List<BodyPartGroupDef>)>();
        foreach (var recipe in DefDatabase<RecipeDef>.AllDefs)
        {
            if (recipe.appliedOnFixedBodyParts.NullOrEmpty() && recipe.appliedOnFixedBodyPartGroups.NullOrEmpty()) continue;
            if (recipe.ingredients == null) continue;
            foreach (var ing in recipe.ingredients)
            {
                if (ing?.filter == null) continue;
                foreach (var td in ing.filter.AllowedThingDefs)
                    if (td != null && !partsByItem.ContainsKey(td))
                        partsByItem[td] = (recipe.appliedOnFixedBodyParts, recipe.appliedOnFixedBodyPartGroups);
            }
        }

        foreach (var hediff in DefDatabase<HediffDef>.AllDefsListForReading)
        {
            if (hediff.spawnThingOnRemoved == null || defaultBodyParts.ContainsKey(hediff)) continue;
            if (partsByItem.TryGetValue(hediff.spawnThingOnRemoved, out var fromItem))
                AddDefaultBodyParts(hediff, fromItem.Item1, fromItem.Item2);
        }


        // Collapse EXACT duplicates. Several mods ship hediffs that are identical in label, description,
        // class and actual effect (stat/capacity/pain changes), which turned the picker into a wall of
        // repeated entries doing the same thing. Keep ONE per effect signature — preferring the vanilla
        // definition when one of them is vanilla — and let the suffix logic below disambiguate only what
        // GENUINELY differs.
        var indexBySignature = new Dictionary<string, int>();
        var deduped = new List<HediffDef>();
        foreach (var def in DefDatabase<HediffDef>.AllDefsListForReading)
        {
            var signature = EffectSignature(def);
            if (indexBySignature.TryGetValue(signature, out var index))
            {
                if (IsVanilla(def) && !IsVanilla(deduped[index])) deduped[index] = def;
                continue;
            }
            indexBySignature[signature] = deduped.Count;
            deduped.Add(def);
        }
        items = deduped;

        // Collapse "(left)" / "(right)" pairs into a single entry: listing the same prosthetic twice is
        // noise. The user picks the side in the footer (like vanilla surgery) and we add the matching def.
        sideVariants = new Dictionary<HediffDef, List<(string, HediffDef)>>();
        labelOverrides = new Dictionary<HediffDef, string>();

        // The group key is NOT just the side-less label: two mods can both ship a "Bionic arm (left)"
        // that behaves differently, and merging those would hide one of them. Class, source mod,
        // description and part efficiency have to match too before we treat them as one item.
        var sideGroups = new Dictionary<string, (string baseLabel, List<(string side, HediffDef def)> defs)>();
        foreach (var def in items)
        {
            var side = SideOfLabel(def.label, out var baseLabel);
            if (side == null) continue;

            var key = string.Join("|", baseLabel, def.hediffClass?.FullName, def.modContentPack?.PackageId,
                def.description, def.addedPartProps?.partEfficiency.ToString("F3"));
            if (!sideGroups.TryGetValue(key, out var group))
                sideGroups[key] = group = (baseLabel, new List<(string, HediffDef)>());
            // One def per side: a duplicate "left" would show up twice in the side menu.
            if (group.defs.Any(v => v.side == side)) continue;
            group.defs.Add((side, def));
        }

        var collapsed = new List<HediffDef>(items);
        foreach (var group in sideGroups.Values)
        {
            if (group.defs.Count < 2) continue;
            var representative = group.defs[0].def;
            sideVariants[representative] = group.defs;
            labelOverrides[representative] = group.baseLabel.CapitalizeFirst();
            for (var i = 1; i < group.defs.Count; i++) collapsed.Remove(group.defs[i].def);
        }
        items = collapsed;

        // Label collisions ("Acid burn" x4, "Ability warmup" x3, "Acid fangs" x2...): tag each one with
        // its source mod so they're distinguishable; if the mod name doesn't separate them either
        // (several variants from the SAME mod), fall back to the defName, which is always unique.
        labelSuffixes = new Dictionary<HediffDef, string>();
        // Group by the DISPLAYED label: collapsed side-pairs now show their side-less name, so that's
        // the string that can actually collide on screen.
        foreach (var group in items.GroupBy(d => labelOverrides.TryGetValue(d, out var o) ? o : d.LabelCap.ToString()))
        {
            var defs = group.ToList();
            if (defs.Count < 2) continue;
            var modNamesDistinct = defs.Select(d => d.modContentPack?.Name ?? "").Distinct().Count() == defs.Count;
            foreach (var def in defs)
                labelSuffixes[def] = modNamesDistinct
                    ? $" ({def.modContentPack?.Name ?? def.defName})"
                    : $" ({def.defName})";
        }
        filters = GetFilters();
    }

    private static bool IsVanilla(HediffDef def) => def?.modContentPack?.IsCoreMod ?? false;

    // The pawn the picker is currently open on. The filter list is static (built once, shared), so the
    // per-pawn predicate needs to know who we are looking at; set in FiltersFor from the constructor.
    private static Pawn filterPawn;
    // Every hediff that some surgery recipe installs. A hediff NOT in here (a disease, an injury, a
    // condition, a module) has no race to be judged against, so it is always shown.
    private static HashSet<HediffDef> installableHediffs;
    private static bool warnedNoFilterPawn;

    private static List<Filter<HediffDef>> FiltersFor(Pawn pawn)
    {
        filterPawn = pawn;
        return filters;
    }

    /// <summary>
    /// True if this hediff can plausibly go on the pawn currently being edited: either nothing installs
    /// it surgically (so we can't tell, and we don't hide it), or some recipe valid for THIS race does.
    /// That is what separates the animal-only prosthetics from the humanlike ones without hardcoding
    /// name prefixes, which would break with mods and translations.
    /// </summary>
    private static bool FitsFilterPawn(HediffDef def)
    {
        if (def == null) return true;
        var pawn = filterPawn;
        if (pawn?.def?.AllRecipes == null)
        {
            // Don't fail silently into "shows everything": that is indistinguishable from a dead filter.
            if (!warnedNoFilterPawn)
            {
                warnedNoFilterPawn = true;
                Log.Warning("[Pawn Editor] The \"fits this pawn\" hediff filter has no pawn to check against, "
                            + "so it is showing everything. Please report this with the steps you followed.");
            }
            return true;
        }

        installableHediffs ??= DefDatabase<RecipeDef>.AllDefsListForReading
            .Where(r => r?.addsHediff != null).Select(r => r.addsHediff).ToHashSet();

        // Side-collapsed entries stand for their variants: judge the def we would actually install.
        var resolved = ResolveSideDef(def);
        if (!installableHediffs.Contains(resolved)) return true;

        return pawn.def.AllRecipes.Any(r => r?.addsHediff == resolved);
    }

    /// <summary>
    /// A string that captures what a hediff actually DOES: its label, description, class, the item it
    /// drops, its added-part efficiency and every stage's pain / stat / capacity changes. Two defs with
    /// the same signature are interchangeable for the player, so only one needs to be listed.
    /// </summary>
    private static string EffectSignature(HediffDef def)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(def.label).Append('|').Append(def.description).Append('|')
          .Append(def.hediffClass?.FullName).Append('|').Append(def.spawnThingOnRemoved?.defName).Append('|')
          .Append(def.addedPartProps?.partEfficiency.ToString("F3"));

        if (def.stages != null)
            foreach (var stage in def.stages)
            {
                sb.Append("#").Append(stage.minSeverity.ToString("F3"))
                  .Append(':').Append(stage.painOffset).Append(',').Append(stage.painFactor);
                if (stage.statOffsets != null)
                    foreach (var s in stage.statOffsets) sb.Append(s.stat?.defName).Append('=').Append(s.value).Append(';');
                if (stage.statFactors != null)
                    foreach (var s in stage.statFactors) sb.Append(s.stat?.defName).Append('*').Append(s.value).Append(';');
                if (stage.capMods != null)
                    foreach (var c in stage.capMods)
                        sb.Append(c.capacity?.defName).Append('+').Append(c.offset).Append('/').Append(c.setMax).Append(';');
            }

        return sb.ToString();
    }

    /// <summary>
    /// Detects a "(left)" / "(right)" marker in a def label. Returns the side (or null) and, via
    /// <paramref name="baseLabel" />, the label with the marker stripped so both sides group together.
    /// </summary>
    private static string SideOfLabel(string label, out string baseLabel)
    {
        baseLabel = label;
        if (label.NullOrEmpty()) return null;

        foreach (var side in new[] { "left", "right" })
        {
            var marker = "(" + side + ")";
            var index = label.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            baseLabel = (label.Substring(0, index) + label.Substring(index + marker.Length))
                .Replace("  ", " ").Trim();
            return side;
        }

        return null;
    }

    /// <summary>
    /// Maps a collapsed list entry to the def for the side the user picked in the footer. Defs without
    /// side variants (a heart, a spine) are returned untouched.
    /// </summary>
    private static HediffDef ResolveSideDef(HediffDef hediffDef)
    {
        if (hediffDef == null || !sideVariants.TryGetValue(hediffDef, out var variants)) return hediffDef;
        return variants.FirstOrDefault(v => v.def == currentSideVariant).def ?? variants[0].def;
    }

    private static void AddDefaultBodyParts(HediffDef hediff, List<BodyPartDef> appliedOnFixedBodyParts, List<BodyPartGroupDef> appliedOnFixedBodyPartGroups)
    {
        if (!defaultBodyParts.TryGetValue(hediff, out var item))
            defaultBodyParts.Add(hediff, (appliedOnFixedBodyParts ?? new(), appliedOnFixedBodyPartGroups ?? new()));
        else
        {
            List<BodyPartDef> item1 = new();
            List<BodyPartGroupDef> item2 = new();
            item1.AddRange(item.Item1);
            item2.AddRange(item.Item2);
            if (appliedOnFixedBodyParts != null)
                item1.AddRange(appliedOnFixedBodyParts);
            if (appliedOnFixedBodyPartGroups != null)
                item2.AddRange(appliedOnFixedBodyPartGroups);
            defaultBodyParts[hediff] = (item1, item2);
        }
    }

    public ListingMenu_Hediffs(Pawn pawn, UITable<Pawn> table) : base(items, labelGetter, b => TryAdd(b, pawn, table),
        "PawnEditor.Choose".Translate() + " " + "PawnEditor.Hediff".Translate().ToLower(),
        b => descGetter(b), null, FiltersFor(pawn), pawn)
    {

    }

    public override void PreOpen()
    {
        // The filter list is static and shared, so its per-pawn predicate reads a static pawn. Refresh it
        // on every open instead of trusting the value set at construction: if this window ever gets
        // reused or built ahead of time, a stale (or null) pawn silently turns the filter into a no-op,
        // which is exactly what "it stopped filtering" looks like.
        filterPawn = Pawn;
        base.PreOpen();
    }

    /// <summary>
    /// Finds the body part this hediff installs on according to a surgery recipe that is valid for THIS
    /// pawn's race (pawn.def.AllRecipes). Keeps animal-only recipes from hijacking a human lookup (and
    /// vice versa), which is what left modular implants without a part.
    /// </summary>
    private static BodyPartRecord ResolvePartFromPawnRecipes(Pawn pawn, HediffDef hediffDef, int depth = 0)
    {
        if (pawn?.def?.AllRecipes == null || pawn.RaceProps?.body == null || hediffDef == null || depth > 2)
            return null;

        foreach (var recipe in pawn.def.AllRecipes)
        {
            if (recipe?.addsHediff != hediffDef) continue;

            if (!recipe.appliedOnFixedBodyParts.NullOrEmpty())
                foreach (var bodyPartDef in recipe.appliedOnFixedBodyParts)
                {
                    var found = pawn.RaceProps.body.GetPartsWithDef(bodyPartDef)?.FirstOrDefault();
                    if (found != null) return found;
                }

            if (!recipe.appliedOnFixedBodyPartGroups.NullOrEmpty())
                foreach (var group in recipe.appliedOnFixedBodyPartGroups)
                {
                    var found = pawn.RaceProps.body.AllParts.FirstOrDefault(p => p.IsInGroup(group));
                    if (found != null) return found;
                }

            // "Upgrade / convert" surgeries declare NO fixed body part on purpose — they target whatever
            // part already carries the hediff they replace (e.g. LTS "modularize bionic arm":
            // removesHediff=BionicArm, addsHediff=LTS_ModularBionicArm). That's why every modular base
            // limb fell back to "whole body". Resolve the part from the hediff being replaced:
            if (recipe.removesHediff != null)
            {
                // 1) The pawn already has the base part -> install the modular version right there.
                var onPawn = pawn.health?.hediffSet?.hediffs
                    ?.FirstOrDefault(h => h.def == recipe.removesHediff && h.Part != null);
                if (onPawn != null) return onPawn.Part;

                // 2) It doesn't -> use wherever that base part itself would be installed.
                var viaBase = ResolvePartFromPawnRecipes(pawn, recipe.removesHediff, depth + 1);
                if (viaBase != null) return viaBase;
            }
        }
        return null;
    }

    /// <summary>
    /// Same resolution as <see cref="ResolvePartFromPawnRecipes" /> but returns EVERY candidate part
    /// instead of the first. Needed for the location button: a modular eye/arm has no fixed body part in
    /// its own recipe (it is a conversion surgery), so defaultBodyParts was empty and the picker offered
    /// no left/right choice at all, while the plain vanilla version did.
    /// </summary>
    private static List<BodyPartRecord> ResolveAllPartsFromPawnRecipes(Pawn pawn, HediffDef hediffDef, int depth = 0)
    {
        var results = new List<BodyPartRecord>();
        if (pawn?.def?.AllRecipes == null || pawn.RaceProps?.body == null || hediffDef == null || depth > 2) return results;

        foreach (var recipe in pawn.def.AllRecipes)
        {
            if (recipe?.addsHediff != hediffDef) continue;

            if (!recipe.appliedOnFixedBodyParts.NullOrEmpty())
                foreach (var bodyPartDef in recipe.appliedOnFixedBodyParts)
                    results.AddRange(pawn.RaceProps.body.GetPartsWithDef(bodyPartDef));

            if (!recipe.appliedOnFixedBodyPartGroups.NullOrEmpty())
                foreach (var group in recipe.appliedOnFixedBodyPartGroups)
                    results.AddRange(pawn.RaceProps.body.AllParts.Where(p => p.IsInGroup(group)));

            // Conversion surgery ("modularize bionic arm"): the target is wherever the replaced hediff
            // lives, or, if the pawn doesn't have it yet, wherever that base part itself would go. We
            // offer BOTH sides on purpose: in the editor the user is placing the piece, not operating.
            if (recipe.removesHediff != null)
            {
                if (pawn.health?.hediffSet?.hediffs != null)
                    results.AddRange(pawn.health.hediffSet.hediffs
                        .Where(h => h?.def == recipe.removesHediff && h.Part != null)
                        .Select(h => h.Part));
                results.AddRange(ResolveAllPartsFromPawnRecipes(pawn, recipe.removesHediff, depth + 1));
            }
        }

        return results.Where(p => p != null).Distinct().ToList();
    }

    private static AddResult TryAdd(HediffDef hediffDef, Pawn pawn, UITable<Pawn> uiTable)
    {
        // "(left)" / "(right)" pairs share a single list entry, so swap in the def for the chosen side.
        hediffDef = ResolveSideDef(hediffDef);

        BodyPartRecord part = currentBodyPart;

        // A MODULE (arm blade, dart gun, eye modules...) declares no body part and no surgery recipe:
        // it slots into a modular prosthetic the pawn must already have. Without this check it fell
        // through every resolver and landed on "whole body". Guide the user instead of guessing.
        var allowIllegal = PawnEditorMod.Settings?.AllowIllegalPlacements ?? false;

        var moduleInfo = ModularModulesCompat.GetModule(hediffDef);
        if (moduleInfo != null && !allowIllegal)
        {
            var hosts = ModularModulesCompat.HostsOnPawn(pawn, moduleInfo);
            if (hosts.Count == 0)
                return new FailureInfo("PawnEditor.ModuleNeedsHost".Translate(
                    hediffDef.LabelCap, ModularModulesCompat.RequirementLabel(moduleInfo)));

            // Respect the side/location the user picked if it is a valid host; otherwise take the only
            // one available (or the first, when both arms are modular and nothing was chosen yet).
            var host = hosts.FirstOrDefault(h => h.Part == currentBodyPart) ?? hosts[0];
            part = host.Part;

            var conflicting = ModularModulesCompat.ConflictOnPart(pawn, moduleInfo, part);
            if (conflicting != null)
                return new FailureInfo("PawnEditor.ModuleConflict".Translate(
                    hediffDef.LabelCap, conflicting.LabelCap, part.LabelCap));

            return BuildAddResult(hediffDef, pawn, part, uiTable);
        }

        // Bionic Modularity works the other way around from EBSG: no slots, the module just requires a
        // hediff marked as modular sitting on the SAME part. Same guidance, different framework.
        if (BionicModularityCompat.IsModule(hediffDef) && !allowIllegal)
        {
            var hostParts = BionicModularityCompat.PartsWithHost(pawn, hediffDef);
            if (hostParts.Count == 0)
            {
                // Two very different situations, and telling them apart is the whole point: "you have no
                // modular parts at all" vs "you have modular parts, just not the one THIS module needs".
                // The second one used to read like a bug to anyone looking at a pawn with a modular eye.
                var required = BionicModularityCompat.RequiredPartsLabel(hediffDef);
                if (!required.NullOrEmpty())
                    return new FailureInfo("PawnEditor.BmModuleNeedsPart".Translate(hediffDef.LabelCap, required));

                return new FailureInfo("PawnEditor.BmModuleNeedsHost".Translate(hediffDef.LabelCap));
            }

            if (currentBodyPart != null && hostParts.Contains(currentBodyPart))
                part = currentBodyPart;
            else if (part == null || !hostParts.Contains(part))
                part = hostParts[0];

            return BuildAddResult(hediffDef, pawn, part, uiTable);
        }

        // Prefer a surgery recipe that is actually valid for THIS pawn's race. The static map below
        // merges EVERY recipe that adds this hediff, so an animal-only variant (jaw/beak/snout) could
        // hijack the lookup for a human — that's why modular pieces like the arm dart gun module (whose
        // human recipe targets the Shoulder) ended up on "whole body". pawn.def.AllRecipes only lists
        // recipes usable on this pawn.
        part ??= ResolvePartFromPawnRecipes(pawn, hediffDef);

        if (part is null && defaultBodyParts.TryGetValue(hediffDef, out var defaultPart))
        {
            // Pick the first body part the pawn ACTUALLY has: the old code took the first projected
            // value even when it was null, so a single missing part def aborted the whole lookup.
            if (defaultPart.Item1?.Select(bp => pawn.RaceProps.body.GetPartsWithDef(bp)?.FirstOrDefault())
                    .FirstOrDefault(p => p != null) is { } part1)
                part = part1;
            if (part is null
                && defaultPart.Item2?.Select(group => pawn.RaceProps.body.AllParts.FirstOrDefault(p => p.IsInGroup(group)))
                    .FirstOrDefault(p => p != null) is { } part2)
                part = part2;
        }
        // FIX #004: Only injuries can safely default to corePart. MissingPart on corePart = death.
        if (part == null && typeof(Hediff_Injury).IsAssignableFrom(hediffDef.hediffClass))
            part = pawn.RaceProps.body.corePart;

        if (part == null && typeof(Hediff_MissingPart).IsAssignableFrom(hediffDef.hediffClass))
        {
            part = pawn.RaceProps.body.AllParts.FirstOrDefault(p => p != pawn.RaceProps.body.corePart);
            if (part == null)
                return new FailureInfo("PawnEditor.SelectBodyPart".Translate());
        }

        return BuildAddResult(hediffDef, pawn, part, uiTable);
    }

    /// <summary>
    /// Shared tail of TryAdd: builds the actual add action plus every confirmation/price wrapper. Split
    /// out so the module path (which resolves its own part) reuses the exact same safety checks.
    /// </summary>
    private static AddResult BuildAddResult(HediffDef hediffDef, Pawn pawn, BodyPartRecord part, UITable<Pawn> uiTable)
    {
        AddResult result = new SuccessInfo(() =>
        {
            // For a prosthetic/bionic (an "added part"), clear the target part FIRST, exactly like
            // vanilla surgery does. Otherwise AddHediff just stacks a second added part on the same
            // slot, producing the illegal bionic combinations users reported. RestorePart removes any
            // existing added/missing-part hediffs on the part (and its children), then we install the
            // new one cleanly — replacing instead of stacking.
            if (part != null && typeof(Hediff_AddedPart).IsAssignableFrom(hediffDef.hediffClass)
                && !(PawnEditorMod.Settings?.AllowIllegalPlacements ?? false))
                pawn.health.RestorePart(part);
            pawn.health.AddHediff(hediffDef, part);
            pawn.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty();
            TabWorker_Table<Pawn>.ClearCacheFor<TabWorker_Needs>();
            PawnEditor.Notify_PointsUsed();
            uiTable.ClearCache();
        });

        var price = hediffDef.priceOffset;
        if (price == 0 && hediffDef.priceImpact && hediffDef.spawnThingOnRemoved != null) price = hediffDef.spawnThingOnRemoved.BaseMarketValue;
        if (price is >= 1 or <= 1 && hediffDef.priceImpact)
            result = new ConditionalInfo(PawnEditor.CanUsePoints(price), result);

        if (typeof(Hediff_AddedPart).IsAssignableFrom(hediffDef.hediffClass)
            && pawn.health.hediffSet.GetFirstHediffMatchingPart<Hediff_AddedPart>(part) is { } hediff)
            result = new ConfirmInfo("PawnEditor.HediffConflict".Translate(hediffDef.LabelCap, hediff.LabelCap), "HediffConflict", result);

        if (typeof(Hediff_AddedPart).IsAssignableFrom(hediffDef.hediffClass) && part == null)
            result = new ConfirmInfo("PawnEditor.MissingPart".Translate(hediffDef.LabelCap), "MissingPart", result);


        if (!typeof(Hediff_Injury).IsAssignableFrom(hediffDef.hediffClass))
        {
            var existing = new List<Hediff>();
            pawn.health.hediffSet.GetHediffs(ref existing, h => h.def == hediffDef && h.Part == part);
            result = new ConfirmInfo("PawnEditor.HediffDuplicate".Translate(hediffDef.LabelCap), "HediffDuplicate", result, existing.Count > 0);
        }

        result = new ConfirmInfo("PawnEditor.WouldDie".Translate(hediffDef.LabelCap, pawn.NameShortColored), "HediffDeath", result,
            pawn.health.WouldDieAfterAddingHediff(hediffDef, part, hediffDef.initialSeverity));
        result = new ConfirmInfo("PawnEditor.WouldBeDowned".Translate(hediffDef.LabelCap, pawn.NameShortColored), "HediffDowned", result,
            pawn.health.WouldBeDownedAfterAddingHediff(hediffDef, part, hediffDef.initialSeverity));

        return result;
    }

    private List<BodyPartRecord> AllowedBodyParts()
    {
        var hediffDef = ResolveSideDef(Listing.Selected);
        var pawn = Pawn;

        // A module can only live where a modular host part is actually installed, so the location
        // button offers exactly those parts (usually "left arm" / "right arm") and nothing else.
        var moduleInfo = ModularModulesCompat.GetModule(hediffDef);
        if (moduleInfo != null)
            return ModularModulesCompat.HostsOnPawn(pawn, moduleInfo)
                .Select(h => h.Part).Distinct().ToList();

        // A Bionic Modularity module only fits on parts that already carry a modular prosthetic.
        if (BionicModularityCompat.IsModule(hediffDef))
            return BionicModularityCompat.PartsWithHost(pawn, hediffDef);

        var records = new List<BodyPartRecord>();
        if (defaultBodyParts.TryGetValue(hediffDef, out var defaultPart))
        {
            var allBodyParts = defaultPart.Item1;
            foreach (var bodyPart in allBodyParts)
            {
                var parts = pawn.RaceProps.body.GetPartsWithDef(bodyPart);
                records.AddRange(parts);
            }
        }

        // defaultBodyParts only knows recipes that declare a FIXED body part. Conversion surgeries
        // (every "(modular)" limb, eye, jaw...) declare none, so this list came out empty and the user
        // got no side choice. Fall back to the same race-aware recipe walk TryAdd uses.
        if (records.Count == 0)
            records = ResolveAllPartsFromPawnRecipes(pawn, hediffDef);

        return records.Where(p => p != null).Distinct().ToList();
    }

    private static void RecheckCurrentBodyPart(List<BodyPartRecord> records)
    {
        if (currentBodyPart != null && records.Contains(currentBodyPart) is false || currentBodyPart is null)
        {
            currentBodyPart = records.Any() ? records[0] : null;
        }
    }

    protected override void DrawFooter(ref Rect inRect)
    {
        if (Listing.Selected != null)
        {
            // Side selector, like vanilla surgery. Only shown for prosthetics that exist as separate
            // "(left)" / "(right)" defs; a heart or a spine never gets this row.
            if (sideVariants.TryGetValue(Listing.Selected, out var variants))
            {
                const float sidePadding = 4f;
                var sideRect = inRect.TakeBottomPart(30f + sidePadding * 2f).ContractedBy(0f, sidePadding);
                Widgets.Label(sideRect.LeftHalf(), "PawnEditor.SelectedSide".Translate());

                var currentSide = variants.FirstOrDefault(v => v.def == currentSideVariant).side ?? variants[0].side;
                if (Widgets.ButtonText(sideRect.TakeRightPart(UIUtility.BottomButtonSize.x), currentSide.CapitalizeFirst()))
                {
                    var sideOptions = new List<FloatMenuOption>();
                    foreach (var variant in variants)
                    {
                        var captured = variant;
                        sideOptions.Add(new FloatMenuOption(captured.side.CapitalizeFirst(), delegate
                        {
                            currentSideVariant = captured.def;
                            currentBodyPart = null; // force a re-pick against the new side's allowed parts
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(sideOptions));
                }
            }

            var allBodyParts = AllowedBodyParts();
            RecheckCurrentBodyPart(allBodyParts);
            if (allBodyParts.Count > 1)
            {
                const float padding = 4f;
                var rowRect = inRect.TakeBottomPart(30f + padding * 2f);
                rowRect = rowRect.ContractedBy(0f, padding);
                Widgets.Label(rowRect.LeftHalf(), "PawnEditor.SelectedLocation".Translate());
                if (Widgets.ButtonText(rowRect.TakeRightPart(UIUtility.BottomButtonSize.x),
                    currentBodyPart is null ? "None".Translate() : currentBodyPart.LabelCap))
                {
                    var options = new List<FloatMenuOption>();
                    foreach (var part in allBodyParts)
                    {
                        options.Add(new FloatMenuOption(part.LabelCap, delegate
                        {
                            currentBodyPart = part;
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }
        }
    }

    private static List<Filter<HediffDef>> GetFilters()
    {
        var list = new List<Filter<HediffDef>>
        {
            new Filter_Toggle<HediffDef>("PawnEditor.Prosthetic".Translate(), def => typeof(Hediff_AddedPart).IsAssignableFrom(def.hediffClass)),
            new Filter_Toggle<HediffDef>("PawnEditor.IsImplant".Translate(),
                def => typeof(Hediff_Implant).IsAssignableFrom(def.hediffClass) && !typeof(Hediff_AddedPart).IsAssignableFrom(def.hediffClass)),
            new Filter_Toggle<HediffDef>("PawnEditor.IsInjury".Translate(), def => typeof(Hediff_Injury).IsAssignableFrom(def.hediffClass)),
            new Filter_Toggle<HediffDef>("PawnEditor.IsDisease".Translate(), def => def.makesSickThought),
            // ON by default: a human picker was full of "Animal bionic thrumbo brain" / "Animal chicken
            // kidney". The user can delete the filter (trash icon) to see everything again.
            new Filter_Toggle<HediffDef>("PawnEditor.FitsThisPawn".Translate(), FitsFilterPawn,
                true, "PawnEditor.FitsThisPawnDesc".Translate())
        };

        var techLevel = possibleTechLevels.ToDictionary<TechLevel, string, Func<HediffDef, bool>>(
            level => level.ToStringHuman().CapitalizeFirst(),
            level => hediff => hediff.spawnThingOnRemoved?.techLevel == level);
        list.Add(new Filter_Dropdown<HediffDef>("PawnEditor.TechLevel".Translate(), techLevel));
        list.Add(new Filter_ModSource<HediffDef>());
        return list;
    }
}