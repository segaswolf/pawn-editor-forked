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
public class Dialog_AppearanceEditor : Window, IDragLockable, IMinWindowSize
{
    // Enforced by the resizer, so the panels never get laid out at an impossible size for one frame.
    public Vector2 MinWindowSize => new(900f, 620f);

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
    private FacialAnimCompat.FacePart facialAnimationPart;
    private bool draggingPreview;
    private Vector3 pickerPreviewCameraOffset = new(0f, 0f, 0.12f);
    private Rot4 pickerPreviewRotation = Rot4.South;
    private float pickerPreviewZoom = 1.35f;
    private MainTab mainTab;
    private Vector2 scrollPos;
    private int selectedColorIndex;
    private ShapeTab shapeTab;
    private ModContentPack sourceFilter;
    private int xenotypeCategoryIndex = -1;

    // Appearance lists used to be rebuilt (LINQ Where + ToList) every single frame; with 1000+
    // hairs/tattoos that re-filtered and allocated constantly (GC churn + CPU). Only ONE icon grid
    // and ONE color strip render per frame, so a single cache slot each is enough: rebuild only when
    // the key (tab / source filter / pawn state) changes.
    private object optionsCacheKey;
    private object optionsCacheVal;
    private object colorsCacheKey;
    private List<Color> colorsCacheVal;

    // Preview panel width: purely proportional to the window (see ProportionalLeftWidth). Resize the
    // whole editor from its corner and the preview reflows with it. No splitter — it clashed with the
    // drag-to-rotate preview.
    private float leftPanelWidth = 280f;

    // Below/above these window widths the layout changes shape (like CSS breakpoints).
    private const float WideBreakpoint = 1250f;   // room for a bigger preview + side-by-side controls
    private const float NarrowBreakpoint = 1000f; // keep the preview modest so options don't get cramped

    // When the option grid is at least this wide, the colour palette moves to a side column of this
    // width instead of stacking under the grid.
    private const float ColorColumnBreakpoint = 720f;
    private const float ColorColumnWidth = 220f;

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
        // Enforce a minimum size: below this the left panel's controls and the option grid start
        // overlapping into a mess (the resizer would otherwise let you shrink it to nothing).
        if (windowRect.width < 900f || windowRect.height < 620f)
        {
            windowRect.width = Mathf.Max(windowRect.width, 900f);
            windowRect.height = Mathf.Max(windowRect.height, 620f);
        }

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

            // v3.3: the preview panel width is now purely PROPORTIONAL to the window. Resize the whole
            // editor from its bottom-right corner and everything reflows — bigger preview AND more option
            // columns as it widens. The old draggable splitter was removed on purpose: it clashed with
            // the drag-to-rotate preview (two drags fighting over the same strip), and the window's own
            // corner handle already covers the "make it bigger" need.
            var contentWidth = inRect.width;
            var maxLeft = Mathf.Max(150f, contentWidth - 300f);
            leftPanelWidth = Mathf.Clamp(ProportionalLeftWidth(windowRect.width), 150f, maxLeft);
            var leftRect = inRect.TakeLeftPart(leftPanelWidth);
            inRect.xMin += 8f; // small gap between the preview panel and the options
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
            if (FacialAnimCompat.Active && FacialAnimCompat.HasFaceControls(pawn))
                mainTabs.Add(new("PawnEditor.FA.Tab".Translate(), () => mainTab = MainTab.FacialAnimation, mainTab == MainTab.FacialAnimation));

