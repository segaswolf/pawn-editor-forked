using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[HotSwappable]
public class TabWorker_Gear : TabWorker<Pawn>
{
    private enum GearSlot
    {
        Apparel,
        Equipment,
        Possession
    }

    private static UITable<Pawn> apparelTable;
    private static UITable<Pawn> equipmentTable;
    private static UITable<Pawn> possessionsTable;
    private static GearEditSession editSession;
    private bool draggingPreview;
    private Vector3 previewCameraOffset = new(0f, 0f, 0.12f);
    private Pawn previewPawn;
    private Rot4 previewRotation = Rot4.South;
    private float previewZoom = 1.35f;
    private Vector2 scrollPos;

    public override void Initialize()
    {
        base.Initialize();
        apparelTable = new(GetHeadings(), p => GetRows(p, apparelTable, pawn => pawn.apparel.WornApparel.Cast<Thing>().ToList(), GearSlot.Apparel));
        equipmentTable = new(GetHeadings(), p => GetRows(p, equipmentTable, pawn => pawn.equipment.equipment.Cast<Thing>().ToList(), GearSlot.Equipment));
        possessionsTable = new(GetHeadings(), p => GetRows(p, possessionsTable, pawn => pawn.inventory.innerContainer.ToList(), GearSlot.Possession));
    }

    protected override void Notify_Open()
    {
        editSession?.Cancel();
        editSession = null;
    }

    private List<UITable<Pawn>.Heading> GetHeadings() =>
        new()
        {
            new(32),
            new("PawnEditor.Name".Translate(), textAnchor: TextAnchor.MiddleLeft),
            new("PawnEditor.Condition".Translate(), 70),
            new("PawnEditor.Edit".Translate(), 64),
            new(24)
        };

    private IEnumerable<UITable<Pawn>.Row> GetRows(Pawn pawn, UITable<Pawn> table, Func<Pawn, List<Thing>> thingsGetter, GearSlot slot)
    {
        var things = thingsGetter(pawn);
        for (var i = 0; i < things.Count; i++)
        {
            var thing = things[i];
            var captured = thing;
            var selected = editSession?.IsEditing(captured) == true;
            var items = new List<UITable<Pawn>.Row.Item>
            {
                new(iconRect =>
                {
                    Widgets.ThingIcon(iconRect.ContractedBy(4f), captured);
                    iconRect.xMin += 4f;
                    if (Mouse.IsOver(iconRect))
                    {
                        Widgets.DrawHighlight(iconRect);
                        TooltipHandler.TipRegion(iconRect, "PawnEditor.ClickToOpen".Translate());
                    }

                    if (Widgets.ButtonInvisible(iconRect)) Find.WindowStack.Add(new Dialog_InfoCard(captured));
                }),
                new(captured.LabelCap, captured.LabelCap.ToCharArray()[0], TextAnchor.MiddleLeft),
                new(((float)captured.HitPoints / captured.MaxHitPoints).ToStringPercent().Colorize(ColoredText.SubtleGrayColor),
                    captured.HitPoints / captured.MaxHitPoints),
                new(editRect =>
                {
                    var label = selected ? "PawnEditor.Editing".Translate() : "PawnEditor.Edit".Translate() + "...";
                    if (Widgets.ButtonText(editRect.ContractedBy(2f), label) && !selected)
                    {
                        StartExistingSession(pawn, captured, slot);
                    }
                }),
                new(TexButton.Delete, () =>
                {
                    if (editSession?.IsEditing(captured) == true)
                    {
                        editSession.Delete();
                        editSession = null;
                    }
                    else
                    {
                        DeleteTrackedThing(pawn, captured, slot);
                        PawnEditor.Notify_PointsUsed();
                        PawnEditor.RefreshPawnGraphics(pawn);
                        ClearCaches();
                    }
                })
            };

            yield return new(items, captured.GetTooltip().text);
        }
    }

    public static void ClearCaches()
    {
        apparelTable?.ClearCache();
        equipmentTable?.ClearCache();
        possessionsTable?.ClearCache();
    }

    private static void DeleteTrackedThing(Pawn pawn, Thing thing, GearSlot slot)
    {
        switch (slot)
        {
            case GearSlot.Apparel when thing is Apparel apparel && pawn.apparel.Contains(apparel):
                pawn.apparel.Remove(apparel);
                apparel.Destroy();
                break;
            case GearSlot.Equipment when thing is ThingWithComps equipment && pawn.equipment.Contains(equipment):
                pawn.equipment.DestroyEquipment(equipment);
                break;
            case GearSlot.Possession when pawn.inventory.innerContainer.Contains(thing):
                pawn.inventory.innerContainer.Remove(thing);
                thing.Destroy();
                break;
        }
    }

