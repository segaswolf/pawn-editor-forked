using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[StaticConstructorOnStartup]
// ToDo: Highlight current items?
public class ListingMenu_Items : ListingMenu<ThingDef>
{
    public enum ItemType
    {
        All = 0,
        Apparel,
        Equipment,
        Possessions,
        RangedWeapons,
        MeleeWeapons,
        Items,
        Starting
    }

    private static ItemType type;
    private static List<ThingDef> apparel;
    private static List<ThingDef> kidApparel;
    private static List<ThingDef> equipment;
    private static List<ThingDef> items;
    private static List<ThingDef> rangedWeapons;
    private static List<ThingDef> meleeWeapons;
    private static List<ThingDef> nonWeaponItems;
    private static List<ThingDef> starting;
    private static List<ThingDef> all;

    private static readonly Func<ThingDef, string> labelGetter = t => t.LabelCap;
    private static readonly Func<ThingDef, string> descGetter = t => t.DescriptionDetailed;
    private static readonly Action<ThingDef, Rect> iconDrawer = DrawThingIcon;

    public static readonly HashSet<ThingStyle> ThingStyles = new();
    private static IEnumerable<BodyPartGroupDef> occupiableGroupsDefs;

    private enum FallbackGroup
    {
        HeadFace,
        Hands,
        LegsFeet,
        UtilityApparel,
        BodyApparel,
        OtherApparel,
        OtherRangedWeapons,
        OtherMeleeWeapons,
        Food,
        Medicine,
        Drugs,
        ResourcesMaterials,
        UtilityItems,
        OtherItems
    }

    static ListingMenu_Items()
    {
        foreach (var styleCategoryDef in DefDatabase<StyleCategoryDef>.AllDefs)
        {
            if (styleCategoryDef?.thingDefStyles == null) continue;
            foreach (var thingDefStyle in styleCategoryDef.thingDefStyles)
            {
                // Skip entries with null ThingDef or StyleDef — can happen with broken/partial mod defs
                if (thingDefStyle == null || thingDefStyle.ThingDef == null || thingDefStyle.StyleDef == null) continue;

                var existing = ThingStyles.FirstOrDefault(ts => ts.ThingDef == thingDefStyle.ThingDef);
                if (existing.ThingDef != null && existing.StyleDefs != null)
                {
                    // ThingDef already in the set — add the style to its dictionary
                    existing.StyleDefs.TryAdd(thingDefStyle.StyleDef, styleCategoryDef);
                    continue;
                }

                ThingStyles.Add(new()
                {
                    ThingDef = thingDefStyle.ThingDef,
                    StyleDefs = new()
                    {
                        { thingDefStyle.StyleDef, styleCategoryDef }
                    }
                });
            }
        }

        MakeItemLists();
    }

    public ListingMenu_Items(ItemType itemType, Pawn pawn, TreeNode_ThingCategory treeNodeThingCategory = null) : base(t => TryAdd(t, pawn),
        GetMenuTitle(itemType), pawn)
    {
        TreeNodeThingCategory = treeNodeThingCategory ?? ThingCategoryNodeDatabase.RootNode;
        type = itemType;
        InitializeListing(itemType, pawn);

        occupiableGroupsDefs = pawn.def.race.body.cachedAllParts.SelectMany(p => p.groups)
            .Distinct()
            .Where(bp => apparel.Select(td => td.apparel.bodyPartGroups)
                .Any(bpg => bpg.Contains(bp)));
    }

    public ListingMenu_Items(ItemType itemType, Pawn pawn, Action<ThingDef> onSelected, TreeNode_ThingCategory treeNodeThingCategory = null) : base(t => new SuccessInfo(() => onSelected(t)),
        GetMenuTitle(itemType), pawn)
    {
        TreeNodeThingCategory = treeNodeThingCategory ?? ThingCategoryNodeDatabase.RootNode;
        type = itemType;
        InitializeListing(itemType, pawn);

        occupiableGroupsDefs = pawn.def.race.body.cachedAllParts.SelectMany(p => p.groups)
            .Distinct()
            .Where(bp => apparel.Select(td => td.apparel.bodyPartGroups)
                .Any(bpg => bpg.Contains(bp)));
    }

