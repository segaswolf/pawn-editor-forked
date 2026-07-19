using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Dialog for editing a pawn's visual appearance: body shape, hair, tattoos, and xenotype cosmetic genes.
/// The Xenotype tab uses CosmeticGeneDiscovery to dynamically show all cosmetic genes from loaded mods.
/// </summary>
[StaticConstructorOnStartup]
[HotSwappable]
public class Dialog_AppearanceEditor : Window, IDragLockable
{
    private bool windowLocked = true; // locked by default so the preview splitter is draggable
    public bool DragLocked => windowLocked;

    static Dialog_AppearanceEditor()
    {
        CosmeticGeneDiscovery.Initialize();
    }

    private readonly List<TabRecord> mainTabs = new(3);
    private readonly Pawn pawn;
    private readonly List<TabRecord> shapeTabs = new(2);
    private bool ignoreXenotype;

    private float lastColorHeight;
    private FloatMenuOption lastRandomization;
    private float lastXenotypeHeight;
    private MainTab mainTab;
    private Vector2 scrollPos;
    private int selectedColorIndex;
    private ShapeTab shapeTab;
    private ModContentPack sourceFilter;

    // Appearance lists used to be rebuilt (LINQ Where + ToList) every single frame; with 1000+
    // hairs/tattoos that re-filtered and allocated constantly (GC churn + CPU). Only ONE icon grid
    // and ONE color strip render per frame, so a single cache slot each is enough: rebuild only when
    // the key (tab / source filter / pawn state) changes.
    private object optionsCacheKey;
    private object optionsCacheVal;
    private object colorsCacheKey;
    private List<Color> colorsCacheVal;

    // v3.1: user-resizable preview panel. Drag the splitter to grow/shrink the pawn; the option grid
    // on the right reflows to fill the rest.
    private float leftPanelWidth = 280f; // bigger default so the pawn preview is clearly visible
    private bool draggingSplitter;

    public Dialog_AppearanceEditor(Pawn pawn)
    {
        this.pawn = pawn;
        closeOnClickedOutside = false;
        doCloseX = false;
        doCloseButton = false;
        closeOnCancel = false;

        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnAccept = false;
        closeOnCancel = true;
        forceCatchAcceptAndCancelEventEvenIfUnfocused = true;

        if (HARCompat.Active)
            HARCompat.Notify_AppearanceEditorOpen(pawn);
    }

    public override float Margin => 8;

    public override Vector2 InitialSize => new(1000, 700);

