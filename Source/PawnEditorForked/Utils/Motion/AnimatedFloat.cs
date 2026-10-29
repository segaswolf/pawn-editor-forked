using UnityEngine;

namespace PawnEditor;

/// <summary>A number that glides to its target over time, like WPF's <c>DoubleAnimation</c>.</summary>
/// <remarks>
/// <para>
/// The caller passes the clock (<c>now</c>) so this stays testable without the game. In the UI use
/// <c>Time.realtimeSinceStartup</c>: it keeps running while the game is paused.
/// </para>
/// </remarks>
public sealed class AnimatedFloat
{
    private float from;
    private float to;
    private float startTime;
    private float duration;

    public AnimatedFloat(float value)
    {
        from = value;
        to = value;
    }

    public float Target => to;

    public float Evaluate(float now)
    {
        if (duration <= 0f) return to;

        var progress = (now - startTime) / duration;
        return Mathf.LerpUnclamped(from, to, Easing.OutQuad(progress));
    }

    /// <summary>Starts moving to <paramref name="target"/>. A duration of 0 jumps straight there.</summary>
    public void AnimateTo(float target, float now, float seconds)
    {
        if (target == to) return;

        // Start from where it is right now, so retargeting mid-animation doesn't jump.
        from = Evaluate(now);
        to = target;
        startTime = now;
        duration = seconds;
    }
}