    public ListingMenu_Items(List<Thing> things, ItemType itemType, Action callback = null, string menuTitle = null) : base(t => TryAdd(t, things, callback),
        menuTitle)
    {
        TreeNodeThingCategory = ThingCategoryNodeDatabase.RootNode;
        type = itemType;
        InitializeListing(itemType);
    }

    public ListingMenu_Items(Func<Thing, AddResult> adder, ItemType itemType, string menuTitle = null) : base(t => TryAdd(t, adder), menuTitle)
    {
        TreeNodeThingCategory = ThingCategoryNodeDatabase.RootNode;
        type = itemType;
        InitializeListing(itemType);
    }

    private void InitializeListing(ItemType itemType, Pawn pawn = null)
    {
        var candidates = GetItemList(itemType, pawn);
        var listing = new Listing_TreeThing(candidates, labelGetter, iconDrawer, descGetter);
        listing.SetManualGroups(BuildFallbackGroups(candidates, TreeNodeThingCategory, itemType));
        Listing = listing;
    }

    public override void PreOpen()
    {
        base.PreOpen();
        // Filters are set on open because some filters depend on the pawn.
        Listing.Filters = GetFilters();
    }

    private static void DrawThingIcon(ThingDef thingDef, Rect rect)
    {
        var color = Color.white;
        var texture = Widgets.PlaceholderIconTex;
        if (thingDef != null)
        {
            color = GenStuff.AllowedStuffsFor(thingDef).FirstOrDefault()?.stuffProps.color ?? color;
            if (thingDef.colorGenerator != null)
                color = thingDef.colorGenerator.ExemplaryColor;
            texture = Widgets.GetIconFor(thingDef);
        }

        GUI.color = color;
        Widgets.DrawTextureFitted(rect, texture, .8f);
        GUI.color = Color.white;
    }

    private static string GetMenuTitle(ItemType itemType)
    {
        var typeLabel = "PawnEditor.ItemType." + itemType;
        return "PawnEditor.Choose".Translate() + " " + typeLabel.Translate().ToLower();
    }

    private static IEnumerable<Listing_TreeThing.ManualGroup> BuildFallbackGroups(
        IEnumerable<ThingDef> candidates,
        TreeNode_ThingCategory rootNode,
        ItemType itemType)
    {
        var reachableDefs = rootNode?.catDef?.DescendantThingDefs.ToHashSet() ?? new();
        return candidates
            .Where(thingDef => thingDef != null && !reachableDefs.Contains(thingDef))
            .Distinct()
            .GroupBy(thingDef => GetFallbackGroup(thingDef, itemType))
            .OrderBy(group => group.Key)
            .Select(group => new Listing_TreeThing.ManualGroup(GetFallbackLabel(group.Key), group));
    }

    private static FallbackGroup GetFallbackGroup(ThingDef thingDef, ItemType itemType)
    {
        if (thingDef.IsApparel)
            return GetApparelFallbackGroup(thingDef);

        if (itemType == ItemType.RangedWeapons || thingDef.IsRangedWeapon)
            return FallbackGroup.OtherRangedWeapons;

        if (itemType == ItemType.MeleeWeapons || thingDef.IsMeleeWeapon)
            return FallbackGroup.OtherMeleeWeapons;

        if (thingDef.IsMedicine)
            return FallbackGroup.Medicine;

        if (thingDef.IsDrug)
            return FallbackGroup.Drugs;

        if (thingDef.IsNutritionGivingIngestible)
            return FallbackGroup.Food;

        if (thingDef.IsStuff || thingDef.stuffProps != null)
            return FallbackGroup.ResourcesMaterials;

        if (thingDef.HasComp(typeof(CompUsable)))
            return FallbackGroup.UtilityItems;

        return FallbackGroup.OtherItems;
    }