    public override void DoWindowContents(Rect inRect)
    {
        Widgets.BeginGroup(inRect);
        using (new TextBlock(GameFont.Medium))
        {
            var rect = inRect.TakeTopPart(Text.LineHeight * 2.5f);
            rect.y += Text.LineHeight / 4;
            using (new TextBlock(TextAnchor.UpperLeft))
                Widgets.Label(rect, "PawnEditor.EditAppearance".Translate());
            using (new TextBlock(TextAnchor.UpperRight))
            {
                Widgets.Label(rect, pawn.Name.ToStringShort + (", " + pawn.story.TitleCap).Colorize(ColoredText.SubtleGrayColor));
                var size = Text.CalcSize(pawn.Name.ToStringShort + ", " + pawn.story.TitleCap);
                GUI.DrawTexture(new(rect.xMax - size.x - rect.height * 0.6f, rect.y - Text.LineHeight / 4, rect.height * 0.6f, rect.height * 0.6f),
                    PawnEditor.GetPawnTex(pawn, new(rect.height, rect.height), Rot4.South));
            }
        }

        using (new TextBlock(GameFont.Small))
        {
            DrawBottomButtons(inRect.TakeBottomPart(50));

            // v3.1: resizable preview panel. The splitter lets the user grow/shrink the pawn preview;
            // the option grid on the right reflows to fill the remaining width.
            var contentWidth = inRect.width;
            var maxLeft = Mathf.Max(150f, contentWidth - 300f);
            leftPanelWidth = Mathf.Clamp(leftPanelWidth, 150f, maxLeft);
            var leftRect = inRect.TakeLeftPart(leftPanelWidth);
            HandleLeftSplitter(inRect.TakeLeftPart(14f), contentWidth);
            DoLeftSection(leftRect.ContractedBy(6, 0));

            mainTabs.Clear();
            mainTabs.Add(new("PawnEditor.Shape".Translate(), () => mainTab = MainTab.Shape, mainTab == MainTab.Shape));
            mainTabs.Add(new("PawnEditor.Hair".Translate().CapitalizeFirst(), () => mainTab = MainTab.Hair, mainTab == MainTab.Hair));
            if (ModsConfig.IdeologyActive)
                mainTabs.Add(new("Tattoos".Translate(), () => mainTab = MainTab.Tattoos, mainTab == MainTab.Tattoos));
            if (ModsConfig.BiotechActive)
                mainTabs.Add(new("Xenotype".Translate(), () => mainTab = MainTab.Xenotype, mainTab == MainTab.Xenotype));
            if (HARCompat.Active)
                mainTabs.Add(new("HAR.RaceFeatures".Translate(), () => mainTab = MainTab.HAR, mainTab == MainTab.HAR));

            Widgets.DrawMenuSection(inRect);
            TabDrawer.DrawTabs(inRect, mainTabs, 400f);
            inRect.yMin += 40;

            switch (mainTab)
            {
                case MainTab.Shape:
                    shapeTabs.Clear();
                    shapeTabs.Add(new("PawnEditor.Body".Translate(), () => shapeTab = ShapeTab.Body, shapeTab == ShapeTab.Body));
                    shapeTabs.Add(new("PawnEditor.Head".Translate().CapitalizeFirst(), () => shapeTab = ShapeTab.Head, shapeTab == ShapeTab.Head));
                    Widgets.DrawMenuSection(inRect);
                    TabDrawer.DrawTabs(inRect, shapeTabs);
                    switch (shapeTab)
                    {
                        case ShapeTab.Body:
                            var bodyTypes = CachedOptions(("body", sourceFilter, pawn.DevelopmentalStage, HARCompat.Active), () =>
                            {
                                IEnumerable<BodyTypeDef> q = DefDatabase<BodyTypeDef>.AllDefs.Where(h => MatchesSource(h) && IsAllowed(h, pawn))
                                    .Where(bodyType =>
                                        pawn.DevelopmentalStage switch
                                        {
                                            DevelopmentalStage.Baby or DevelopmentalStage.Newborn => bodyType == BodyTypeDefOf.Baby,
                                            DevelopmentalStage.Child => bodyType == BodyTypeDefOf.Child,
                                            DevelopmentalStage.Adult => bodyType != BodyTypeDefOf.Baby && bodyType != BodyTypeDefOf.Child,
                                            _ => true
                                        });
                                if (HARCompat.Active)
                                {
                                    var allowedBodyTypes = HARCompat.AllowedBodyTypes(pawn);
                                    if (!allowedBodyTypes.NullOrEmpty()) q = q.Intersect(allowedBodyTypes);
                                }
                                return q;
                            });

                            DoIconOptions(inRect.ContractedBy(5), bodyTypes, def =>
                                {
                                    pawn.story.bodyType = def;
                                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                                }, def => TexPawnEditor.BodyTypeIcons[def], def => pawn.story.bodyType == def, 1, new[] { pawn.story.SkinColor },
                                (color, i) =>
                                {
                                    pawn.story.skinColorOverride = color;
                                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                                }, ColorType.Misc,
                                CachedColors("colMisc", () => DefDatabase<ColorDef>.AllDefs.Select(static def => def.color)));
                            break;
                        case ShapeTab.Head:
                            var headTypes = CachedOptions(("head", sourceFilter, pawn.gender, HARCompat.Active), () =>
                            {
                                IEnumerable<HeadTypeDef> q = DefDatabase<HeadTypeDef>.AllDefs.Where(h => MatchesSource(h) && IsAllowed(h, pawn));
                                if (HARCompat.Active)
                                {
                                    q = HARCompat.FilterHeadTypes(q, pawn);
                                    // HAR doesn't like head types not matching genders
                                    q = q.Where(type => type.gender == Gender.None || type.gender == pawn.gender);
                                }
                                return q;
                            });

                            DoIconOptions(inRect.ContractedBy(5), headTypes, def =>
                                {
                                    pawn.story.headType = def;
                                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                                }, def => def.GetGraphic(pawn, pawn.story.HairColor).MatSouth.mainTexture,
                                def => pawn.story.headType == def, 1, new[] { pawn.story.SkinColor },
                                (color, i) =>
                                {
                                    pawn.story.skinColorOverride = color;
                                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                                }, ColorType.Misc,
                                CachedColors("colMisc", () => DefDatabase<ColorDef>.AllDefs.Select(static def => def.color)));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    break;
                case MainTab.Hair:
                    shapeTabs.Clear();
                    shapeTabs.Add(new("PawnEditor.Head".Translate().CapitalizeFirst(), () => shapeTab = ShapeTab.Head, shapeTab == ShapeTab.Head));
                    shapeTabs.Add(new("PawnEditor.Beard".Translate(), () => shapeTab = ShapeTab.Body, shapeTab == ShapeTab.Body));
                    Widgets.DrawMenuSection(inRect);
                    TabDrawer.DrawTabs(inRect, shapeTabs);
                    switch (shapeTab)
                    {
                        case ShapeTab.Head:
                            var hairEnforce = HARCompat.Active && HARCompat.EnforceRestrictions;
                            var hairTypes = CachedOptions(("hair", sourceFilter, hairEnforce), () =>
                            {
                                IEnumerable<HairDef> q = DefDatabase<HairDef>.AllDefs.Where(MatchesSource);
                                if (hairEnforce) q = q.Where(hair => HARCompat.AllowStyleItem(hair, pawn));
                                return q;
                            });
                            DoIconOptions(inRect.ContractedBy(5), hairTypes, def =>
                                {
                                    pawn.story.hairDef = def;
                                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                                }, def => def.Icon,
                                def => pawn.story.hairDef == def, 1, new[] { pawn.story.HairColor },
                                (color, i) =>
                                {
                                    pawn.story.HairColor = color;
                                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                                }, ColorType.Hair,
                                CachedColors("colHair", () => DefDatabase<ColorDef>.AllDefs.Where(static def => def.colorType == ColorType.Hair).Select(static def => def.color)));
                            break;
                        case ShapeTab.Body:
                            var beardEnforce = HARCompat.Active && HARCompat.EnforceRestrictions;
                            var beardTypes = CachedOptions(("beard", sourceFilter, beardEnforce), () =>
                            {
                                IEnumerable<BeardDef> q = DefDatabase<BeardDef>.AllDefs.Where(MatchesSource);
                                if (beardEnforce) q = q.Where(hair => HARCompat.AllowStyleItem(hair, pawn));
                                return q;
                            });
                            DoIconOptions(inRect.ContractedBy(5), beardTypes, def =>
                                {
                                    pawn.style.beardDef = def;
                                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                                }, def => def.Icon,
                                def => pawn.style.beardDef == def, 1, new[] { pawn.story.HairColor },
                                (color, i) =>
                                {
                                    pawn.story.HairColor = color;
                                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                                }, ColorType.Hair,
                                CachedColors("colHair", () => DefDatabase<ColorDef>.AllDefs.Where(static def => def.colorType == ColorType.Hair).Select(static def => def.color)));
                            break;
                    }

                    break;
                case MainTab.Tattoos:
                    shapeTabs.Clear();
                    shapeTabs.Add(new("PawnEditor.Body".Translate(), () => shapeTab = ShapeTab.Body, shapeTab == ShapeTab.Body));
                    shapeTabs.Add(new("PawnEditor.Head".Translate().CapitalizeFirst(), () => shapeTab = ShapeTab.Head, shapeTab == ShapeTab.Head));
                    Widgets.DrawMenuSection(inRect);
                    TabDrawer.DrawTabs(inRect, shapeTabs);
                    var tattooEnforce = HARCompat.Active && HARCompat.EnforceRestrictions;
                    switch (shapeTab)
                    {
                        case ShapeTab.Body:
                            var bodyTattoos = CachedOptions(("tattooBody", sourceFilter, tattooEnforce), () =>
                            {
                                IEnumerable<TattooDef> q = DefDatabase<TattooDef>.AllDefs.Where(MatchesSource);
                                if (tattooEnforce) q = q.Where(td => HARCompat.AllowStyleItem(td, pawn));
                                return q.Where(static td => td.tattooType == TattooType.Body);
                            });
                            DoIconOptions(inRect.ContractedBy(5), bodyTattoos, def =>
                                {
                                    pawn.style.BodyTattoo = def;
                                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                                }, static def => def.Icon,
                                def => pawn.style.BodyTattoo == def, 0, Array.Empty<Color>(), null, ColorType.Misc, null);
                            break;
                        case ShapeTab.Head:
                            var faceTattoos = CachedOptions(("tattooFace", sourceFilter, tattooEnforce), () =>
                            {
                                IEnumerable<TattooDef> q = DefDatabase<TattooDef>.AllDefs.Where(MatchesSource);
                                if (tattooEnforce) q = q.Where(td => HARCompat.AllowStyleItem(td, pawn));
                                return q.Where(static td => td.tattooType == TattooType.Face);
                            });
                            DoIconOptions(inRect.ContractedBy(5), faceTattoos, def =>
                                {
                                    pawn.style.FaceTattoo = def;
                                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                                }, static def => def.Icon,
                                def => pawn.style.FaceTattoo == def, 0, Array.Empty<Color>(), null, ColorType.Misc, null);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    break;
                case MainTab.Xenotype:
                    inRect.yMin -= TabDrawer.TabHeight;
                    DoXenotypeOptions(inRect.ContractedBy(5));
                    break;
                case MainTab.HAR:
                    HARCompat.DoRaceTabs(inRect.ContractedBy(5));
                    if (Event.current.type is EventType.MouseDown or EventType.Used)
                        TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        Widgets.EndGroup();
    }

    // Return the cached option list for the current key, rebuilding only when the key changed.
    private List<T> CachedOptions<T>(object key, Func<IEnumerable<T>> build)
    {
        if (optionsCacheVal is List<T> cached && Equals(optionsCacheKey, key)) return cached;
        // Only fires on a cache miss (tab/source/pawn change), so PerAction cadence stays cheap.
        var list = PawnEditorProfiler.Measure("AppearanceEditor.RebuildOptions", PawnEditorProfiler.Cadence.PerAction, () => build().ToList());
        optionsCacheVal = list;
        optionsCacheKey = key;
        return list;
    }

    // Color strips don't change during the dialog, so cache them by a simple identity key.
    private List<Color> CachedColors(object key, Func<IEnumerable<Color>> build)
    {
        if (colorsCacheVal != null && Equals(colorsCacheKey, key)) return colorsCacheVal;
        colorsCacheVal = build().ToList();
        colorsCacheKey = key;
        return colorsCacheVal;
    }

    private void DoIconOptions<T>(Rect inRect, List<T> options, Action<T> onSelected, Func<T, Texture> getIcon, Func<T, bool> isSelected, int colorCount,
        Color[] colors, Action<Color, int> setColor, ColorType colorType, List<Color> availableColors)
    {
        if (selectedColorIndex + 1 > colorCount) selectedColorIndex = 0;
        if (colorCount > 0)
        {
            var rect = new Rect(inRect.xMax - 26, inRect.yMax - 26, 18, 18);
            if (Widgets.ButtonImage(rect, Designator_Eyedropper.EyeDropperTex))
                Find.WindowStack.Add(new Dialog_ColorPicker(color => setColor(color, selectedColorIndex), availableColors, colors[selectedColorIndex]));

            for (var i = 0; i < colorCount; i++)
            {
                rect.x -= 26;
                Widgets.DrawBoxSolid(rect, colors[i]);
                if (selectedColorIndex == i) Widgets.DrawBox(rect);
                if (Widgets.ButtonInvisible(rect)) selectedColorIndex = i;
            }

            var oldColor = colors[selectedColorIndex];
            Widgets.ColorSelector(inRect.TakeBottomPart(lastColorHeight + 10).ContractedBy(4), ref colors[selectedColorIndex], availableColors,
                out lastColorHeight, colorSize: 18);
            if (colors[selectedColorIndex] != oldColor) setColor(colors[selectedColorIndex], selectedColorIndex);
        }

        var itemsPerRow = 9;
        var itemSize = (inRect.width - 20) / itemsPerRow;
        while (itemSize > 192)
        {
            itemsPerRow++;
            itemSize = (inRect.width - 20) / itemsPerRow;
        }

        while (itemSize < 48)
        {
            itemsPerRow--;
            itemSize = (inRect.width - 20) / itemsPerRow;
        }

        var viewRect = new Rect(0, 0, inRect.width - 20, Mathf.Ceil((float)options.Count / itemsPerRow) * itemSize);
        Widgets.BeginScrollView(inRect, ref scrollPos, viewRect);

        // Only draw the rows visible in the viewport (plus one row of margin each side). Without this,
        // 1000+ items meant 1000+ DrawTexture/Button/Tooltip calls per frame even though ~20 show.
        var firstRow = Mathf.Max(0, Mathf.FloorToInt(scrollPos.y / itemSize) - 1);
        var lastRow = Mathf.FloorToInt((scrollPos.y + inRect.height) / itemSize) + 1;
        var firstIndex = firstRow * itemsPerRow;
        var lastIndex = Mathf.Min(options.Count, (lastRow + 1) * itemsPerRow);

        for (var i = firstIndex; i < lastIndex; i++)
        {
            var option = options[i];
            var rect = new Rect(i % itemsPerRow * itemSize, Mathf.Floor((float)i / itemsPerRow) * itemSize, itemSize, itemSize).ContractedBy(6);
            Widgets.DrawHighlight(rect);

            if (option is Def def)
                if (Mouse.IsOver(rect))
                {
                    Widgets.DrawLightHighlight(rect);
                    var str = def.label ?? def.defName;
                    TooltipHandler.TipRegion(rect, str.CapitalizeFirst() + "\n\n" + "ModClickToSelect".Translate());
                }

            if (isSelected(option)) Widgets.DrawBox(rect);
            if (Widgets.ButtonInvisible(rect)) onSelected(option);

            GUI.DrawTexture(rect.ContractedBy(2), getIcon(option));
        }

        Widgets.EndScrollView();
    }

    private void DoXenotypeOptions(Rect inRect)
    {
        if (Event.current.type == EventType.Layout) lastXenotypeHeight = 9999;
        var viewRect = new Rect(0, 0, inRect.width - 20, lastXenotypeHeight);
        // Visible content band, passed to DoGeneOptions so it can cull off-screen gene rows/groups.
        var visMin = scrollPos.y;
        var visMax = scrollPos.y + inRect.height;
        Widgets.BeginScrollView(inRect, ref scrollPos, viewRect);
        for (var i = 0; i < CosmeticGeneDiscovery.GroupLabels.Count; i++) DoGeneOptions(ref viewRect, CosmeticGeneDiscovery.GroupLabels[i], CosmeticGeneDiscovery.GroupGenes[i], visMin, visMax);
        if (Event.current.type == EventType.Layout) lastXenotypeHeight -= viewRect.height;
        Widgets.EndScrollView();
    }

    private void DoGeneOptions(ref Rect inRect, string label, List<GeneDef> options, float visMin, float visMax)
    {
        Widgets.Label(inRect.TakeTopPart(Text.LineHeight), label);
        if (options.Count == 0)
        {
            Widgets.Label(inRect.TakeTopPart(Text.LineHeight).RightPart(0.9f), "PawnEditor.NoOptions".Translate().Colorize(ColoredText.SubtleGrayColor));
            return;
        }

        var itemsPerRow = 9;
        var itemSize = (inRect.width - 20) / itemsPerRow;
        while (itemSize > 192)
        {
            itemsPerRow++;
            itemSize = (inRect.width - 20) / itemsPerRow;
        }

        while (itemSize < 48)
        {
            itemsPerRow--;
            itemSize = (inRect.width - 20) / itemsPerRow;
        }

        var gridHeight = Mathf.Ceil((float)options.Count / itemsPerRow) * itemSize;
        var groupTop = inRect.yMin;
        Widgets.BeginGroup(inRect.TakeTopPart(gridHeight));

        // Cull gene rows outside the visible band (also skips whole groups scrolled off-screen). The
        // layout above still consumes the full height, so scrolling stays correct.
        var firstRow = Mathf.Max(0, Mathf.FloorToInt((visMin - groupTop) / itemSize) - 1);
        var lastRow = Mathf.FloorToInt((visMax - groupTop) / itemSize) + 1;
        var start = firstRow * itemsPerRow;
        var end = Mathf.Min(options.Count, (lastRow + 1) * itemsPerRow);
        for (var i = start; i < end; i++)
        {
            var option = options[i];
            var rect = new Rect(i % itemsPerRow * itemSize, Mathf.Floor((float)i / itemsPerRow) * itemSize, itemSize, itemSize).ContractedBy(2);
            bool enabled = GeneIsAllowed(option);
            Widgets.DrawHighlight(rect);
            if (pawn.genes.HasActiveGene(option))
            {
                Widgets.DrawBox(rect);
            }
            if (enabled && Widgets.ButtonInvisible(rect))
            {
                // Guard the whole add/remove against a gene from a removed mod (null/bad def):
                // without this, an exception here mid-render can hard-crash the game.
                try
                {
                    if (pawn.genes.HasActiveGene(option))
                    {
                        pawn.genes.RemoveGene(pawn.genes.GetGene(option));
                    }
                    else
                    {
                        // Genes in this group are mutually exclusive (same exclusionTag), so
                        // remove any the pawn already has from the group, THEN add the chosen one
                        // once. AddGene must be OUTSIDE the loop — inside it re-added the same gene
                        // once per group member (harmless but wasteful and confusing).
                        foreach (var geneDef in options)
                        {
                            if (pawn.genes.GetGene(geneDef) is { } gene) pawn.genes.RemoveGene(gene);
                        }

                        pawn.genes.AddGene(option, false);
                    }

                    TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Pawn Editor] Failed to toggle gene '{option?.defName ?? "null"}': {ex.Message}");
                }
            }

            GUI.color = enabled ? Color.white : Color.gray;
            // ToDo: Apply correct gene background texture according to gene category.
            GUI.DrawTexture(rect.ContractedBy(4), GeneUIUtility.GeneBackground_Endogene.Texture);
            GUI.color *= option.IconColor;
            GUI.DrawTexture(rect.ContractedBy(4), option.Icon);
            GUI.color = Color.white;

            TooltipHandler.TipRegion(rect, option.LabelCap + (enabled ? TaggedString.Empty : "\n\n" + "PawnEditor.XenotypeForbbiden".Translate()));
        }

        inRect.yMin += 8f;
        Widgets.EndGroup();
    }

    private bool GeneIsAllowed(GeneDef option)
    {
        if (ignoreXenotype) return true;
        if (HARCompat.Active && HARCompat.EnforceRestrictions && HARCompat.CanHaveGene(option, pawn) is false)
        {
            return false;
        }
        else if (pawn.IsBaseliner())
        {
            // For baseliners, allow all cosmetic genes (they're in the appearance editor for a reason)
            return true;
        }
        else if (pawn.genes.Xenotype != null)
        {
            return pawn.genes.Xenotype.AllGenes.Contains(option);
        }
        else if (pawn.genes.CustomXenotype != null)
        {
            return pawn.genes.CustomXenotype.genes.Contains(option);
        }

        return false;
    }

    // v3.1: draggable splitter that resizes the preview panel. mousePosition is group-relative, so its
    // x is the distance from the content's left edge = the new panel width.
    private void HandleLeftSplitter(Rect bar, float contentWidth)
    {
        // Always show a subtle handle so it's obviously draggable; brighten on hover/drag. Draw a small
        // vertical "grip" (two short lines) in the middle.
        var hot = Mouse.IsOver(bar) || draggingSplitter;
        Widgets.DrawBoxSolid(bar, new Color(1f, 1f, 1f, hot ? 0.18f : 0.06f));
        GUI.color = new Color(1f, 1f, 1f, hot ? 0.85f : 0.45f);
        var midY = bar.y + bar.height / 2f - 12f;
        Widgets.DrawLineVertical(bar.center.x - 2f, midY, 24f);
        Widgets.DrawLineVertical(bar.center.x + 2f, midY, 24f);
        GUI.color = Color.white;
        if (hot) Widgets.DrawHighlight(bar);

        var ev = Event.current;
        if (ev.type == EventType.MouseDown && ev.button == 0 && Mouse.IsOver(bar))
        {
            draggingSplitter = true;
            ev.Use();
        }
        else if (draggingSplitter && ev.type == EventType.MouseDrag)
        {
            leftPanelWidth = Mathf.Clamp(ev.mousePosition.x, 150f, Mathf.Max(150f, contentWidth - 300f));
            ev.Use();
        }
        else if (draggingSplitter && ev.rawType == EventType.MouseUp)
        {
            draggingSplitter = false;
        }
    }

    private void DoLeftSection(Rect inRect)
    {
        inRect.yMin -= 30f;

        // v3.1: lock toggle, kept at the TOP of the panel. Locked (default) keeps the window still so
        // you can drag the splitter to resize the preview; unlock to move the window around.
        if (Widgets.ButtonText(inRect.TakeTopPart(24f), windowLocked ? "Unlock window (move)" : "Lock window (resize)"))
            windowLocked = !windowLocked;
        inRect.yMin += 4f;

        // v3.1: preview scales with the panel width (drag the splitter to grow it), capped so the
        // controls below still fit. Square + centered to avoid stretching the pawn.
        var previewSide = Mathf.Clamp(inRect.width, 150f, inRect.height * 0.55f);
        // Snap to 24px steps so dragging the splitter doesn't render a NEW portrait texture every pixel
        // (PortraitsCache keys by size; unrounded sizes = per-frame RenderTexture churn = the old GC/
        // black-screen problem). The portrait only re-renders when it crosses a step.
        previewSide = Mathf.Round(previewSide / 24f) * 24f;
        var slot = inRect.TakeTopPart(previewSide);
        PawnEditor.DrawPawnPortrait(new Rect(slot.x + (slot.width - previewSide) / 2f, slot.y, previewSide, previewSide));

        // v3.1: open NL Facial Animation's own face editor for this pawn, if that mod is present.
        if (FacialAnimCompat.CanEditFace(pawn))
        {
            inRect.yMin += 2f;
            // Opens on a higher window layer, so it sits in FRONT of this window and the main editor
            // (both can stay open behind it).
            if (Widgets.ButtonText(inRect.TakeTopPart(28f), "Customize face"))
                FacialAnimCompat.OpenFaceEditor(pawn);
        }
        inRect.yMin += 8f;
        var buttonsRect = inRect.TakeTopPart(110);
        Widgets.DrawHighlight(buttonsRect);
        buttonsRect = buttonsRect.ContractedBy(4);

        using (new TextBlock(TextAnchor.MiddleCenter))
        {
            var sexRect = buttonsRect.TopHalf().LeftHalf().ContractedBy(2);
            Widgets.DrawHighlightIfMouseover(sexRect);

            if (Widgets.ButtonImageWithBG(sexRect.TakeTopPart(UIUtility.RegularButtonHeight), pawn.gender.GetIcon(), new Vector2(22f, 22f))
                && pawn.kindDef.fixedGender == null && pawn.RaceProps.hasGenders)
            {
                var list = new List<FloatMenuOption>
                {
                    new("Female".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetGender(pawn, Gender.Female), GenderUtility.FemaleIcon,
                        Color.white),
                    new("Male".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetGender(pawn, Gender.Male), GenderUtility.MaleIcon, Color.white)
                };

                Find.WindowStack.Add(new FloatMenu(list));
            }

            Widgets.Label(sexRect, "PawnEditor.Sex".Translate());

            TaggedString text;
            if (ModsConfig.BiotechActive)
            {
                var devStageRect = buttonsRect.TopHalf().RightHalf().ContractedBy(2);
                text = pawn.DevelopmentalStage.ToString().Translate().CapitalizeFirst();
                if (Mouse.IsOver(devStageRect))
                {
                    Widgets.DrawHighlight(devStageRect);
                    if (Find.WindowStack.FloatMenu == null)
                        TooltipHandler.TipRegion(devStageRect,
                            text.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + "DevelopmentalAgeSelectionDesc".Translate());
                }

                if (Widgets.ButtonImageWithBG(devStageRect.TakeTopPart(UIUtility.RegularButtonHeight), pawn.DevelopmentalStage.Icon().Texture,
                        new Vector2(22f, 22f)))
                {
                    var options = new List<FloatMenuOption>
                    {
                        new("Adult".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetDevStage(pawn, DevelopmentalStage.Adult),
                            DevelopmentalStageExtensions.AdultTex.Texture, Color.white),
                        new("Child".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetDevStage(pawn, DevelopmentalStage.Child),
                            DevelopmentalStageExtensions.ChildTex.Texture, Color.white),
                        new("Baby".Translate().CapitalizeFirst(), () => TabWorker_Bio_Humanlike.SetDevStage(pawn, DevelopmentalStage.Baby),
                            DevelopmentalStageExtensions.BabyTex.Texture, Color.white)
                    };
                    Find.WindowStack.Add(new FloatMenu(options));
                }

                Widgets.Label(devStageRect, text);

                var xenotypeRect = buttonsRect.BottomHalf().LeftHalf().ContractedBy(2);
                text = pawn.genes.XenotypeLabelCap;
                if (Mouse.IsOver(xenotypeRect))
                {
                    Widgets.DrawHighlight(xenotypeRect);
                    if (Find.WindowStack.FloatMenu == null)
                        TooltipHandler.TipRegion(xenotypeRect, text.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + "XenotypeSelectionDesc".Translate());
                }


                if (Widgets.ButtonImageWithBG(xenotypeRect.TakeTopPart(UIUtility.RegularButtonHeight), pawn.genes.XenotypeIcon, new Vector2(22f, 22f)))
                {
                    var list = new List<FloatMenuOption>();
                    foreach (var item in DefDatabase<XenotypeDef>.AllDefs.Where(x => x != pawn.genes.xenotype).OrderBy(x => 0f - x.displayPriority))
                    {
                        var xenotype = item;
                        list.Add(new(xenotype.LabelCap,
                            () => { SetXenotype(xenotype); }, xenotype.Icon, XenotypeDef.IconColor, MenuOptionPriority.Default,
                            r => TooltipHandler.TipRegion(r, xenotype.descriptionShort ?? xenotype.description), null, 24f,
                            r => Widgets.InfoCardButton(r.x, r.y + 3f, xenotype), extraPartRightJustified: true));
                    }

                    foreach (var customXenotype in CharacterCardUtility.CustomXenotypes.Where(x => x != pawn.genes.CustomXenotype))
                    {
                        var customInner = customXenotype;
                        list.Add(new(customInner.name.CapitalizeFirst() + " (" + "Custom".Translate() + ")",
                            delegate
                            {
                                // Use customInner (the per-iteration copy), NOT customXenotype: the loop
                                // variable is shared across all delegates, so referencing it directly would
                                // apply whichever xenotype was LAST in the loop, not the one clicked.
                                ApplyCustomXenotype(customInner);
                            }, customInner.IconDef.Icon, XenotypeDef.IconColor, MenuOptionPriority.Default, null, null, 24f, delegate(Rect r)
                            {
                                if (Widgets.ButtonImage(new(r.x, r.y + (r.height - r.width) / 2f, r.width, r.width), TexButton.Delete, GUI.color))
                                {
                                    Find.WindowStack.Add(new Dialog_Confirm("ConfirmDelete".Translate(customInner.name.CapitalizeFirst()), "ConfirmDeleteXenotype",
                                        delegate
                                        {
                                            var path = GenFilePaths.AbsFilePathForXenotype(customInner.name);
                                            if (File.Exists(path))
                                            {
                                                File.Delete(path);
                                                CharacterCardUtility.cachedCustomXenotypes = null;
                                            }
                                        }, true));
                                    return true;
                                }

                                return false;
                            }, extraPartRightJustified: true));
                    }

                    list.Add(new("XenotypeEditor".Translate() + "...",
                        delegate { Find.WindowStack.Add(new Dialog_CreateXenotype(-1, delegate { CharacterCardUtility.cachedCustomXenotypes = null; })); }));

                    Find.WindowStack.Add(new FloatMenu(list));
                }

                Widgets.Label(xenotypeRect, text.Truncate(xenotypeRect.width));
            }
        }

        inRect.yMin += 6;

        /* Doesn't seem to be doing anything at the moment, disabled for now to avoid new bug reports
        using (new TextBlock(GameFont.Tiny)) Widgets.Label(inRect.TakeTopPart(Text.LineHeight), "DominantStyle".Translate().CapitalizeFirst());

        if (Widgets.ButtonText(inRect.TakeTopPart(30).ContractedBy(3), "Default".Translate()))
            Messages.Message("PawnEditor.NoStyles".Translate(), MessageTypeDefOf.RejectInput, false);

        inRect.yMin += 4;

        */
        using (new TextBlock(GameFont.Tiny)) Widgets.Label(inRect.TakeTopPart(Text.LineHeight), "Source".Translate().CapitalizeFirst());

        if (mainTab != MainTab.HAR && Widgets.ButtonText(inRect.TakeTopPart(30).ContractedBy(3), sourceFilter?.Name ?? "PawnEditor.All".Translate().CapitalizeFirst()))
        {
            var allDefs = GetAllDefsForTab(mainTab, shapeTab);
            var options = LoadedModManager.RunningMods.Intersect(allDefs.Select(def => def.modContentPack).Distinct())
                .Where(x => x != null)
                .Select(mod => new FloatMenuOption(mod.Name, () => sourceFilter = mod))
                .Prepend(new(
                    "PawnEditor.All".Translate().CapitalizeFirst(), () => sourceFilter = null))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        if (ModsConfig.BiotechActive)
            Widgets.CheckboxLabeled(inRect.TakeBottomPart(50), "PawnEditor.IgnoreXenotype".Translate(), ref ignoreXenotype);
        Widgets.CheckboxLabeled(inRect.TakeBottomPart(30), "PawnEditor.ShowApparel".Translate(), ref PawnEditor.RenderClothes);
        Widgets.CheckboxLabeled(inRect.TakeBottomPart(30), "PawnEditor.ShowHeadgear".Translate(), ref PawnEditor.RenderHeadgear);
    }

    private void SetXenotype(XenotypeDef xenotype)
    {
        for (int num = pawn.genes.endogenes.Count - 1; num >= 0; num--)
        {
            pawn.genes.RemoveGene(pawn.genes.endogenes[num]);
        }

        pawn.genes.ClearXenogenes();
        PawnGenerator.GenerateGenes(pawn, xenotype, default);
    }

    /// <summary>
    /// Applies a custom xenotype to the pawn. Guards against gene defs that are null — which
    /// happens when a saved custom xenotype references genes from a mod that is no longer
    /// installed. Without this guard, AddGene(null, ...) throws and (in game, mid-render) can
    /// hard-crash. Each gene is added in isolation so one missing gene can't abort the rest.
    /// </summary>
    private void ApplyCustomXenotype(CustomXenotype customXenotype)
    {
        if (customXenotype == null) return;
        try
        {
            if (!pawn.IsBaseliner()) pawn.genes.SetXenotype(XenotypeDefOf.Baseliner);
            pawn.genes.xenotypeName = customXenotype.name;
            pawn.genes.iconDef = customXenotype.IconDef;

            if (customXenotype.genes != null)
            {
                foreach (var geneDef in customXenotype.genes)
                {
                    if (geneDef == null) continue; // gene from a removed mod — skip instead of crashing
                    try { pawn.genes.AddGene(geneDef, !customXenotype.inheritable); }
                    catch (Exception ex) { Log.Warning($"[Pawn Editor] Skipped a gene while applying custom xenotype '{customXenotype.name}': {ex.Message}"); }
                }
            }

            TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
        }
        catch (Exception ex)
        {
            Log.Error($"[Pawn Editor] Failed to apply custom xenotype '{customXenotype.name}': {ex.Message}");
        }
    }

    private void DrawBottomButtons(Rect inRect)
    {
        if (Widgets.ButtonText(inRect.TakeLeftPart(210).ContractedBy(5), "PawnEditor.GotoGearTab".Translate()))
        {
            Close();
            PawnEditor.Select(pawn);
            PawnEditor.GotoTab(PawnEditorDefOf.Gear);
        }

        if (Widgets.ButtonText(inRect.TakeRightPart(210).ContractedBy(5), "Accept".Translate()))
        {
            OnAcceptKeyPressed();
            Close();
        }

        var randomRect = new Rect(0, 0, 200, 40).CenteredOnXIn(inRect).CenteredOnYIn(inRect);

        if (lastRandomization != null && Widgets.ButtonImageWithBG(randomRect.TakeRightPart(20), TexUI.RotRightTex, new Vector2(12, 12)))
        {
            lastRandomization.action();
            randomRect.TakeRightPart(1);
        }

        if (Widgets.ButtonText(randomRect, "Randomize".Translate()))
        {
            var initialOptions = new List<FloatMenuOption>
            {
                new("PawnEditor.Shape".Translate(), () =>
                {
                    pawn.story.bodyType = (BodyTypeDef)GetAllDefsForTab(MainTab.Shape, ShapeTab.Body).Where(MatchesSource).RandomElement();
                    PawnEditor.RefreshPawnGraphics(pawn);
                }),
                new("PawnEditor.Head".Translate().CapitalizeFirst(), () =>
                {
                    pawn.story.headType = (HeadTypeDef)GetAllDefsForTab(MainTab.Shape, ShapeTab.Head).Where(MatchesSource).RandomElement();
                    PawnEditor.RefreshPawnGraphics(pawn);
                }),
            };

            if (ModsConfig.IdeologyActive)
            {
                initialOptions.Add(new("Tattoos".Translate(), () =>
                {
                    pawn.style.FaceTattoo = DefDatabase<TattooDef>.AllDefs.Where(MatchesSource).RandomElement();
                    pawn.style.BodyTattoo = DefDatabase<TattooDef>.AllDefs.Where(MatchesSource).RandomElement();
                    PawnEditor.RefreshPawnGraphics(pawn);
                }));
            }

            var options = initialOptions.Select(opt => new FloatMenuOption("Randomize".Translate() + " " + opt.Label, () =>
                {
                    lastRandomization = opt;
                    opt.action();
                }))
                .ToList();

            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    private bool MatchesSource(Def def) => sourceFilter == null || def.modContentPack == sourceFilter;

    private bool IsAllowed(HeadTypeDef def, Pawn p)
    {
        if (ignoreXenotype) return true;
        if (ModsConfig.BiotechActive && !def.requiredGenes.NullOrEmpty())
        {
            if (p.genes == null)
            {
                return false;
            }

            foreach (GeneDef requiredGene in def.requiredGenes)
            {
                if (!pawn.genes.HasActiveGene(requiredGene))
                {
                    return false;
                }
            }
        }

        if (def.gender != 0)
        {
            return def.gender == p.gender;
        }

        return def.randomChosen;
    }

    private bool IsAllowed(BodyTypeDef def, Pawn p)
    {
        if (ignoreXenotype) return true;
        if (ModsConfig.BiotechActive && pawn.DevelopmentalStage.Juvenile())
        {
            return def == BodyTypeDefOf.Baby || def == BodyTypeDefOf.Child;
        }

        return true;
    }

    private IEnumerable<Def> GetAllDefsForTab(MainTab tab, ShapeTab shape)
    {
        switch (tab)
        {
            case MainTab.Shape:
                switch (shape)
                {
                    case ShapeTab.Body:
                        var bodyTypes = DefDatabase<BodyTypeDef>.AllDefs
                            .Where(bodyType =>
                                pawn.DevelopmentalStage switch
                                {
                                    DevelopmentalStage.Baby or DevelopmentalStage.Newborn => bodyType == BodyTypeDefOf.Baby,
                                    DevelopmentalStage.Child => bodyType == BodyTypeDefOf.Child,
                                    DevelopmentalStage.Adult => bodyType != BodyTypeDefOf.Baby && bodyType != BodyTypeDefOf.Child,
                                    _ => true
                                });

                        if (HARCompat.Active)
                        {
                            var allowedBodyTypes = HARCompat.AllowedBodyTypes(pawn);
                            if (!allowedBodyTypes.NullOrEmpty()) bodyTypes = bodyTypes.Intersect(allowedBodyTypes);
                        }

                        return bodyTypes;
                    case ShapeTab.Head:
                        var headTypes = DefDatabase<HeadTypeDef>.AllDefs;
                        if (HARCompat.Active)
                        {
                            headTypes = HARCompat.FilterHeadTypes(headTypes, pawn);
                            // HAR doesn't like head types not matching genders
                            headTypes = headTypes.Where(type => (type.gender == Gender.None || type.gender == pawn.gender) && type.randomChosen);
                        }

                        return headTypes;
                }

                break;
            case MainTab.Hair:
                return DefDatabase<HairDef>.AllDefsListForReading;
            case MainTab.Tattoos:
                return shape switch
                {
                    ShapeTab.Body => DefDatabase<TattooDef>.AllDefs.Where(td => td.tattooType == TattooType.Body).Cast<Def>().ToList(),
                    ShapeTab.Head => DefDatabase<TattooDef>.AllDefs.Where(td => td.tattooType == TattooType.Face).Cast<Def>().ToList(),
                    _ => new()
                };
            case MainTab.Xenotype:
                return CosmeticGeneDiscovery.GroupGenes.SelectMany(defs => defs.Cast<Def>());
        }

        return Enumerable.Empty<Def>();
    }

    private enum MainTab
    {
        Shape,
        Hair,
        Tattoos,
        Xenotype,
        HAR
    }

    private enum ShapeTab
    {
        Body,
        Head
    }
}