    private void DoTables(Rect inRect, Pawn pawn)
    {
        if (inRect.width <= 1f || inRect.height <= 1f)
            return;

        var apparelHeight = apparelTable.Height;
        var equipmentHeight = equipmentTable.Height;
        var possessionsHeight = possessionsTable.Height;
        var totalHeight = apparelHeight + equipmentHeight + possessionsHeight +
                          16f * 2f + 3f * 4f + 3 * Text.LineHeightOf(GameFont.Small);

        var viewRect = new Rect(inRect.x, inRect.y, Mathf.Max(1f, inRect.width - 20f), Mathf.Max(1f, totalHeight));

        Widgets.BeginScrollView(inRect, ref scrollPos, viewRect);
        DrawTableSection(ref viewRect, "Apparel".Translate().CapitalizeFirst(), apparelTable, pawn, apparelHeight);
        DrawTableSection(ref viewRect, "Equipment".Translate().CapitalizeFirst(), equipmentTable, pawn, equipmentHeight);
        DrawTableSection(ref viewRect, "Possessions".Translate().CapitalizeFirst(), possessionsTable, pawn, possessionsHeight);
        Widgets.EndScrollView();
    }

    private static void DrawTableSection(ref Rect viewRect, TaggedString title, UITable<Pawn> table, Pawn pawn, float height)
    {
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(viewRect.TakeTopPart(Text.LineHeightOf(GameFont.Small)), title.Colorize(ColoredText.TipSectionTitleColor));
        viewRect.xMin += 4f;
        viewRect.yMin += 4f;
        table.OnGUI(viewRect.TakeTopPart(height), pawn);
        viewRect.xMin -= 4f;
        viewRect.yMin += 16f;
    }

    public override void DrawTabContents(Rect inRect, Pawn pawn)
    {
        if (previewPawn != pawn)
        {
            draggingPreview = false;
            previewCameraOffset = new(0f, 0f, 0.12f);
            previewPawn = pawn;
            previewRotation = Rot4.South;
            previewZoom = 1.35f;
        }

        if (editSession?.Pawn != pawn)
        {
            editSession?.Cancel();
            editSession = null;
        }

        var bottomHeight = Mathf.Min(UIUtility.RegularButtonHeight, Mathf.Max(0f, inRect.height));
        DoBottomOptions(inRect.TakeBottomPart(bottomHeight), pawn);

        var contentRect = inRect.ContractedBy(4f);
        if (contentRect.width <= 1f || contentRect.height <= 1f)
            return;

        Rect previewRect;
        if (contentRect.width >= 520f)
        {
            var previewWidth = Mathf.Clamp(contentRect.width * 0.44f, 160f,
                Mathf.Max(160f, contentRect.width - 340f));
            previewRect = contentRect.TakeLeftPart(previewWidth).ContractedBy(4f);
            contentRect.xMin = Mathf.Min(contentRect.xMax, contentRect.xMin + 8f);
        }
        else
        {
            var previewHeight = Mathf.Min(contentRect.height * 0.4f, Mathf.Min(contentRect.width, 180f));
            previewRect = contentRect.TakeTopPart(Mathf.Max(0f, previewHeight)).ContractedBy(4f);
            contentRect.yMin = Mathf.Min(contentRect.yMax, contentRect.yMin + 8f);
        }

        DrawPreview(previewRect, pawn);

        if (editSession != null)
        {
            if (contentRect.width > 1f && contentRect.height > 1f)
                editSession.Draw(contentRect);
            return;
        }

        var headerHeight = contentRect.width >= 320f ? 120f : 230f;
        var headerRect = contentRect.TakeTopPart(Mathf.Min(headerHeight, contentRect.height));
        DrawEquipmentInfo(headerRect, pawn);
        contentRect.yMin = Mathf.Min(contentRect.yMax, contentRect.yMin + 8f);
        DoTables(contentRect, pawn);
    }

    private void DrawPreview(Rect inRect, Pawn pawn)
    {
        if (inRect.width <= 1f || inRect.height <= 1f)
            return;

        Widgets.DrawMenuSection(inRect);
        var margin = Mathf.Min(4f, Mathf.Min(inRect.width, inRect.height) * 0.25f);
        var portraitRect = inRect.ContractedBy(margin);
        PawnEditor.DrawInteractivePawnPreview(portraitRect, pawn, ref draggingPreview, ref previewRotation,
            ref previewCameraOffset, ref previewZoom);
    }

    private void DrawEquipmentInfo(Rect inRect, Pawn pawn)
    {
        if (inRect.width <= 1f || inRect.height <= 1f)
            return;

        var listing = new Listing_Standard();
        listing.Begin(inRect);
        var useTwoColumns = inRect.width >= 320f;
        listing.ColumnWidth = useTwoColumns ? Mathf.Max(1f, (inRect.width - 16f) / 2f) : inRect.width;

        listing.ListSeparator("TabBasics".Translate());
        listing.Label("MassCarried".Translate(MassUtility.GearAndInventoryMass(pawn).ToString("0.##"),
            MassUtility.Capacity(pawn).ToString("0.##")));
        listing.Label("ComfyTemperatureRange".Translate() + ": " +
                      pawn.GetStatValue(StatDefOf.ComfyTemperatureMin).ToStringTemperature("F0") + " ~ "
                    + pawn.GetStatValue(StatDefOf.ComfyTemperatureMax).ToStringTemperature("F0"));
        listing.Label("MarketValueTip".Translate() + ": $" + pawn.GetStatValue(StatDefOf.MarketValue));
        if (useTwoColumns)
            listing.NewColumn();
        else
            listing.Gap(8f);

        listing.ListSeparator("OverallArmor".Translate());
        DrawOverallArmor(listing, pawn, StatDefOf.ArmorRating_Sharp, "ArmorSharp".Translate());
        DrawOverallArmor(listing, pawn, StatDefOf.ArmorRating_Blunt, "ArmorBlunt".Translate());
        DrawOverallArmor(listing, pawn, StatDefOf.ArmorRating_Heat, "ArmorHeat".Translate());
        listing.End();
    }