    private static FallbackGroup GetApparelFallbackGroup(ThingDef thingDef)
    {
        var apparel = thingDef.apparel;
        var layers = apparel.layers;
        var bodyPartGroups = apparel.bodyPartGroups;

        if (layers.Contains(ApparelLayerDefOf.Belt))
            return FallbackGroup.UtilityApparel;

        if (layers.Contains(ApparelLayerDefOf.Overhead) || layers.Contains(ApparelLayerDefOf.EyeCover) ||
            bodyPartGroups.Contains(BodyPartGroupDefOf.FullHead) || bodyPartGroups.Contains(BodyPartGroupDefOf.UpperHead) ||
            bodyPartGroups.Contains(BodyPartGroupDefOf.Eyes))
            return FallbackGroup.HeadFace;

        if (ContainsBodyPartGroup(bodyPartGroups, "hand", "paw", "claw"))
            return FallbackGroup.Hands;

        if (bodyPartGroups.Contains(BodyPartGroupDefOf.Legs) || ContainsBodyPartGroup(bodyPartGroups, "foot", "hoof"))
            return FallbackGroup.LegsFeet;

        if (bodyPartGroups.Contains(BodyPartGroupDefOf.Torso))
            return FallbackGroup.BodyApparel;

        return FallbackGroup.OtherApparel;
    }

