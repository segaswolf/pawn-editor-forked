using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// The generic option grid every style picker draws with: icons, selection, colour palette, the
// per-def missing-icon tracking, and the single-slot caches that keep the lists from being rebuilt
// every frame.
public partial class Dialog_AppearanceEditor
{
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

    /// <summary>
    /// Drops the cached option list so the next draw rebuilds it. Needed whenever something the lists
    /// depend on changes without changing the cache key: the pawn's genes (a new xenotype, a cosmetic
    /// gene) or leaving and re-entering a tab.
    /// </summary>
    private void ForgetCachedOptions()
    {
        optionsCacheKey = null;
        optionsCacheVal = null;
    }

    /// <summary>
    /// Switches the main tab. Coming back to a tab always rebuilds its list: another tab (cosmetic
    /// genes, above all) may have changed what the pawn is allowed to pick in the meantime.
    /// </summary>
    private void SelectMainTab(MainTab tab)
    {
        if (mainTab == tab) return;
        mainTab = tab;
        ForgetCachedOptions();
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
}