    private static void DrawOverallArmor(Listing_Standard listing, Pawn pawn, StatDef stat, string label)
    {
        var num = 0f;
        var num2 = Mathf.Clamp01(pawn.GetStatValue(stat) / 2f);
        var allParts = pawn.RaceProps.body.AllParts;
        var wornApparel = pawn.apparel?.WornApparel;
        foreach (var part in allParts)
        {
            var num3 = 1f - num2;
            if (wornApparel != null)
                foreach (var apparel in wornApparel)
                    if (apparel.def.apparel.CoversBodyPart(part))
                    {
                        var num4 = Mathf.Clamp01(apparel.GetStatValue(stat) / 2f);
                        num3 *= 1f - num4;
                    }

            num += part.coverageAbs * (1f - num3);
        }

        num = Mathf.Clamp(num * 2f, 0f, 2f);
        listing.LabelDouble(label.Truncate(120), num.ToStringPercent());
    }

    private void DoBottomOptions(Rect inRect, Pawn pawn)
    {
        if (inRect.width <= 1f || inRect.height <= 1f)
            return;

        const float gap = 4f;
        var buttonWidth = Mathf.Max(0f, (inRect.width - gap * 4f) / 5f);
        if (buttonWidth <= 1f)
            return;

        var quickRect = new Rect(inRect.x, inRect.y, buttonWidth, inRect.height);
        var apparelRect = new Rect(quickRect.xMax + gap, inRect.y, buttonWidth, inRect.height);
        var rangedRect = new Rect(apparelRect.xMax + gap, inRect.y, buttonWidth, inRect.height);
        var meleeRect = new Rect(rangedRect.xMax + gap, inRect.y, buttonWidth, inRect.height);
        var itemRect = new Rect(meleeRect.xMax + gap, inRect.y, buttonWidth, inRect.height);

        if (Widgets.ButtonText(quickRect, "PawnEditor.QuickActions".Translate()))
            Find.WindowStack.Add(new FloatMenu(new()
            {
                new("PawnEditor.RepairAll".Translate(), () =>
                {
                    pawn.apparel.WornApparel.ForEach(a =>
                        {
                            a.HitPoints = a.MaxHitPoints;
                            a.wornByCorpseInt = false;
                        }
                    );
                    pawn.equipment.AllEquipmentListForReading.ForEach(e => e.HitPoints = e.MaxHitPoints);
                    foreach (var thing in pawn.inventory.innerContainer) thing.HitPoints = thing.MaxHitPoints;
                    PawnEditor.Notify_PointsUsed();
                    PawnEditor.RefreshPawnGraphics(pawn);
                    ClearCaches();
                }),
                new("PawnEditor.SetAllTo".Translate("Apparel".Translate().ToLower(), "PawnEditor.FavColor".Translate().ToLower()), () =>
                {
                    pawn.apparel.WornApparel.ForEach(a =>
                        {
                            if (a.TryGetComp<CompColorable>() != null)
                            {
                                if (pawn.story.favoriteColor != null)
                                {
                                    a.SetColor(pawn.story.favoriteColor.color);
                                }
                                else
                                {
                                    Messages.Message("No favourite color found for pawn", MessageTypeDefOf.RejectInput);
                                }
                            }
                        }
                    );
                    PawnEditor.RefreshPawnGraphics(pawn);
                    ClearCaches();
                })
            }));

        if (Widgets.ButtonText(apparelRect, "PawnEditor.AddApparel".Translate()))
        {
            Find.WindowStack.Add(new ListingMenu_Items(ListingMenu_Items.ItemType.Apparel, pawn,
                thingDef => StartNewSession(pawn, thingDef, GearSlot.Apparel),
                ThingCategoryNodeDatabase.allThingCategoryNodes.FirstOrDefault(tc => tc.catDef == ThingCategoryDefOf.Apparel)));
        }

        if (Widgets.ButtonText(rangedRect, "PawnEditor.AddRanged".Translate()))
        {
            Find.WindowStack.Add(new ListingMenu_Items(ListingMenu_Items.ItemType.RangedWeapons, pawn,
                thingDef => StartNewSession(pawn, thingDef, GearSlot.Equipment),
                ThingCategoryNodeDatabase.RootNode));
        }

        if (Widgets.ButtonText(meleeRect, "PawnEditor.AddMelee".Translate()))
        {
            Find.WindowStack.Add(new ListingMenu_Items(ListingMenu_Items.ItemType.MeleeWeapons, pawn,
                thingDef => StartNewSession(pawn, thingDef, GearSlot.Equipment),
                ThingCategoryNodeDatabase.RootNode));
        }

        if (Widgets.ButtonText(itemRect, "PawnEditor.AddItem".Translate()))
        {
            Find.WindowStack.Add(new ListingMenu_Items(ListingMenu_Items.ItemType.Items, pawn,
                thingDef => StartNewSession(pawn, thingDef, GearSlot.Possession),
                ThingCategoryNodeDatabase.RootNode));
        }
    }

