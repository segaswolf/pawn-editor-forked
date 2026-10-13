using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>Draws the race between a disease's severity and the pawn's immunity as two thin bars.</summary>
public static class ImmunityRaceBar
{
    private const float BarsWidth = 120f;
    private const float BarHeight = 5f;
    private const float BarGap = 3f;
    private const float LabelGap = 8f;
    private const float GlideSeconds = 0.35f;

    private static readonly Color TrackColor = new(0.12f, 0.12f, 0.12f);
    private static readonly Color SeverityLowColor = new(0.85f, 0.75f, 0.30f);
    private static readonly Color SeverityHighColor = new(0.80f, 0.20f, 0.20f);
    private static readonly Color ImmunityColor = new(0.35f, 0.80f, 0.45f);

    // One state per hediff. Weak keys: a removed hediff takes its state with it.
    private static readonly ConditionalWeakTable<Hediff, ShownValues> shown = new();

    public static void Draw(Rect rect, Hediff hediff, HediffComp_Immunizable immunizable)
    {
        var severity = SeverityFraction(hediff);
        var immunity = immunizable.Immunity;

        var labelRect = rect;
        if (Layout.TryTakeLeft(ref labelRect, BarsWidth, out var barsRect))
        {
            var now = Time.realtimeSinceStartup;
            var values = ShownFor(hediff, severity, immunity);
            values.GlideTo(severity, immunity, now);

            DrawBars(barsRect, values.Severity.Evaluate(now), values.Immunity.Evaluate(now));
            labelRect.xMin += LabelGap;
        }

        DrawLabel(labelRect, severity, immunity);
    }

    private static ShownValues ShownFor(Hediff hediff, float severity, float immunity)
    {
        if (!shown.TryGetValue(hediff, out var values))
        {
            values = new ShownValues(severity, immunity);
            shown.Add(hediff, values);
        }

        return values;
    }

    private static void DrawBars(Rect rect, float severity, float immunity)
    {
        var top = rect.center.y - (BarHeight * 2f + BarGap) / 2f;
        var severityBar = new Rect(rect.x, top, rect.width, BarHeight);
        var immunityBar = new Rect(rect.x, top + BarHeight + BarGap, rect.width, BarHeight);

        FillBar(severityBar, severity, Color.Lerp(SeverityLowColor, SeverityHighColor, severity));
        FillBar(immunityBar, immunity, ImmunityColor);
    }

    private static void FillBar(Rect rect, float fill, Color color)
    {
        Widgets.DrawBoxSolid(rect, TrackColor);
        if (fill > 0f)
            Widgets.DrawBoxSolid(rect.LeftPart(fill), color);
    }

    private static void DrawLabel(Rect rect, float severity, float immunity)
    {
        var text = $"{"PawnEditor.Severity".Translate()} {severity.ToStringPercent()} · {"Immunity".Translate()} {immunity.ToStringPercent()}";
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(rect, text.Colorize(ColoredText.SubtleGrayColor));
    }

    private sealed class ShownValues
    {
        public readonly AnimatedFloat Severity;
        public readonly AnimatedFloat Immunity;

        public ShownValues(float severity, float immunity)
        {
            Severity = new AnimatedFloat(severity);
            Immunity = new AnimatedFloat(immunity);
        }

        public void GlideTo(float severity, float immunity, float now)
        {
            var seconds = (PawnEditorMod.Settings?.Animations ?? true) ? GlideSeconds : 0f;
            Severity.AnimateTo(severity, now, seconds);
            Immunity.AnimateTo(immunity, now, seconds);
        }
    }

    /// <summary>Severity as a 0-1 fraction of the point where the hediff kills.</summary>
    private static float SeverityFraction(Hediff hediff)
    {
        var lethal = hediff.def.lethalSeverity;
        // No lethal point: assume severity is already on a 0-1 scale.
        var scale = lethal > 0f ? lethal : 1f;
        return Mathf.Clamp01(hediff.Severity / scale);
    }
}
