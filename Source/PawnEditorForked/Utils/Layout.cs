using System;
using UnityEngine;

namespace PawnEditor;

/// <summary>
/// Rect arithmetic that cannot produce an invalid rect.
///
/// WHY THIS EXISTS
/// RimWorld's UI is immediate mode: every frame recomputes every rectangle by hand, and its helpers
/// happily hand back a rectangle that does not fit. <c>Rect.TakeTopPart(30f)</c> returns a 30px row
/// even when the rect has 4px left — the row simply hangs outside the panel, and so does everything
/// drawn into it, including click regions and tooltips.
///
/// That is not hypothetical. It shipped twice:
///   - The Trauma and Integrity "Betrayer" checkbox was drawn past the bottom of its panel, on top of
///     the bottom buttons. Because a checkbox reacts to a click anywhere inside it, clicking a button
///     down there could silently flag a pawn as a betrayer.
///   - The bottom button row mixed centred and right-anchored placement, so at small widths Randomize
///     and Save overlapped.
///
/// The fix for that whole class of bug is not to be more careful. It is to make the invalid result
/// unrepresentable: these methods either give you a rect that fits, or tell you there is no room.
///
/// DESIGN
/// This type does layout only — it never draws. Computing the layout as data means it can be checked
/// before anything is painted, which is exactly what immediate mode otherwise makes impossible.
///
/// No caching or buffer reuse here yet, on purpose: the allocations are small arrays on UI frames, and
/// this project's rule is to measure before optimizing. If a profile ever shows it matters, add an
/// overload that fills a caller-supplied array.
/// </summary>
public static class Layout
{
    /// <summary>Width RimWorld reserves for a vertical scrollbar.</summary>
    public const float ScrollBarWidth = 16f;

    /// <summary>
    /// Takes a row of <paramref name="height"/> off the top of <paramref name="rect"/>.
    ///
    /// Returns false and leaves <paramref name="rect"/> untouched when there is not enough room, so a
    /// caller that respects the result can never draw outside its panel. Prefer this over
    /// <c>TakeTopPart</c> for anything interactive.
    /// </summary>
    public static bool TryTakeTop(ref Rect rect, float height, out Rect row)
    {
        row = default;
        if (height <= 0f || rect.height < height) return false;

        row = new Rect(rect.x, rect.y, rect.width, height);
        rect.yMin += height;
        return true;
    }

    /// <summary>
    /// Takes a column of <paramref name="width"/> off the left of <paramref name="rect"/>.
    /// Same contract as <see cref="TryTakeTop"/>: false means there was no room, and nothing changed.
    /// </summary>
    public static bool TryTakeLeft(ref Rect rect, float width, out Rect column)
    {
        column = default;
        if (width <= 0f || rect.width < width) return false;

        column = new Rect(rect.x, rect.y, width, rect.height);
        rect.xMin += width;
        return true;
    }

    /// <summary>
    /// Splits <paramref name="rect"/> into columns sized by <paramref name="weights"/>, each at least
    /// its entry in <paramref name="minWidths"/>, separated by <paramref name="spacing"/>.
    ///
    /// The returned columns ALWAYS fit inside <paramref name="rect"/>. When the minimums cannot all be
    /// honoured, they are scaled down together rather than allowed to overflow — a cramped layout is
    /// recoverable, one drawn over the neighbouring panel is not.
    /// </summary>
    /// <param name="weights">Relative share of the free space. Must be non-empty and non-negative.</param>
    /// <param name="minWidths">Floor per column. Pass null for no floors.</param>
    public static Rect[] Columns(Rect rect, float[] weights, float[] minWidths = null, float spacing = 0f)
    {
        if (weights == null || weights.Length == 0) throw new ArgumentException("At least one weight is required.", nameof(weights));
        if (minWidths != null && minWidths.Length != weights.Length)
            throw new ArgumentException("minWidths must have one entry per weight.", nameof(minWidths));

        var count = weights.Length;
        var gaps = count - 1;

        // The spacing has to shrink as well when the rect is narrower than the gaps alone would need.
        // Shrinking only the columns is not enough: the gaps keep pushing each column further right
        // and the last one lands outside the parent — the exact failure this class exists to prevent.
        // Caught by LayoutTests.Columns_NeverSpillOutsideTheParent_WhateverTheInputs.
        var usableWidth = Mathf.Max(0f, rect.width);
        var requestedSpacing = Mathf.Max(0f, spacing);
        var effectiveSpacing = gaps > 0 ? Mathf.Min(requestedSpacing, usableWidth / gaps) : 0f;

        var available = Mathf.Max(0f, usableWidth - effectiveSpacing * gaps);
        var widths = DistributeWidths(available, weights, minWidths);

        var columns = new Rect[count];
        var x = rect.x;
        for (var i = 0; i < count; i++)
        {
            columns[i] = new Rect(x, rect.y, widths[i], rect.height);
            x += widths[i] + effectiveSpacing;
        }

        return columns;
    }

