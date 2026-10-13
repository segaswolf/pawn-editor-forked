using NUnit.Framework;

namespace PawnEditor.Tests;

/// <summary>Pruebas de <see cref="Easing"/> y <see cref="AnimatedFloat"/>.</summary>
[TestFixture]
public class MotionTests
{
    private const float Tolerance = 0.0001f;

    // ── Easing ────────────────────────────────────────────────────────────────────────────────

    [TestCase(0f, 0f)]
    [TestCase(0.5f, 0.75f)]
    [TestCase(1f, 1f)]
    public void OutQuad_MatchesTheFormula(float t, float expected) =>
        Assert.That(Easing.OutQuad(t), Is.EqualTo(expected).Within(Tolerance));

    [TestCase(0.5f, 0.875f)]
    public void OutCubic_MatchesTheFormula(float t, float expected) =>
        Assert.That(Easing.OutCubic(t), Is.EqualTo(expected).Within(Tolerance));

    [TestCase(-1f, 0f)]
    [TestCase(2f, 1f)]
    public void Easing_OutOfRangeProgress_IsClamped(float t, float expected)
    {
        Assert.That(Easing.OutQuad(t), Is.EqualTo(expected).Within(Tolerance));
        Assert.That(Easing.OutCubic(t), Is.EqualTo(expected).Within(Tolerance));
    }

    // ── AnimatedFloat ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void NewValue_SitsStillAtItsStart()
    {
        var value = new AnimatedFloat(0.3f);

        Assert.That(value.Evaluate(100f), Is.EqualTo(0.3f).Within(Tolerance));
    }

    [Test]
    public void AnimateTo_GoesFromStartToTargetOverTheDuration()
    {
        var value = new AnimatedFloat(0f);
        value.AnimateTo(1f, now: 10f, seconds: 2f);

        Assert.That(value.Evaluate(10f), Is.EqualTo(0f).Within(Tolerance));
        Assert.That(value.Evaluate(11f), Is.EqualTo(0.75f).Within(Tolerance)); // mitad del tiempo, OutQuad = 0.75
        Assert.That(value.Evaluate(12f), Is.EqualTo(1f).Within(Tolerance));
        Assert.That(value.Evaluate(99f), Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void AnimateTo_ZeroDuration_JumpsStraightToTarget()
    {
        var value = new AnimatedFloat(0f);
        value.AnimateTo(0.6f, now: 5f, seconds: 0f);

        Assert.That(value.Evaluate(5f), Is.EqualTo(0.6f).Within(Tolerance));
    }

    [Test]
    public void Retarget_MidAnimation_StartsFromTheCurrentValueWithoutJumping()
    {
        var value = new AnimatedFloat(0f);
        value.AnimateTo(1f, now: 0f, seconds: 2f);
        var current = value.Evaluate(1f);

        value.AnimateTo(0f, now: 1f, seconds: 2f);

        Assert.That(value.Evaluate(1f), Is.EqualTo(current).Within(Tolerance));
        Assert.That(value.Target, Is.EqualTo(0f));
    }

    [Test]
    public void AnimateTo_SameTarget_DoesNotRestart()
    {
        var value = new AnimatedFloat(0f);
        value.AnimateTo(1f, now: 0f, seconds: 2f);

        value.AnimateTo(1f, now: 1f, seconds: 2f);

        Assert.That(value.Evaluate(2f), Is.EqualTo(1f).Within(Tolerance));
    }
}