    private static void StartNewSession(Pawn pawn, ThingDef thingDef, GearSlot slot)
    {
        editSession?.Cancel();
        editSession = GearEditSession.ForNew(pawn, thingDef, slot);
        editSession.ApplyPreview();
        ClearCaches();
    }

    private static void StartExistingSession(Pawn pawn, Thing thing, GearSlot slot)
    {
        editSession?.Cancel();
        editSession = GearEditSession.ForExisting(pawn, thing, slot);
        editSession.ApplyPreview();
        ClearCaches();
    }

    private static void CancelSessionFor(Pawn pawn)
    {
        if (editSession?.Pawn != pawn)
            return;

        editSession.Cancel();
        editSession = null;
    }

    public override IEnumerable<SaveLoadItem> GetSaveLoadItems(Pawn pawn)
    {
        yield return new SaveLoadItem<Pawn_ApparelTracker>("Apparel".Translate(), pawn.apparel, new()
        {
            ParentPawn = pawn,
            PrepareSave = _ => CancelSessionFor(pawn),
            PrepareLoad = _ => CancelSessionFor(pawn),
            OnLoad = _ => { PawnEditor.RefreshPawnGraphics(pawn); ClearCaches(); }
        });
        yield return new SaveLoadItem<Pawn_EquipmentTracker>("Equipment".Translate(), pawn.equipment, new()
        {
            ParentPawn = pawn,
            PrepareSave = _ => CancelSessionFor(pawn),
            PrepareLoad = _ => CancelSessionFor(pawn),
            OnLoad = _ => { PawnEditor.RefreshPawnGraphics(pawn); ClearCaches(); }
        });
        yield return new SaveLoadItem<Pawn_InventoryTracker>("Possessions".Translate(), pawn.inventory, new()
        {
            ParentPawn = pawn,
            PrepareSave = _ => CancelSessionFor(pawn),
            PrepareLoad = _ => CancelSessionFor(pawn),
            OnLoad = _ => ClearCaches()
        });
    }

    public override IEnumerable<FloatMenuOption> GetRandomizationOptions(Pawn pawn)
    {
        yield return new("Apparel".Translate(), () =>
        {
            editSession?.Cancel();
            editSession = null;
            pawn.apparel.DestroyAll();
            PawnApparelGenerator.workingSet.Reset(pawn);
            PawnApparelGenerator.usableApparel.Clear();
            PawnApparelGenerator.usableApparel.AddRange(PawnApparelGenerator.allApparelPairs.Where(apparel =>
                apparel.thing.apparel.PawnCanWear(pawn) &&
                !PawnApparelGenerator.workingSet.PairOverlapsAnything(apparel)));

            ThingStuffPair workingPair;

            while (Rand.Value >= PawnApparelGenerator.workingSet.Count / 10f
                && PawnApparelGenerator.usableApparel.TryRandomElementByWeight(pa => pa.Commonality,
                       out workingPair))
            {
                PawnApparelGenerator.workingSet.Add(workingPair);
                for (var k = PawnApparelGenerator.usableApparel.Count - 1; k >= 0; k--)
                    if (PawnApparelGenerator.workingSet.PairOverlapsAnything(PawnApparelGenerator.usableApparel[k]))
                        PawnApparelGenerator.usableApparel.RemoveAt(k);
            }

            PawnApparelGenerator.workingSet.GiveToPawn(pawn);
            PawnApparelGenerator.workingSet.Reset(null, null);
            PawnEditor.RefreshPawnGraphics(pawn);
            ClearCaches();
        });
        yield return new("Apparel".Translate() + " " + "PawnEditor.FromKind".Translate(), () =>
        {
            editSession?.Cancel();
            editSession = null;
            PawnApparelGenerator.GenerateStartingApparelFor(
                pawn, new(pawn.kindDef, pawn.Faction));

            PawnEditor.RefreshPawnGraphics(pawn);
            ClearCaches();
        });
        yield return new("Equipment".Translate(), () =>
        {
            editSession?.Cancel();
            editSession = null;
            pawn.equipment.DestroyAllEquipment();
            var thingStuffPair = PawnWeaponGenerator.allWeaponPairs.RandomElement();
            var thingWithComps = (ThingWithComps)ThingMaker.MakeThing(thingStuffPair.thing, thingStuffPair.stuff);
            PawnGenerator.PostProcessGeneratedGear(thingWithComps, pawn);
            if (thingWithComps.TryGetComp<CompEquippable>() is { } compEquippable)
            {
                if (pawn.kindDef.weaponStyleDef != null)
                    compEquippable.parent.StyleDef = pawn.kindDef.weaponStyleDef;
                else if (pawn.Ideo != null) compEquippable.parent.StyleDef = pawn.Ideo.GetStyleFor(thingWithComps.def);
            }

            pawn.equipment.AddEquipment(thingWithComps);
            PawnEditor.RefreshPawnGraphics(pawn);
            ClearCaches();
        });
        yield return new("Equipment".Translate() + " " + "PawnEditor.FromKind".Translate(),
            () =>
            {
                editSession?.Cancel();
                editSession = null;
                pawn.equipment.DestroyAllEquipment();
                PawnWeaponGenerator.TryGenerateWeaponFor(pawn, new(pawn.kindDef, pawn.Faction));
                PawnEditor.RefreshPawnGraphics(pawn);
                ClearCaches();
            });
        yield return new("Possessions".Translate(), () =>
        {
            editSession?.Cancel();
            editSession = null;
            pawn.inventory.DestroyAll();
            PawnInventoryGenerator.GenerateInventoryFor(pawn, new(pawn.kindDef, pawn.Faction));
            ClearCaches();
        });
    }

