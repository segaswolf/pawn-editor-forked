using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// The style pickers (body, head, hair, beard, body and face tattoos) described as data, plus the
// Gradient Hair row.
//
// Each picker used to be a copy-pasted block inside DoWindowContents: build sub-tabs, build a cached
// query, call DoIconOptions with ten arguments, recache graphics. Now a picker is a StylePicker<T>
// that says WHAT it offers and how it applies; drawing is shared. Adding a picker is adding one entry
// to BuildStylePickers, and a failure names the picker in the stack trace.
public partial class Dialog_AppearanceEditor
{
    /// <summary>HAR style restrictions apply to hair, beards and tattoos only when HAR enforces them.</summary>
    private static bool EnforceHarStyleRestrictions => HARCompat.Active && HARCompat.EnforceRestrictions;

    private Dictionary<MainTab, IStylePicker[]> stylePickers;

    // Selected sub-tab per main tab. It used to be one shared ShapeTab value whose meaning changed per
    // tab (in Hair, "Head" meant hair and "Body" meant beard), so switching tabs jumped to arbitrary
    // sub-tabs. An index per tab means each tab remembers its own.
    private readonly Dictionary<MainTab, int> selectedPicker = new();

    // ── Queries ───────────────────────────────────────────────────────────────────────────────────
    // `bySource` applies the Source filter at the exact point it always was. With it off, the same
    // query lists every candidate, which is what the Source menu needs to know which mods to offer.

    internal IEnumerable<BodyTypeDef> BodyTypeOptions() => BodyTypes(bySource: true);

    internal IEnumerable<HeadTypeDef> HeadTypeOptions() => HeadTypes(bySource: true);

    internal IEnumerable<HairDef> HairOptions() => Hairs(bySource: true);

    internal IEnumerable<BeardDef> BeardOptions() => Beards(bySource: true);

    internal IEnumerable<TattooDef> TattooOptions(TattooType type) => Tattoos(type, bySource: true);

    private IEnumerable<BodyTypeDef> BodyTypes(bool bySource)
    {
        IEnumerable<BodyTypeDef> q = DefDatabase<BodyTypeDef>.AllDefs.Where(h => (!bySource || MatchesSource(h)) && IsAllowed(h, pawn))
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
    }

    private IEnumerable<HeadTypeDef> HeadTypes(bool bySource)
    {
        IEnumerable<HeadTypeDef> q = DefDatabase<HeadTypeDef>.AllDefs.Where(h => (!bySource || MatchesSource(h)) && IsAllowed(h, pawn));
        if (HARCompat.Active)
        {
            q = HARCompat.FilterHeadTypes(q, pawn);
            // HAR doesn't like head types not matching genders
            q = q.Where(type => type.gender == Gender.None || type.gender == pawn.gender);
        }
        return q;
    }

    private IEnumerable<HairDef> Hairs(bool bySource)
    {
        IEnumerable<HairDef> q = DefDatabase<HairDef>.AllDefs.Where(h => !bySource || MatchesSource(h));
        if (EnforceHarStyleRestrictions) q = q.Where(hair => HARCompat.AllowStyleItem(hair, pawn));
        return q;
    }

    private IEnumerable<BeardDef> Beards(bool bySource)
    {
        IEnumerable<BeardDef> q = DefDatabase<BeardDef>.AllDefs.Where(b => !bySource || MatchesSource(b));
        if (EnforceHarStyleRestrictions) q = q.Where(beard => HARCompat.AllowStyleItem(beard, pawn));
        return q;
    }

    private IEnumerable<TattooDef> Tattoos(TattooType type, bool bySource)
    {
        IEnumerable<TattooDef> q = DefDatabase<TattooDef>.AllDefs.Where(t => !bySource || MatchesSource(t));
        if (EnforceHarStyleRestrictions) q = q.Where(td => HARCompat.AllowStyleItem(td, pawn));
        return q.Where(td => td.tattooType == type);
    }

    // ── Picker table ──────────────────────────────────────────────────────────────────────────────