    /// <summary>
    /// A single proportional size with a floor and a ceiling — the "grows with the window, but never
    /// past sensible bounds" pattern used by the appearance editor and the Bio tab's group column.
    /// </summary>
    public static float Proportional(float available, float fraction, float min, float max)
    {
        if (max < min) (min, max) = (max, min);
        return Mathf.Clamp(available * fraction, min, Mathf.Min(max, Mathf.Max(min, available)));
    }

    /// <summary>
    /// Usable content width inside a scroll view: the scrollbar is only subtracted when the content
    /// actually overflows. Subtracting it unconditionally leaves a dead gap; never subtracting it lets
    /// the bar sit on top of the content.
    /// </summary>
    public static float ScrollViewWidth(Rect outRect, float contentHeight) =>
        contentHeight > outRect.height ? Mathf.Max(0f, outRect.width - ScrollBarWidth) : outRect.width;

    /// <summary>
    /// Splits <paramref name="available"/> across the weights, honouring the floors when they fit and
    /// shrinking them proportionally when they do not. Extracted so <see cref="Columns"/> reads as
    /// "work out the widths, then place them" instead of doing both at once.
    /// </summary>
    private static float[] DistributeWidths(float available, float[] weights, float[] minWidths)
    {
        var count = weights.Length;
        var widths = new float[count];

        var totalMin = 0f;
        if (minWidths != null)
            for (var i = 0; i < count; i++) totalMin += Mathf.Max(0f, minWidths[i]);

        // Not even the floors fit: shrink them all by the same factor so the row still fits exactly.
        if (totalMin > available)
        {
            var scale = totalMin > 0f ? available / totalMin : 0f;
            for (var i = 0; i < count; i++) widths[i] = Mathf.Max(0f, minWidths[i]) * scale;
            return widths;
        }

        var totalWeight = 0f;
        for (var i = 0; i < count; i++) totalWeight += Mathf.Max(0f, weights[i]);

        // No usable weights: fall back to an even split so the caller still gets something sane.
        if (totalWeight <= 0f)
        {
            var even = available / count;
            for (var i = 0; i < count; i++) widths[i] = even;
            return widths;
        }

        // Hand out the weighted share, then lift any column that landed under its floor. The space for
        // those lifts comes from the columns that are above their floor, which is why the floors were
        // checked against the total first.
        var surplus = available;
        for (var i = 0; i < count; i++)
        {
            widths[i] = available * (Mathf.Max(0f, weights[i]) / totalWeight);
            surplus -= widths[i];
        }

        if (minWidths == null) return widths;

        var debt = 0f;
        for (var i = 0; i < count; i++)
        {
            var floor = Mathf.Max(0f, minWidths[i]);
            if (widths[i] >= floor) continue;
            debt += floor - widths[i];
            widths[i] = floor;
        }

        if (debt <= 0f) return widths;

        RepayFromColumnsAboveFloor(widths, minWidths, debt);
        return widths;
    }

    /// <summary>
    /// Reclaims <paramref name="debt"/> pixels from the columns that still have room above their floor,
    /// in proportion to that headroom, so no column is pushed under its minimum.
    /// </summary>
    private static void RepayFromColumnsAboveFloor(float[] widths, float[] minWidths, float debt)
    {
        var headroom = 0f;
        for (var i = 0; i < widths.Length; i++)
            headroom += Mathf.Max(0f, widths[i] - Mathf.Max(0f, minWidths[i]));

        if (headroom <= 0f) return;

        var factor = Mathf.Min(1f, debt / headroom);
        for (var i = 0; i < widths.Length; i++)
        {
            var slack = Mathf.Max(0f, widths[i] - Mathf.Max(0f, minWidths[i]));
            widths[i] -= slack * factor;
        }
    }
}