    private sealed class GearEditSession
    {
        private readonly struct DetachedApparel
        {
            public DetachedApparel(Apparel apparel, bool locked)
            {
                Apparel = apparel;
                Locked = locked;
            }

            public Apparel Apparel { get; }
            public bool Locked { get; }
        }

        private readonly GearSlot slot;
        private readonly Thing original;
        private readonly List<DetachedApparel> detachedApparel = new();
        private readonly List<ThingWithComps> detachedEquipment = new();
        private readonly List<Thing> detachedInventory = new();
        private Thing candidate;
        private ThingDef stuff;
        private ThingStyleDef style;
        private QualityCategory quality = QualityCategory.Normal;
        private Color color = Color.white;
        private int stackCount = 1;
        private bool tainted;
        private bool dirty = true;
        private bool accepted;
        private bool detachedItems;
        private string stackBuffer;
        private Vector2 optionsScrollPosition;
        private float optionsViewHeight = 260f;

        private GearEditSession(Pawn pawn, ThingDef thingDef, GearSlot slot, Thing original)
        {
            Pawn = pawn;
            ThingDef = thingDef;
            this.slot = slot;
            this.original = original;

            stuff = original?.Stuff ?? DefaultStuffFor(thingDef);
            style = original?.StyleDef;
            if (original?.TryGetComp<CompQuality>() is { } compQuality)
            {
                quality = compQuality.Quality;
            }
            else if (thingDef.HasComp(typeof(CompQuality)))
            {
                quality = QualityCategory.Normal;
            }

            color = original?.DrawColor ?? DefaultColorFor(thingDef, stuff);
            stackCount = Math.Max(1, original?.stackCount ?? 1);
            stackBuffer = stackCount.ToString();
            if (original is Apparel apparel)
            {
                tainted = apparel.WornByCorpse;
            }
        }

        public Pawn Pawn { get; }
        private ThingDef ThingDef { get; }

        public static GearEditSession ForNew(Pawn pawn, ThingDef thingDef, GearSlot slot) => new(pawn, thingDef, slot, null);
        public static GearEditSession ForExisting(Pawn pawn, Thing thing, GearSlot slot) => new(pawn, thing.def, slot, thing);

        public bool IsEditing(Thing thing)
        {
            return ReferenceEquals(original, thing) || ReferenceEquals(candidate, thing);
        }

        public void Draw(Rect inRect)
        {
            if (inRect.width <= 1f || inRect.height <= 1f)
                return;

            Widgets.DrawMenuSection(inRect);
            var margin = Mathf.Min(8f, Mathf.Min(inRect.width, inRect.height) * 0.25f);
            var rect = inRect.ContractedBy(margin);
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            var headerRect = rect.TakeTopPart(Mathf.Min(42f, rect.height));
            DrawHeader(headerRect);
            rect.yMin = Mathf.Min(rect.yMax, rect.yMin + Mathf.Min(4f, rect.height));

            var buttonsRect = rect.TakeBottomPart(Mathf.Min(UIUtility.RegularButtonHeight, rect.height));
            Rect optionsRect;
            Rect previewRect;
            if (rect.width >= 260f)
            {
                var previewWidth = Mathf.Min(170f, Mathf.Max(80f, rect.width * 0.34f));
                previewWidth = Mathf.Min(previewWidth, Mathf.Max(0f, rect.width - 140f));
                previewRect = new Rect(rect.xMax - previewWidth, rect.y, previewWidth,
                    Mathf.Min(previewWidth, rect.height));
                optionsRect = new Rect(rect.x, rect.y, Mathf.Max(0f, previewRect.xMin - rect.x - 8f), rect.height);
            }
            else
            {
                var previewSize = Mathf.Min(100f, Mathf.Min(rect.width, rect.height * 0.35f));
                previewRect = new Rect(rect.x + (rect.width - previewSize) / 2f, rect.y, previewSize, previewSize);
                var optionsY = Mathf.Min(rect.yMax, previewRect.yMax + 4f);
                optionsRect = new Rect(rect.x, optionsY, rect.width, Mathf.Max(0f, rect.yMax - optionsY));
            }

            if (optionsRect.width > 1f && optionsRect.height > 1f)
                DrawOptions(optionsRect);
            if (previewRect.width > 1f && previewRect.height > 1f)
                DrawItemPreview(previewRect);

            if (buttonsRect.width <= 1f || buttonsRect.height <= 1f)
                return;

            var buttonMargin = Mathf.Min(2f, Mathf.Min(buttonsRect.height, buttonsRect.width / 2f) * 0.25f);
            var cancelRect = buttonsRect.LeftHalf().ContractedBy(buttonMargin);
            var applyRect = buttonsRect.RightHalf().ContractedBy(buttonMargin);
            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()))
            {
                Cancel();
                editSession = null;
            }