    /// <summary>The pickers shown as sub-tabs of a main tab, or null for tabs that draw themselves.</summary>
    internal IReadOnlyList<IStylePicker> StylePickersFor(MainTab tab)
    {
        stylePickers ??= BuildStylePickers();
        return stylePickers.TryGetValue(tab, out var pickers) ? pickers : null;
    }

    private Dictionary<MainTab, IStylePicker[]> BuildStylePickers()
    {
        var skin = new ColorSlot(() => pawn.story.SkinColor, color => pawn.story.skinColorOverride = color, ColorType.Misc,
            "colMisc", () => DefDatabase<ColorDef>.AllDefs.Select(static def => def.color));
        var hair = new ColorSlot(() => pawn.story.HairColor, color => pawn.story.HairColor = color, ColorType.Hair,
            "colHair", () => DefDatabase<ColorDef>.AllDefs.Where(static def => def.colorType == ColorType.Hair).Select(static def => def.color));

        return new Dictionary<MainTab, IStylePicker[]>
        {
            [MainTab.Shape] = new IStylePicker[]
            {
                new StylePicker<BodyTypeDef>(this, "PawnEditor.Body".Translate(),
                    () => ("body", sourceFilter, pawn.DevelopmentalStage, HARCompat.Active, ignoreXenotype), BodyTypes,
                    def => pawn.story.bodyType = def, def => TexPawnEditor.GetBodyTypeIcon(def), def => pawn.story.bodyType == def, skin),
                new StylePicker<HeadTypeDef>(this, "PawnEditor.Head".Translate().CapitalizeFirst(),
                    () => ("head", sourceFilter, pawn.gender, HARCompat.Active, ignoreXenotype), HeadTypes,
                    def => pawn.story.headType = def, def => def.GetGraphic(pawn, pawn.story.HairColor).MatSouth.mainTexture,
                    def => pawn.story.headType == def, skin)
            },
            [MainTab.Hair] = new IStylePicker[]
            {
                new StylePicker<HairDef>(this, "PawnEditor.Hair".Translate().CapitalizeFirst(),
                    () => ("hair", sourceFilter, EnforceHarStyleRestrictions), Hairs,
                    def => pawn.story.hairDef = def, def => def.Icon, def => pawn.story.hairDef == def, hair)
                {
                    // Gradient Hair (optional mod) puts its toggle and second colour above the grid.
                    BeforeGrid = DoGradientHairRow
                },
                new StylePicker<BeardDef>(this, "PawnEditor.Beard".Translate(),
                    () => ("beard", sourceFilter, EnforceHarStyleRestrictions), Beards,
                    def => pawn.style.beardDef = def, def => def.Icon, def => pawn.style.beardDef == def, hair)
            },
            [MainTab.Tattoos] = new IStylePicker[]
            {
                new StylePicker<TattooDef>(this, "PawnEditor.Body".Translate(),
                    () => ("tattooBody", sourceFilter, EnforceHarStyleRestrictions), bySource => Tattoos(TattooType.Body, bySource),
                    def => pawn.style.BodyTattoo = def, static def => def.Icon, def => pawn.style.BodyTattoo == def, null),
                new StylePicker<TattooDef>(this, "PawnEditor.Head".Translate().CapitalizeFirst(),
                    () => ("tattooFace", sourceFilter, EnforceHarStyleRestrictions), bySource => Tattoos(TattooType.Face, bySource),
                    def => pawn.style.FaceTattoo = def, static def => def.Icon, def => pawn.style.FaceTattoo == def, null)
            }
        };
    }

    /// <summary>Draws a main tab made of style pickers: its sub-tabs, then the selected picker.</summary>
    private void DrawStylePickers(Rect inRect, IReadOnlyList<IStylePicker> pickers)
    {
        var tab = mainTab;
        var selected = Mathf.Clamp(selectedPicker.TryGetValue(tab, out var index) ? index : 0, 0, pickers.Count - 1);

        subTabs.Clear();
        for (var i = 0; i < pickers.Count; i++)
        {
            var picker = i;
            subTabs.Add(new TabRecord(pickers[i].Label, () => selectedPicker[tab] = picker, picker == selected));
        }

        Widgets.DrawMenuSection(inRect);
        TabDrawer.DrawTabs(inRect, subTabs);

        // Boundary per picker: a mod def that breaks one grid names that picker in the log and leaves
        // the rest of the editor usable.
        var shown = pickers[selected];
        var gridRect = inRect.ContractedBy(5);
        Diagnostics.Run($"Drawing the {shown.Label} picker", pawn, () => shown.Draw(gridRect));
    }