    private static bool ContainsBodyPartGroup(IEnumerable<BodyPartGroupDef> groups, params string[] terms)
    {
        return groups.Any(group => terms.Any(term => group.defName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
    }

    private static string GetFallbackLabel(FallbackGroup group) => ("PawnEditor.Fallback." + group).Translate();

    private static void CheckCapacity(Pawn pawn, Thing newItem)
    {
        if (MassUtility.FreeSpace(pawn) < newItem.GetStatValue(StatDefOf.Mass))
            Messages.Message("PawnEditor.WouldMakeOverCapacity".Translate(newItem.LabelCap, pawn.NameShortColored), MessageTypeDefOf.CautionInput, false);
    }

    private static AddResult TryAdd(ThingDef thingDef, Pawn pawn)
    {
        switch (type)
        {
            case ItemType.Apparel:
            {
                if (HARCompat.Active && HARCompat.EnforceRestrictions && !HARCompat.CanWear(thingDef, pawn))
                    return "PawnEditor.HARRestrictionViolated".Translate(pawn.Named("PAWN"), pawn.def.label.Named("RACE"), "PawnEditor.Wear".Named("VERB"),
                        thingDef.label.Named("ITEM"));

                if (thingDef.IsApparel)
                    {
                        Apparel newApparel = MakeApparel(thingDef);
                        AddResult result = new ConditionalInfo(PawnEditor.CanUsePoints(newApparel), new SuccessInfo(() =>
                        {
                            CheckCapacity(pawn, newApparel);
                            pawn.apparel.Wear(newApparel, false);
                            PawnEditor.Notify_PointsUsed();
                            TabWorker_Gear.ClearCaches();
                        }));


                        if (pawn.apparel.WornApparel.FirstOrDefault(ap => !ApparelUtility.CanWearTogether(thingDef, ap.def, pawn.RaceProps.body)) is
                            { } conflictApparel)
                            result = new ConfirmInfo("PawnEditor.WearingWouldRemove".Translate(thingDef.LabelCap, conflictApparel.LabelCap), "ApparelConflict",
                                result);

                        return result;
                    }

                    break;
            }
            case ItemType.Equipment:
            {
                if (HARCompat.Active && HARCompat.EnforceRestrictions && !HARCompat.CanEquip(thingDef, pawn))
                    return "PawnEditor.HARRestrictionViolated".Translate(pawn.Named("PAWN"), pawn.def.label.Named("RACE"), "PawnEditor.Equip".Named("VERB"),
                        thingDef.label.Named("ITEM"));

                if (thingDef.equipmentType != EquipmentType.None)
                    {
                        ThingWithComps newEquipment = MakeEquipment(thingDef);
                        return new ConditionalInfo(PawnEditor.CanUsePoints(newEquipment), new SuccessInfo(() =>
                        {
                            pawn.equipment.MakeRoomFor(newEquipment);
                            pawn.equipment.AddEquipment(newEquipment);
                            PawnEditor.Notify_PointsUsed();
                            TabWorker_Gear.ClearCaches();
                        }));
                    }
                    break;
            }
            case ItemType.All:
            case ItemType.Possessions:
                var newPossession = ThingMaker.MakeThing(thingDef, thingDef.defaultStuff);
                return new ConditionalInfo(PawnEditor.CanUsePoints(newPossession), new SuccessInfo(() =>
                {
                    // Try to stack with an existing item of the same def in the pawn's inventory
                    var existing = pawn.inventory.innerContainer
                        .FirstOrDefault(t => t.def == thingDef && t.stackCount < t.def.stackLimit);
                    if (existing != null)
                    {
                        existing.stackCount++;
                    }
                    else
                    {
                        pawn.inventory.innerContainer.TryAdd(newPossession, 1);
                    }
                    PawnEditor.Notify_PointsUsed();
                    TabWorker_Gear.ClearCaches();
                }));
            default:
                Log.WarningOnce("No ItemType!", 15703);
                break;
        }

        return false;
    }

    private static Apparel MakeApparel(ThingDef thingDef)
    {
        if (thingDef.MadeFromStuff)
        {
            if (PawnApparelGenerator.allApparelPairs.Where(pair => pair.thing == thingDef)
                .TryRandomElement(out var thingStuffPair))
            {
                var newApparel = (Apparel)ThingMaker.MakeThing(thingStuffPair.thing, thingStuffPair.stuff);
                return newApparel;
            }
            else
            {
                var newApparel = (Apparel)ThingMaker.MakeThing(thingDef, GenStuff.DefaultStuffFor(thingDef));
                return newApparel;
            }
        }
        else
        {
            var newApparel = (Apparel)ThingMaker.MakeThing(thingDef);
            return newApparel;
        }
    }

    private static ThingWithComps MakeEquipment(ThingDef thingDef)
    {
        if (thingDef.MadeFromStuff)
        {
            if (PawnWeaponGenerator.allWeaponPairs.Where(pair => pair.thing == thingDef)
                    .TryRandomElement(out var thingStuffPair))
            {
                var newEquipment = (ThingWithComps)ThingMaker.MakeThing(thingStuffPair.thing, thingStuffPair.stuff);
                return newEquipment;
            }
            else
            {
                var newEquipment = (ThingWithComps)ThingMaker.MakeThing(thingDef, GenStuff.DefaultStuffFor(thingDef));
                return newEquipment;
            }
        }
        else
        {
            var newEquipment = (ThingWithComps)ThingMaker.MakeThing(thingDef);
            return newEquipment;
        }
    }

    private static AddResult TryAdd(ThingDef thingDef, List<Thing> things, Action callback = null)
    {
        var thing = ThingMaker.MakeThing(thingDef);
        return new ConditionalInfo(PawnEditor.CanUsePoints(thing), new SuccessInfo(() =>
        {
            // Try to stack with an existing item of the same def in the list
            var existing = things.FirstOrDefault(t => t.def == thingDef && t.stackCount < t.def.stackLimit);
            if (existing != null)
            {
                existing.stackCount++;
            }
            else
            {
                things.Add(thing);
            }
            PawnEditor.Notify_PointsUsed();
            callback?.Invoke();
        }));
    }

    private static AddResult TryAdd(ThingDef thingDef, Func<Thing, AddResult> adder)
    {
        var thing = ThingMaker.MakeThing(thingDef);
        return new ConditionalInfo(PawnEditor.CanUsePoints(thing), adder(thing));
    }

    private static void MakeItemLists()
    {
        apparel = DefDatabase<ThingDef>.AllDefs
            .Where(td => td != null && td.IsApparel && td.apparel != null
                && td.apparel.developmentalStageFilter.Has(DevelopmentalStage.Adult)).ToList();
        kidApparel = DefDatabase<ThingDef>.AllDefs
            .Where(td => td != null && td.IsApparel && td.apparel != null
                && td.apparel.developmentalStageFilter.Has(DevelopmentalStage.Child)).ToList();
        // Some modded consumables/resources incorrectly carry equipmentType=Primary. A primary
        // slot flag alone does not make a def a usable weapon; require RimWorld's normal weapon
        // test so food, drugs, and materials remain available from the Items picker.
        equipment = DefDatabase<ThingDef>.AllDefs
            .Where(IsPrimaryWeapon).ToList();
        items = DefDatabase<ThingDef>.AllDefs
            .Where(td => td != null && td.category == ThingCategory.Item).ToList();
        rangedWeapons = equipment.Where(td => td.IsRangedWeapon).ToList();
        meleeWeapons = equipment.Where(td => td.IsMeleeWeapon).ToList();
        nonWeaponItems = items.Where(td => !td.IsApparel && !IsPrimaryWeapon(td)).ToList();
        starting = DefDatabase<ThingDef>.AllDefs.Where(td =>
                td != null &&
                ((td.category == ThingCategory.Item && td.scatterableOnMapGen && !td.destroyOnDrop)
                || (td.category == ThingCategory.Building && td.Minifiable)
                || (td.category == ThingCategory.Building && td.scatterableOnMapGen)))
            .ToList();
        all = DefDatabase<ThingDef>.AllDefs.Where(td => td != null).ToList();
    }

    private static bool IsPrimaryWeapon(ThingDef thingDef)
    {
        if (thingDef == null || thingDef.equipmentType != EquipmentType.Primary || !thingDef.IsWeapon)
            return false;

        // A few loaded mods flag consumables and resources as primary equipment. Treat the
        // established Weapons category as authoritative, while keeping truly uncategorized
        // mod weapons available through the existing fallback group.
        return ThingCategoryDefOf.Weapons.ContainedInThisOrDescendant(thingDef)
               || thingDef.thingCategories.NullOrEmpty();
    }

    private static List<ThingDef> GetItemList(ItemType itemType2, Pawn pawn = null)
    {
        switch (itemType2)
        {
            case ItemType.Apparel:
                if (pawn != null && pawn.DevelopmentalStage != DevelopmentalStage.Adult)
                    return kidApparel;
                return apparel;
            case ItemType.Equipment:
                return equipment;
            case ItemType.Possessions:
                return items;
            case ItemType.RangedWeapons:
                return rangedWeapons;
            case ItemType.MeleeWeapons:
                return meleeWeapons;
            case ItemType.Items:
                return nonWeaponItems;
            case ItemType.Starting:
                return starting;
            default:
                Log.WarningOnce("No ItemType!", 15703);
                return all;
        }
    }

    private List<Filter<ThingDef>> GetFilters()
    {
        var list = new List<Filter<ThingDef>>();

        list.Add(new Filter_ModSource<ThingDef>());
        list.Add(new Filter_Toggle<ThingDef>("PawnEditor.HasStyle".Translate(), def => ThingStyles.Select(ts => ts.ThingDef).Contains(def)));
        list.Add(new Filter_Toggle<ThingDef>("PawnEditor.HasStuff".Translate(), def => def.MadeFromStuff));

        if (type == ItemType.Apparel && Pawn != null)
        {
            list.Add(new Filter_Dropdown<ThingDef>("PawnEditor.WornOnBodyPart".Translate(),
                Filter_Dropdown<ThingDef>.GetDefFilter((ThingDef td, BodyPartGroupDef def) => 
                td.apparel.bodyPartGroups.Contains(def), occupiableGroupsDefs)));

            list.Add(new Filter_Dropdown<ThingDef>("PawnEditor.OccupiesLayer".Translate(), 
                Filter_Dropdown<ThingDef>.GetDefFilter((ThingDef td, ApparelLayerDef def) => td.apparel.layers.Contains(def))));
        }
        
        return list;
    }

    public struct ThingStyle
    {
        public ThingDef ThingDef; // The thing def that has styles
        public Dictionary<ThingStyleDef, StyleCategoryDef> StyleDefs; // The graphic is the key, the style group is the value
    }
}