            if (Widgets.ButtonText(applyRect, "PawnEditor.Apply".Translate()))
            {
                Accept();
                editSession = null;
            }
        }

        private void DrawHeader(Rect rect)
        {
            using (new TextBlock(GameFont.Medium, TextAnchor.MiddleLeft))
                Widgets.Label(rect, ThingDef.LabelCap);
        }

        private void DrawItemPreview(Rect rect)
        {
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            Widgets.DrawMenuSection(rect);
            var margin = Mathf.Min(12f, Mathf.Min(rect.width, rect.height) * 0.25f);
            var iconRect = rect.ContractedBy(margin);
            var thing = candidate ?? original;
            if (thing != null)
            {
                Widgets.ThingIcon(iconRect, thing);
            }
            else
            {
                GUI.color = ThingDef.uiIconColor;
                Widgets.DrawTextureFitted(iconRect, Widgets.GetIconFor(ThingDef), 0.85f);
                GUI.color = Color.white;
            }
        }

        private void DrawOptions(Rect inRect)
        {
            if (inRect.width <= 1f || inRect.height <= 1f)
                return;

            var viewRect = new Rect(0f, 0f, Mathf.Max(1f, inRect.width - 16f), Mathf.Max(inRect.height, optionsViewHeight));
            Widgets.BeginScrollView(inRect, ref optionsScrollPosition, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.Label("StatsReport_Material".Translate() + ": " + (stuff?.LabelCap ?? "None".Translate()).Colorize(ColoredText.SubtleGrayColor));
            if (ThingDef.MadeFromStuff && Widgets.ButtonText(listing.GetRect(UIUtility.RegularButtonHeight), "PawnEditor.Change".Translate()))
            {
                Find.WindowStack.Add(new FloatMenu(GenStuff.AllowedStuffsFor(ThingDef)
                    .Select(def => new FloatMenuOption(def.LabelCap, () =>
                    {
                        stuff = def;
                        color = DefaultColorFor(ThingDef, stuff);
                        MarkDirty();
                    }, Widgets.GetIconFor(def), def.uiIconColor))
                    .ToList()));
            }

            if (ThingDef.HasComp(typeof(CompQuality)))
            {
                listing.Gap(4f);
                listing.Label("Quality".Translate() + ": " + quality.GetLabel().CapitalizeFirst().Colorize(ColoredText.SubtleGrayColor));
                if (Widgets.ButtonText(listing.GetRect(UIUtility.RegularButtonHeight), "PawnEditor.Change".Translate()))
                {
                    Find.WindowStack.Add(new FloatMenu(QualityUtility.AllQualityCategories
                        .Select(q => new FloatMenuOption(q.GetLabel().CapitalizeFirst(), () =>
                        {
                            quality = q;
                            MarkDirty();
                        }))
                        .ToList()));
                }
            }

            var styleOptions = StyleOptionsFor(ThingDef);
            if (styleOptions.Count > 0)
            {
                listing.Gap(4f);
                listing.Label("Stat_Thing_StyleLabel".Translate() + ": " + (style?.LabelCap ?? "None".Translate()).Colorize(ColoredText.SubtleGrayColor));
                if (Widgets.ButtonText(listing.GetRect(UIUtility.RegularButtonHeight), "PawnEditor.Change".Translate()))
                {
                    var options = styleOptions
                        .Select(pair => new FloatMenuOption(pair.Value.LabelCap, () =>
                        {
                            style = pair.Key;
                            MarkDirty();
                        }, pair.Value.Icon, Color.white))
                        .Append(new FloatMenuOption("None".Translate(), () =>
                        {
                            style = null;
                            MarkDirty();
                        }))
                        .ToList();
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }

            if (SupportsColor)
            {
                listing.Gap(4f);
                var colorRect = listing.GetRect(UIUtility.RegularButtonHeight);
                var swatch = colorRect.TakeRightPart(UIUtility.RegularButtonHeight).ContractedBy(4f);
                if (Widgets.ButtonText(colorRect, "PawnEditor.PickColor".Translate()))
                {
                    var specialColors = new Dictionary<string, Color>
                    {
                        { "Default", DefaultColorFor(ThingDef, stuff) }
                    };
                    if (Pawn.story?.favoriteColor != null)
                    {
                        specialColors.Add("Favorite", Pawn.story.favoriteColor.color);
                    }
                    Find.WindowStack.Add(new Dialog_ColorPicker(c =>
                    {
                        color = c;
                        MarkDirty();
                    }, DefDatabase<ColorDef>.AllDefs.Select(def => def.color).ToList(), color, specialColors));
                }
                Widgets.DrawBoxSolid(swatch, color);
                Widgets.DrawBox(swatch);
            }

            if (ThingDef.stackLimit > 1)
            {
                listing.Gap(4f);
                listing.Label("PenFoodTab_Count".Translate());
                var countRect = listing.GetRect(UIUtility.RegularButtonHeight);
                var before = stackCount;
                UIUtility.IntField(countRect, ref stackCount, 1, int.MaxValue, ref stackBuffer);
                if (before != stackCount)
                {
                    MarkDirty();
                }
            }

            if (candidate is Apparel apparel)
            {
                listing.Gap(4f);
                var before = tainted;
                listing.CheckboxLabeled("PawnEditor.Tainted".Translate(), ref tainted);
                if (before != tainted)
                {
                    MarkDirty();
                }
            }

            if (candidate?.TryGetComp<CompBladelinkWeapon>() is { } bladelink)
            {
                listing.Gap(4f);
                DrawPersonaTraits(listing.GetRect(60f), bladelink);
            }

            listing.Gap(4f);
            listing.Label("PawnEditor.Condition".Translate() + ": " +
                          ((float)(candidate ?? original)?.HitPoints / Mathf.Max(1, (candidate ?? original)?.MaxHitPoints ?? 1)).ToStringPercent()
                              .Colorize(ColoredText.SubtleGrayColor));

            listing.End();
            if (Event.current.type == EventType.Layout)
                optionsViewHeight = Mathf.Max(inRect.height, listing.CurHeight + 4f);
            Widgets.EndScrollView();
        }

        private void DrawPersonaTraits(Rect rect, CompBladelinkWeapon bladelink)
        {
            if (rect.width <= 1f || rect.height <= 1f)
                return;

            Widgets.Label(rect.TakeTopPart(Text.LineHeight), "Traits".Translate());
            var addRect = rect.TakeRightPart(Mathf.Min(100f, rect.width)).TopPartPixels(Mathf.Min(30f, rect.height));
            if (Widgets.ButtonText(addRect, "Add".Translate().CapitalizeFirst()))
            {
                Find.WindowStack.Add(new FloatMenu(DefDatabase<WeaponTraitDef>.AllDefs
                    .Select(def => new FloatMenuOption(def.LabelCap, () =>
                    {
                        if (bladelink.CanAddTrait(def))
                        {
                            bladelink.traits.Add(def);
                            MarkDirty(rebuildCandidate: false);
                        }
                        else
                        {
                            Messages.Message("PawnEditor.TraitDisallowedByKind".Translate(def.label, ThingDef.LabelCap), MessageTypeDefOf.RejectInput);
                        }
                    }))
                    .ToList()));
            }

            GenUI.DrawElementStack(rect, 22f, bladelink.traits, delegate(Rect r, WeaponTraitDef weaponTraitDef)
            {
                GUI.color = CharacterCardUtility.StackElementBackground;
                GUI.DrawTexture(r, BaseContent.WhiteTex);
                GUI.color = Color.white;
                if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);

                Widgets.Label(new(r.x + 5f, r.y, r.width - 10f, r.height), weaponTraitDef.LabelCap);
                if (Mouse.IsOver(r))
                {
                    TooltipHandler.TipRegion(r, weaponTraitDef.description);
                    if (Widgets.ButtonImage(r.RightPartPixels(r.height).ContractedBy(4), TexButton.Delete))
                    {
                        bladelink.traits.Remove(weaponTraitDef);
                        MarkDirty(rebuildCandidate: false);
                    }
                }
            }, weaponTraitDef => Text.CalcSize(weaponTraitDef.LabelCap).x + 10f, 5f);
        }

        private bool SupportsColor => ThingDef.IsApparel && ThingDef.HasComp(typeof(CompColorable));

        private void MarkDirty(bool rebuildCandidate = true)
        {
            dirty = rebuildCandidate;
            if (dirty)
            {
                ApplyPreview();
            }
            else
            {
                PawnEditor.RefreshPawnGraphics(Pawn);
                ClearCaches();
            }
        }

        public void ApplyPreview()
        {
            if (!dirty && candidate != null)
            {
                return;
            }

            RemoveCandidate();
            DetachItemsForPreview();
            candidate = MakeCandidate();
            AddCandidateToPawn();
            dirty = false;
            PawnEditor.RefreshPawnGraphics(Pawn);
            ClearCaches();
        }

        public void Accept()
        {
            accepted = true;
            DestroyDetachedItems();
            PawnEditor.Notify_PointsUsed();
            PawnEditor.RefreshPawnGraphics(Pawn);
            ClearCaches();
        }

        public void Cancel()
        {
            if (!accepted)
            {
                RemoveCandidate();
                RestoreDetachedItems();
                PawnEditor.RefreshPawnGraphics(Pawn);
                ClearCaches();
            }
        }

        public void Delete()
        {
            RemoveCandidate();
            DestroyDetachedItems();
            PawnEditor.Notify_PointsUsed();
            PawnEditor.RefreshPawnGraphics(Pawn);
            ClearCaches();
        }

        private Thing MakeCandidate()
        {
            var made = ThingMaker.MakeThing(ThingDef, ThingDef.MadeFromStuff ? stuff : null);
            made.HitPoints = Mathf.Clamp(original?.HitPoints ?? made.MaxHitPoints, 1, made.MaxHitPoints);
            made.stackCount = Math.Max(1, stackCount);
            made.SetStyleDef(style);
            if (made.TryGetComp<CompQuality>() is { } compQuality)
            {
                compQuality.SetQuality(quality, ArtGenerationContext.Outsider);
            }

            if (made.TryGetComp<CompColorable>() is { } colorable)
            {
                colorable.SetColor(color);
            }

            made.Notify_ColorChanged();

            if (made is Apparel apparel)
            {
                apparel.wornByCorpseInt = tainted;
            }

            if (original?.TryGetComp<CompBladelinkWeapon>() is { } srcBlade && made.TryGetComp<CompBladelinkWeapon>() is { } dstBlade)
            {
                dstBlade.traits.Clear();
                dstBlade.traits.AddRange(srcBlade.traits);
            }

            return made;
        }

        private void AddCandidateToPawn()
        {
            switch (slot)
            {
                case GearSlot.Apparel:
                    Pawn.apparel.Wear((Apparel)candidate, false);
                    break;
                case GearSlot.Equipment:
                    Pawn.equipment.AddEquipment((ThingWithComps)candidate);
                    break;
                case GearSlot.Possession:
                    Pawn.inventory.innerContainer.TryAdd(candidate, false);
                    break;
            }
        }

        private void DetachItemsForPreview()
        {
            if (detachedItems)
            {
                return;
            }

            switch (slot)
            {
                case GearSlot.Apparel when original is Apparel apparel && Pawn.apparel.Contains(apparel):
                    detachedApparel.Add(new(apparel, Pawn.apparel.IsLocked(apparel)));
                    Pawn.apparel.Remove(apparel);
                    break;
                case GearSlot.Apparel when original == null:
                    foreach (var worn in Pawn.apparel.WornApparel
                                 .Where(apparel => !ApparelUtility.CanWearTogether(ThingDef, apparel.def, Pawn.RaceProps.body))
                                 .ToList())
                    {
                        detachedApparel.Add(new(worn, Pawn.apparel.IsLocked(worn)));
                        Pawn.apparel.Remove(worn);
                    }
                    break;
                case GearSlot.Equipment when original is ThingWithComps equipment && Pawn.equipment.Contains(equipment):
                    detachedEquipment.Add(equipment);
                    Pawn.equipment.Remove(equipment);
                    break;
                case GearSlot.Equipment when original == null:
                    foreach (var equipped in Pawn.equipment.AllEquipmentListForReading
                                 .Where(thing => thing.def.equipmentType == EquipmentType.Primary).ToList())
                    {
                        detachedEquipment.Add(equipped);
                        Pawn.equipment.Remove(equipped);
                    }
                    break;
                case GearSlot.Possession when original != null && Pawn.inventory.innerContainer.Contains(original):
                    detachedInventory.Add(original);
                    Pawn.inventory.innerContainer.Remove(original);
                    break;
            }

            detachedItems = true;
        }

        private void RemoveCandidate()
        {
            if (candidate == null)
            {
                return;
            }

            DeleteTrackedThing(Pawn, candidate, slot);
            if (!candidate.Destroyed)
                candidate.Destroy();
            candidate = null;
        }

        private void RestoreDetachedItems()
        {
            foreach (var apparel in detachedApparel)
            {
                Pawn.apparel.Wear(apparel.Apparel, false, apparel.Locked);
            }

            foreach (var equipment in detachedEquipment)
            {
                Pawn.equipment.AddEquipment(equipment);
            }

            foreach (var item in detachedInventory)
            {
                Pawn.inventory.innerContainer.TryAdd(item, false);
            }

            detachedApparel.Clear();
            detachedEquipment.Clear();
            detachedInventory.Clear();
            detachedItems = false;
        }

        private void DestroyDetachedItems()
        {
            foreach (var apparel in detachedApparel)
                if (!apparel.Apparel.Destroyed)
                    apparel.Apparel.Destroy();

            foreach (var equipment in detachedEquipment)
                if (!equipment.Destroyed)
                    equipment.Destroy();

            foreach (var item in detachedInventory)
                if (!item.Destroyed)
                    item.Destroy();

            detachedApparel.Clear();
            detachedEquipment.Clear();
            detachedInventory.Clear();
            detachedItems = false;
        }

        private static Dictionary<ThingStyleDef, StyleCategoryDef> StyleOptionsFor(ThingDef thingDef)
        {
            var style = ListingMenu_Items.ThingStyles.FirstOrDefault(ts => ts.ThingDef == thingDef);
            return style.StyleDefs ?? new Dictionary<ThingStyleDef, StyleCategoryDef>();
        }

        private static ThingDef DefaultStuffFor(ThingDef thingDef)
        {
            if (!thingDef.MadeFromStuff)
            {
                return null;
            }

            return GenStuff.AllowedStuffsFor(thingDef).FirstOrDefault() ?? GenStuff.DefaultStuffFor(thingDef);
        }

        private static Color DefaultColorFor(ThingDef thingDef, ThingDef stuff)
        {
            if (stuff != null)
            {
                return stuff.stuffProps.color;
            }

            return thingDef.colorGenerator?.NewRandomizedColor() ?? thingDef.uiIconColor;
        }

    }
}