            if (mainTab == MainTab.FacialAnimation && !FacialAnimCompat.HasFaceControls(pawn))
                mainTab = MainTab.Shape;

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
                                }, def => TexPawnEditor.GetBodyTypeIcon(def), def => pawn.story.bodyType == def, 1, new[] { pawn.story.SkinColor },
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
                            var hairRect = inRect.ContractedBy(5);
                            // Gradient Hair support (optional mod): a toggle + second-colour picker sits
                            // above the hair grid, only when the mod is present and this pawn can use it.
                            DoGradientHairRow(ref hairRect);
                            DoIconOptions(hairRect, hairTypes, def =>
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
                    DoXenotypePicker(inRect.ContractedBy(5));
                    break;
                case MainTab.HAR:
                    HARCompat.DoRaceTabs(inRect.ContractedBy(5));
                    if (Event.current.type is EventType.MouseDown or EventType.Used)
                        TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                    break;
                case MainTab.FacialAnimation:
                    DoFacialAnimationOptions(inRect.ContractedBy(5));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        Widgets.EndGroup();
    }

    /// <summary>
    /// How many frames an icon has to keep coming back null before we say anything about it.
    ///
    /// A null texture is NOT proof that the art is broken. Faster Game Loading (and mods like it) load
    /// textures on a background queue — verified in its own source, which keeps a ConcurrentQueue of
    /// load requests with cancellation — so an icon can legitimately be null for a while and then
    /// appear. Reporting on the first miss named other authors' mods as broken when their art was
    /// merely still in flight. The grid redraws every frame, so anything still missing after this many
    /// draws really is missing.
    /// </summary>
    private const int MissesBeforeReportingIcon = 180;

    // Per-def count of consecutive draws with no texture. An entry reaching the threshold reports once
    // and then stays put so the log gets a single line per def, not one per frame.
    private static readonly Dictionary<string, int> MissingIconDraws = new();

    /// <summary>
    /// Draws an option's icon, but never hands a null texture to the GPU — that's what floods the log
    /// with "null texture passed to GUI.DrawTexture" (thousands per second while the grid is open) and
    /// it's usually another mod's hair/tattoo/gene whose art failed to load.
    ///
    /// Instead we draw a visible placeholder and, only after the icon has stayed null for a while, log
    /// a single line naming the def and the mod it came from — so the author (or the user) can see what
    /// to look at, without accusing anyone whose texture was simply still loading.
    /// </summary>
    private static void SafeDrawIcon<T>(Rect rect, Texture icon, T option, Rect? texCoords = null)
    {
        if (icon != null)
        {
            // texCoords crops the source texture (e.g. Facial Animation's eye icon uses a sub-rect).
            if (texCoords.HasValue)
                GUI.DrawTextureWithTexCoords(rect, icon, texCoords.Value);
            else
                GUI.DrawTexture(rect, icon);

            ForgetMissingIcon(option);
            return;
        }

        GUI.DrawTexture(rect, BaseContent.BadTex);
        NoteMissingIcon(option);
    }

    /// <summary>The icon turned up after all, so the def starts from zero if it ever goes missing again.</summary>
    private static void ForgetMissingIcon<T>(T option)
    {
        if (MissingIconDraws.Count == 0) return;
        if (option is Def def && def.defName != null) MissingIconDraws.Remove(def.defName);
    }

    /// <summary>
    /// Counts a draw with no texture and reports exactly once, when the count crosses the threshold.
    /// Counting past it does nothing, which is what keeps this to one log line per def.
    /// </summary>
    private static void NoteMissingIcon<T>(T option)
    {
        if (option is not Def def || def.defName == null) return;

        MissingIconDraws.TryGetValue(def.defName, out var misses);
        if (misses > MissesBeforeReportingIcon) return;

        MissingIconDraws[def.defName] = ++misses;
        if (misses <= MissesBeforeReportingIcon) return;

        var mod = def.modContentPack?.Name ?? "unknown mod";
        Log.Warning($"[Pawn Editor] '{def.defName}' (from {mod}) still has no icon texture after "
                    + $"{MissesBeforeReportingIcon} draws, so a placeholder is shown in the appearance editor. "
                    + "This is that mod's asset rather than Pawn Editor's, and the texture path may be worth "
                    + "checking — though a mod that loads textures in the background can also delay it.");
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
        Color[] colors, Action<Color, int> setColor, ColorType colorType, List<Color> availableColors, int preferredItemsPerRow = 9,
        Rect? iconTexCoords = null)
    {
        if (selectedColorIndex + 1 > colorCount) selectedColorIndex = 0;
        if (colorCount > 0)
        {
            // When there's horizontal room, the colour palette moves to a column ON THE RIGHT of the
            // grid instead of stacking underneath it. On a wide window the old bottom stack left the
            // grid half-empty above a huge palette; side-by-side uses the width the responsive layout
            // now gives us. Narrow windows keep the exact old bottom-stacked behaviour.
            var sideColumn = inRect.width >= ColorColumnBreakpoint;
            if (sideColumn)
            {
                var colRect = inRect.TakeRightPart(ColorColumnWidth);
                inRect.xMax -= 8f; // gap between grid and colour column

                // Swatches + eyedropper across the top of the column.
                var swatchRow = colRect.TakeTopPart(26f);
                var eyedrop = swatchRow.TakeRightPart(24f).ContractedBy(3f);
                if (Widgets.ButtonImage(eyedrop, Designator_Eyedropper.EyeDropperTex))
                    Find.WindowStack.Add(new Dialog_ColorPicker(color => setColor(color, selectedColorIndex), availableColors, colors[selectedColorIndex]));
                for (var i = 0; i < colorCount; i++)
                {
                    var sw = swatchRow.TakeLeftPart(24f).ContractedBy(3f);
                    Widgets.DrawBoxSolid(sw, colors[i]);
                    if (selectedColorIndex == i) Widgets.DrawBox(sw);
                    if (Widgets.ButtonInvisible(sw)) selectedColorIndex = i;
                }

                var oldC = colors[selectedColorIndex];
                Widgets.ColorSelector(colRect.ContractedBy(2f), ref colors[selectedColorIndex], availableColors, out lastColorHeight, colorSize: 18);
                if (colors[selectedColorIndex] != oldC) setColor(colors[selectedColorIndex], selectedColorIndex);
            }
            else
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
        }

        var availableWidth = Mathf.Max(48f, inRect.width - 20f);
        var itemsPerRow = Mathf.Max(1, preferredItemsPerRow);
        var itemSize = availableWidth / itemsPerRow;
        while (itemSize > 192)
        {
            itemsPerRow++;
            itemSize = availableWidth / itemsPerRow;
        }

        while (itemSize < 48 && itemsPerRow > 1)
        {
            itemsPerRow--;
            itemSize = availableWidth / itemsPerRow;
        }

        var viewRect = new Rect(0, 0, availableWidth, Mathf.Ceil((float)options.Count / itemsPerRow) * itemSize);
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

            // Merged: Lucius's texcoords support (the FA eye crops its icon) + our SafeDrawIcon
            // (placeholder + one-time warning naming the mod when an icon texture is missing).
            SafeDrawIcon(rect.ContractedBy(2), getIcon(option), option, iconTexCoords);
        }

        Widgets.EndScrollView();
    }

    /// <summary>
    /// Draws the Gradient Hair controls (a toggle + a second-colour swatch) at the top of the hair grid.
    /// No-op unless the Gradient Hair mod is installed AND this pawn has its comp — so vanilla pawns,
    /// mechs, and players without the mod see exactly the old layout. Second colour opens the same
    /// colour picker the rest of the editor uses, seeded with the hair palette.
    /// </summary>
    private void DoGradientHairRow(ref Rect inRect)
    {
        if (!GradientHairCompat.Active) return;
        if (!GradientHairCompat.TryGet(pawn, out var enabled, out var colorB)) return;

        var row = inRect.TakeTopPart(30f);
        inRect.yMin += 6f; // gap before the grid

        // Toggle on the left.
        var toggleRect = row.TakeLeftPart(Mathf.Min(190f, row.width * 0.5f));
        var wasEnabled = enabled;
        Widgets.CheckboxLabeled(toggleRect, "PawnEditor.GradientHair".Translate(), ref enabled);
        if (enabled != wasEnabled)
        {
            GradientHairCompat.Set(pawn, enabled, colorB);
            TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
        }

        if (!enabled) return;

        // Second-colour swatch + label on the right of the row.
        var swatch = row.TakeRightPart(28f).ContractedBy(3f);
        Widgets.DrawBoxSolid(swatch, colorB);
        Widgets.DrawBox(swatch);
        if (Widgets.ButtonInvisible(swatch))
        {
            var palette = CachedColors("colHair", () => DefDatabase<ColorDef>.AllDefs
                .Where(static def => def.colorType == ColorType.Hair).Select(static def => def.color));
            Find.WindowStack.Add(new Dialog_ColorPicker(c =>
            {
                GradientHairCompat.Set(pawn, true, c);
                TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
            }, palette, colorB));
        }

        using (new TextBlock(TextAnchor.MiddleRight))
            Widgets.Label(row, "PawnEditor.GradientHairSecondColor".Translate());

        // Gentle heads-up (not our bug): without the "Gradient Hair Fixes" mod, older hairs using the
        // legacy _back/_side/_front texture naming render blank. We don't take over their render; we
        // just point the user at the fix so it doesn't look like our editor broke the hair.
        if (GradientHairCompat.ShouldRecommendFixes)
        {
            var hint = inRect.TakeTopPart(Text.LineHeight);
            inRect.yMin += 4f;
            using (new TextBlock(GameFont.Tiny))
            {
                GUI.color = ColoredText.SubtleGrayColor;
                Widgets.Label(hint, "PawnEditor.GradientHairFixesHint".Translate());
                GUI.color = Color.white;
            }
        }
    }

    private void DoXenotypePicker(Rect inRect)
    {
        DrawXenotypeFilters(inRect.TakeTopPart(UIUtility.RegularButtonHeight));
        inRect.yMin += 4f;

        var overrideRect = inRect.TakeTopPart(30f);
        Widgets.CheckboxLabeled(overrideRect, "PawnEditor.OverrideUnsupportedGenes".Translate(), ref ignoreXenotype);
        TooltipHandler.TipRegion(overrideRect, "PawnEditor.OverrideUnsupportedGenes.Desc".Translate());
        inRect.yMin += 4f;

        DoXenotypeOptions(inRect);
    }

    private void DrawXenotypeFilters(Rect row)
    {
        var sourceRect = row.LeftHalf().ContractedBy(1f, 0f);
        var categoryRect = row.RightHalf().ContractedBy(1f, 0f);
        var sourceLabel = "PawnEditor.Source".Translate().CapitalizeFirst() + ": " +
                          (sourceFilter?.Name ?? "PawnEditor.All".Translate().CapitalizeFirst().ToString());
        if (Widgets.ButtonText(sourceRect, sourceLabel))
        {
            var sourceDefs = CosmeticGeneDiscovery.GroupGenes.SelectMany(defs => defs.Cast<Def>());
            var options = LoadedModManager.RunningMods
                .Intersect(sourceDefs.Select(def => def.modContentPack).Where(mod => mod != null).Distinct())
                .Select(mod => new FloatMenuOption(mod.Name, () => SetXenotypeSource(mod)))
                .Prepend(new FloatMenuOption("PawnEditor.All".Translate().CapitalizeFirst(), () => SetXenotypeSource(null)))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }
        TooltipHandler.TipRegion(sourceRect, "PawnEditor.SourceDesc".Translate());

        var categoryLabel = xenotypeCategoryIndex >= 0 && xenotypeCategoryIndex < CosmeticGeneDiscovery.GroupLabels.Count
            ? CosmeticGeneDiscovery.GroupLabels[xenotypeCategoryIndex]
            : "PawnEditor.All".Translate().ToString().CapitalizeFirst();
        if (Widgets.ButtonText(categoryRect, "PawnEditor.Category".Translate().CapitalizeFirst() + ": " + categoryLabel))
        {
            var options = new List<FloatMenuOption>
            {
                new("PawnEditor.All".Translate().CapitalizeFirst(), () => SetXenotypeCategory(-1))
            };
            for (var i = 0; i < CosmeticGeneDiscovery.GroupLabels.Count; i++)
            {
                var index = i;
                options.Add(new FloatMenuOption(CosmeticGeneDiscovery.GroupLabels[index], () => SetXenotypeCategory(index)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        TooltipHandler.TipRegion(categoryRect, "PawnEditor.XenotypeCategoryDesc".Translate());
    }

    private void SetXenotypeSource(ModContentPack source)
    {
        sourceFilter = source;
        scrollPos = Vector2.zero;
        optionsCacheKey = null;
        optionsCacheVal = null;
    }

    private void SetXenotypeCategory(int index)
    {
        xenotypeCategoryIndex = index;
        scrollPos = Vector2.zero;
    }

    private void DoXenotypeOptions(Rect inRect)
    {
        if (Event.current.type == EventType.Layout) lastXenotypeHeight = 9999;
        var viewRect = new Rect(0, 0, Mathf.Max(1f, inRect.width - 20f), lastXenotypeHeight);
        // Visible content band, passed to DoGeneOptions so it can cull off-screen gene rows/groups.
        var visMin = scrollPos.y;
        var visMax = scrollPos.y + inRect.height;
        Widgets.BeginScrollView(inRect, ref scrollPos, viewRect);
        for (var i = 0; i < CosmeticGeneDiscovery.GroupLabels.Count; i++)
        {
            if (xenotypeCategoryIndex >= 0 && xenotypeCategoryIndex != i)
                continue;

            var options = CosmeticGeneDiscovery.GroupGenes[i].Where(MatchesSource).ToList();
            if (xenotypeCategoryIndex >= 0 || options.Count > 0)
                DoGeneOptions(ref viewRect, CosmeticGeneDiscovery.GroupLabels[i], options, visMin, visMax);
        }
        if (Event.current.type == EventType.Layout) lastXenotypeHeight -= viewRect.height;
        Widgets.EndScrollView();
    }

    private void DoFacialAnimationOptions(Rect inRect)
    {
        var parts = FacialAnimCompat.GetFaceParts().ToList();
        if (parts.Count == 0)
            return;

        if (facialAnimationPart == null || !parts.Contains(facialAnimationPart))
            facialAnimationPart = parts[0];

        shapeTabs.Clear();
        foreach (var part in parts)
        {
            shapeTabs.Add(new TabRecord(part.Label, () =>
            {
                facialAnimationPart = part;
                scrollPos = Vector2.zero;
            }, facialAnimationPart == part));
        }

        TabDrawer.DrawTabs(inRect, shapeTabs, 400f);
        inRect.yMin += TabDrawer.TabHeight + 6f;

        if (facialAnimationPart.Key == "eyes")
        {
            DrawFacialAnimationColor(inRect.TakeTopPart(36f).ContractedBy(2f), "PawnEditor.FA.EyeColor".Translate(),
                FacialAnimCompat.GetEyeColor(pawn), color => FacialAnimCompat.SetEyeColor(pawn, color));
            inRect.yMin += 4f;
            DrawFacialAnimationColor(inRect.TakeTopPart(36f).ContractedBy(2f), "PawnEditor.FA.EyeSecondColor".Translate(),
                FacialAnimCompat.GetSecondEyeColor(pawn), color => FacialAnimCompat.SetSecondEyeColor(pawn, color));
            inRect.yMin += 8f;
        }

        var options = FacialAnimCompat.GetApplicableFaceTypeDefs(pawn, facialAnimationPart)
            .Where(MatchesSource)
            .ToList();
        DoIconOptions(inRect, options, def => FacialAnimCompat.SetFaceType(pawn, facialAnimationPart, def),
            def => FacialAnimCompat.GetFaceTypeIcon(def, pawn),
            def => FacialAnimCompat.GetFaceType(pawn, facialAnimationPart) == def,
            0, Array.Empty<Color>(), null, ColorType.Misc, null, 5,
            facialAnimationPart.Key == "eyes" ? new Rect(0.34f, 0.315f, 0.32f, 0.27f) : null);
    }

    private static void DrawFacialAnimationColor(Rect row, string label, Color? current, Action<Color> onSelected)
    {
        var labelRect = row.TakeLeftPart(Mathf.Min(150f, row.width * 0.36f));
        Widgets.Label(labelRect, label);
        row.xMin += 8f;
        var color = current ?? Color.white;
        var swatch = row.TakeRightPart(36f).ContractedBy(4f);
        Widgets.DrawBoxSolid(swatch, color);
        Widgets.DrawBox(swatch);

        if (Widgets.ButtonText(row, "PawnEditor.PickColor".Translate()))
            Find.WindowStack.Add(new Dialog_ColorPicker(onSelected, DefDatabase<ColorDef>.AllDefs.Select(def => def.color).ToList(), color));
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
            SafeDrawIcon(rect.ContractedBy(4), option.Icon, option);
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

    /// <summary>
    /// Width of the preview panel as a fraction of the window, clamped so it never dominates a huge
    /// window or starves the options on a small one. This is what makes the editor feel responsive:
    /// resize the window from its corner and the pawn preview grows/shrinks with it.
    /// </summary>
    private static float ProportionalLeftWidth(float windowWidth)
    {
        // ~32% of the window, but held between a readable minimum and a cap so options keep their room.
        var min = windowWidth <= NarrowBreakpoint ? 240f : 280f;
        var max = windowWidth >= WideBreakpoint ? 480f : 400f;
        return Mathf.Clamp(windowWidth * 0.32f, min, max);
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
        // Reserve the WHOLE bottom block (Source + the three checkboxes) before sizing anything else.
        // This is the real fix for "Source lands on top of Show headgear": the preview grows with the
        // panel WIDTH, so widening the window made it taller and ate every pixel below it. Reserving
        // afterwards was useless, because by then the space was already gone.
        var bottomBlock = inRect.TakeBottomPart(Mathf.Min(
            Text.LineHeight + 34f + 60f + (ModsConfig.BiotechActive ? 50f : 0f),
            inRect.height * 0.5f));

        // Cap the preview by what is ACTUALLY free below it (face button + the sex/age/xenotype block)
        // instead of a blind 55% of the height, which didn't account for those.
        var neededBelow = 8f + 110f + 6f
            + (FacialAnimCompat.CanEditFace(pawn) ? 30f : 0f)
            + (AnthrosonaeCompat.HasFur(pawn) ? 30f : 0f);
        var previewSide = Mathf.Clamp(inRect.width, 150f, Mathf.Max(150f, inRect.height - neededBelow));
        // Snap to 24px steps so dragging the splitter doesn't render a NEW portrait texture every pixel
        // (PortraitsCache keys by size; unrounded sizes = per-frame RenderTexture churn = the old GC/
        // black-screen problem). The portrait only re-renders when it crosses a step.
        previewSide = Mathf.Round(previewSide / 24f) * 24f;
        var slot = inRect.TakeTopPart(previewSide);
        PawnEditor.DrawInteractivePawnPreview(
            new Rect(slot.x + (slot.width - previewSide) / 2f, slot.y, previewSide, previewSide),
            pawn, ref draggingPreview, ref pickerPreviewRotation, ref pickerPreviewCameraOffset, ref pickerPreviewZoom);

        // v3.1: open NL Facial Animation's own face editor for this pawn, if that mod is present.
        if (FacialAnimCompat.CanEditFace(pawn))
        {
            inRect.yMin += 2f;
            // Opens on a higher window layer, so it sits in FRONT of this window and the main editor
            // (both can stay open behind it).
            if (Widgets.ButtonText(inRect.TakeTopPart(28f), "Customize face"))
                FacialAnimCompat.OpenFaceEditor(pawn);
        }

        // Restore Anthrosonae's "Change fur" button (their own patch skips our fork; see AnthrosonaeCompat).
        // Uses their translation key so the wording matches their mod exactly.
        if (AnthrosonaeCompat.HasFur(pawn))
        {
            inRect.yMin += 2f;
            if (Widgets.ButtonText(inRect.TakeTopPart(28f), "ColorPicker.ChangeFur".Translate()))
                AnthrosonaeCompat.OpenFurPicker(pawn);
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
        // Draw into the block reserved at the top of this method, never into whatever happens to be
        // left over: that is what guarantees these can't collide no matter how the window is resized.
        var bottomRect = bottomBlock.TakeBottomPart(60f + (ModsConfig.BiotechActive ? 50f : 0f));

        using (new TextBlock(GameFont.Tiny)) Widgets.Label(bottomBlock.TakeTopPart(Text.LineHeight), "Source".Translate().CapitalizeFirst());

        if (mainTab != MainTab.HAR && bottomBlock.height >= 30f
            && Widgets.ButtonText(bottomBlock.TakeTopPart(30).ContractedBy(3), sourceFilter?.Name ?? "PawnEditor.All".Translate().CapitalizeFirst()))
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

        if (ModsConfig.BiotechActive && mainTab != MainTab.Xenotype)
            Widgets.CheckboxLabeled(bottomRect.TakeBottomPart(50), "PawnEditor.IgnoreXenotype".Translate(), ref ignoreXenotype);
        Widgets.CheckboxLabeled(bottomRect.TakeBottomPart(30), "PawnEditor.ShowApparel".Translate(), ref PawnEditor.RenderClothes);
        Widgets.CheckboxLabeled(bottomRect.TakeBottomPart(30), "PawnEditor.ShowHeadgear".Translate(), ref PawnEditor.RenderHeadgear);
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
            case MainTab.FacialAnimation:
                return FacialAnimCompat.GetAllOptionDefs(pawn);
        }

        return Enumerable.Empty<Def>();
    }

    private enum MainTab
    {
        Shape,
        Hair,
        Tattoos,
        Xenotype,
        HAR,
        FacialAnimation
    }

    private enum ShapeTab
    {
        Body,
        Head
    }
}
