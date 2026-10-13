using UnityEngine;

namespace PawnEditor;

/// <summary>Easing curves: map linear progress (0-1) to eased progress (0-1).</summary>
public static class Easing
{
    /// <summary>Fast start, gentle stop. Same curve as WPF's <c>QuadraticEase</c> with <c>EaseOut</c>.</summary>
    public static float OutQuad(float t)
    {
        t = Mathf.Clamp01(t);
        var rest = 1f - t;
        return 1f - rest * rest;
    }

    /// <summary>Like <see cref="OutQuad"/>, with a stronger brake at the end.</summary>
    public static float OutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        var rest = 1f - t;
        return 1f - rest * rest * rest;
    }
}