    /// <summary>The picker currently shown in the selected main tab, or null when that tab has none.</summary>
    private IStylePicker SelectedStylePicker()
    {
        var pickers = StylePickersFor(mainTab);
        if (pickers == null || pickers.Count == 0) return null;
        return pickers[Mathf.Clamp(selectedPicker.TryGetValue(mainTab, out var index) ? index : 0, 0, pickers.Count - 1)];
    }

    // ── Picker types ──────────────────────────────────────────────────────────────────────────────

    /// <summary>A sub-tab of the appearance editor that offers one kind of style def.</summary>
    internal interface IStylePicker
    {
        string Label { get; }

        /// <summary>Everything this picker could offer before the Source filter; feeds the Source menu.</summary>
        IEnumerable<Def> Candidates();

        void Draw(Rect inRect);
    }

    /// <summary>The colour strip shown next to a picker's grid: which colour it edits and its palette.</summary>
    private sealed class ColorSlot
    {
        public readonly Func<Color> Current;
        public readonly Action<Color> Set;
        public readonly ColorType Type;
        public readonly string PaletteKey;
        public readonly Func<IEnumerable<Color>> Palette;

        public ColorSlot(Func<Color> current, Action<Color> set, ColorType type, string paletteKey, Func<IEnumerable<Color>> palette)
        {
            Current = current;
            Set = set;
            Type = type;
            PaletteKey = paletteKey;
            Palette = palette;
        }
    }

    /// <summary>Hook that may take space at the top of a picker before its grid is drawn.</summary>
    private delegate void GridHook(ref Rect inRect);

    /// <summary>One style picker as data: what it offers, how it applies, and how it looks.</summary>
    private sealed class StylePicker<T> : IStylePicker where T : Def
    {
        private readonly Dialog_AppearanceEditor editor;
        private readonly Func<object> cacheKey;
        private readonly Func<bool, IEnumerable<T>> query;
        private readonly Action<T> apply;
        private readonly Func<T, Texture> icon;
        private readonly Func<T, bool> isCurrent;
        private readonly ColorSlot color;

        public StylePicker(Dialog_AppearanceEditor editor, string label, Func<object> cacheKey, Func<bool, IEnumerable<T>> query,
            Action<T> apply, Func<T, Texture> icon, Func<T, bool> isCurrent, ColorSlot color)
        {
            this.editor = editor;
            Label = label;
            this.cacheKey = cacheKey;
            this.query = query;
            this.apply = apply;
            this.icon = icon;
            this.isCurrent = isCurrent;
            this.color = color;
        }

        public string Label { get; }

        public GridHook BeforeGrid { get; set; }

        public IEnumerable<Def> Candidates() => query(false).Cast<Def>();

        public void Draw(Rect inRect)
        {
            BeforeGrid?.Invoke(ref inRect);

            var options = editor.CachedOptions(cacheKey(), () => query(true));
            editor.DoIconOptions(inRect, options, OnChosen, icon, isCurrent,
                color == null ? 0 : 1,
                color == null ? Array.Empty<Color>() : new[] { color.Current() },
                color == null ? null : (Action<Color, int>)SetColor,
                color?.Type ?? ColorType.Misc,
                color == null ? null : editor.CachedColors(color.PaletteKey, color.Palette));
        }

        private void OnChosen(T def)
        {
            apply(def);
            TabWorker_Bio_Humanlike.RecacheGraphics(editor.pawn);
        }

        private void SetColor(Color value, int index)
        {
            color.Set(value);
            TabWorker_Bio_Humanlike.RecacheGraphics(editor.pawn);
        }
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
}
