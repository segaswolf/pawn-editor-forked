using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Dialog for editing a pawn's visual appearance: body shape, hair, tattoos and cosmetic genes.
/// </summary>
/// <remarks>
/// <para>Split by responsibility across partial files:</para>
/// <list type="bullet">
/// <item><c>Dialog_AppearanceEditor.cs</c>: the window, its tabs and the bottom buttons.</item>
/// <item><c>_LeftPanel</c>: preview, compat buttons, sex, developmental stage and xenotype buttons.</item>
/// <item><c>_StylePickers</c>: what each style picker offers, and the Gradient Hair row.</item>
/// <item><c>_OptionGrid</c>: the icon grid every picker draws with, its caches and missing-icon tracking.</item>
/// <item><c>_Xenotype</c>: the cosmetic genes tab and applying xenotypes.</item>
/// <item><c>_FacialAnimation</c>: the Facial Animation tab.</item>
/// <item><c>_Randomizer</c>: the Randomize button and its repeat arrow, drawing from the pickers' lists.</item>
/// <item><c>_Filters</c>: which defs a pawn may pick, and what the Source menu lists.</item>
/// </list>
/// </remarks>
[StaticConstructorOnStartup]
[HotSwappable]
public partial class Dialog_AppearanceEditor : Window, IDragLockable, IMinWindowSize
{
    // Enforced by the resizer, so the panels never get laid out at an impossible size for one frame.
    public Vector2 MinWindowSize => new(900f, 620f);

    // Always locked: the window moves only by its corner resizer, never by dragging its body, because
    // dragging inside the preview rotates the pawn and the two drags would fight.
    // HISTORY: until v3.2.3 a button at the top of the left panel toggled this ("Unlock window (move)" /
    // "Lock window (resize)"). It existed for the old preview splitter, which was removed; the button was
    // then dropped as confusing. To bring it back: restore a bool field, return it here, and draw a
    // Widgets.ButtonText at the top of DoLeftSection that flips it (the resize patch reads DragLocked).
    public bool DragLocked => true;

    static Dialog_AppearanceEditor()
    {
        CosmeticGeneDiscovery.Initialize();
    }

    private readonly List<TabRecord> mainTabs = new(3);
    private readonly Pawn pawn;
    private readonly List<TabRecord> subTabs = new(2);
    private bool ignoreXenotype;

    private float lastColorHeight;
    private Action lastRandomization; // what the "repeat" arrow runs; null until the first randomize
    private float lastXenotypeHeight;
    private FacialAnimCompat.FacePart facialAnimationPart;
    private bool draggingPreview;
    private Vector3 pickerPreviewCameraOffset = new(0f, 0f, 0.12f);
    private Rot4 pickerPreviewRotation = Rot4.South;
    private float pickerPreviewZoom = 1.35f;
    private MainTab mainTab;
    private Vector2 scrollPos;
    private int selectedColorIndex;
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
        // No size clamp here: MinWindowSize is handed to the game's own resizer, which refuses to shrink
        // the window BEFORE the frame is laid out. Clamping windowRect in here was a frame too late.
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

            // The preview panel width is purely proportional to the window. Resize the whole editor from
            // its bottom-right corner and everything reflows: bigger preview and more option columns as
            // it widens. There is deliberately no splitter: it clashed with the drag-to-rotate preview.
            var contentWidth = inRect.width;
            var maxLeft = Mathf.Max(150f, contentWidth - 300f);
            leftPanelWidth = Mathf.Clamp(ProportionalLeftWidth(windowRect.width), 150f, maxLeft);
            var leftRect = inRect.TakeLeftPart(leftPanelWidth);
            inRect.xMin += 8f; // small gap between the preview panel and the options
            DoLeftSection(leftRect.ContractedBy(6, 0));

            mainTabs.Clear();
            mainTabs.Add(new("PawnEditor.Shape".Translate(), () => SelectMainTab(MainTab.Shape), mainTab == MainTab.Shape));
            mainTabs.Add(new("PawnEditor.Hair".Translate().CapitalizeFirst(), () => SelectMainTab(MainTab.Hair), mainTab == MainTab.Hair));
            if (ModsConfig.IdeologyActive)
                mainTabs.Add(new("Tattoos".Translate(), () => SelectMainTab(MainTab.Tattoos), mainTab == MainTab.Tattoos));
            if (ModsConfig.BiotechActive)
                // Labelled "Cosmetic genes", not "Xenotype": this tab edits individual cosmetic genes by
                // category. Changing the whole xenotype is a different action (the left-panel button).
                mainTabs.Add(new("PawnEditor.CosmeticGenes".Translate(), () => SelectMainTab(MainTab.Xenotype), mainTab == MainTab.Xenotype));
            if (HARCompat.Active)
                mainTabs.Add(new("HAR.RaceFeatures".Translate(), () => SelectMainTab(MainTab.HAR), mainTab == MainTab.HAR));
            if (FacialAnimCompat.Active && FacialAnimCompat.HasFaceControls(pawn))
                mainTabs.Add(new("PawnEditor.FA.Tab".Translate(), () => SelectMainTab(MainTab.FacialAnimation), mainTab == MainTab.FacialAnimation));

            if (mainTab == MainTab.FacialAnimation && !FacialAnimCompat.HasFaceControls(pawn))
                mainTab = MainTab.Shape;

            Widgets.DrawMenuSection(inRect);
            TabDrawer.DrawTabs(inRect, mainTabs, 400f);
            inRect.yMin += 40;

            switch (mainTab)
            {
                // Body, hair and tattoo tabs are all style pickers described as data; see
                // Dialog_AppearanceEditor_StylePickers.cs.
                case MainTab.Shape:
                case MainTab.Hair:
                case MainTab.Tattoos:
                    DrawStylePickers(inRect, StylePickersFor(mainTab));
                    break;
                // These three tabs depend on third-party mods, so they are the ones that break when a
                // mod changes its API. With the boundary, the failure stays inside its tab, names
                // itself in the log, and the rest of the editor stays usable.
                case MainTab.Xenotype:
                    Diagnostics.Run("Drawing the xenotype picker", pawn, () => DoXenotypePicker(inRect.ContractedBy(5)));
                    break;
                case MainTab.HAR:
                    Diagnostics.Run("Drawing the Humanoid Alien Races tabs", pawn, () =>
                    {
                        HARCompat.DoRaceTabs(inRect.ContractedBy(5));
                        if (Event.current.type is EventType.MouseDown or EventType.Used)
                            TabWorker_Bio_Humanlike.RecacheGraphics(pawn);
                    });
                    break;
                case MainTab.FacialAnimation:
                    Diagnostics.Run("Drawing the Facial Animation tab", pawn, () => DoFacialAnimationOptions(inRect.ContractedBy(5)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        Widgets.EndGroup();
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

        DrawRandomizeButtons(inRect);
    }

    // Internal so the in-game tests can ask which pickers a tab shows.
    internal enum MainTab
    {
        Shape,
        Hair,
        Tattoos,
        Xenotype,
        HAR,
        FacialAnimation
    }
}
