using NUnit.Framework;
using UnityEngine;

namespace PawnEditor.Tests;

/// <summary>
/// Pruebas de <see cref="Layout"/>, la capa que hace irrepresentable el rectángulo inválido.
///
/// Existe por dos bugs que llegaron a los usuarios: la fila de Betrayer dibujada encima de los
/// botones inferiores (y clicable ahí), y los botones de abajo solapándose al angostar la ventana.
/// Ambos eran aritmética de rectángulos hecha a mano.
///
/// Los nombres describen el comportamiento esperado, no el método llamado: una prueba que falla
/// debería decir qué se rompió, no dónde.
/// </summary>
[TestFixture]
public class LayoutTests
{
    private const float Tolerance = 0.001f;

    private static Rect Panel(float width = 300f, float height = 200f) => new(10f, 20f, width, height);

    // ── TryTakeTop ────────────────────────────────────────────────────────────────────────────

    [Test]
    public void TryTakeTop_RowThatFits_ReturnsItAndShrinksTheRemainder()
    {
        var rect = Panel();

        var taken = Layout.TryTakeTop(ref rect, 30f, out var row);

        Assert.That(taken, Is.True);
        Assert.That(row.height, Is.EqualTo(30f).Within(Tolerance));
        Assert.That(row.width, Is.EqualTo(300f).Within(Tolerance));
        Assert.That(row.y, Is.EqualTo(20f).Within(Tolerance));
        Assert.That(rect.height, Is.EqualTo(170f).Within(Tolerance), "el resto debe encoger exactamente lo tomado");
        Assert.That(rect.y, Is.EqualTo(50f).Within(Tolerance));
    }

    [Test]
    public void TryTakeTop_RowTallerThanWhatIsLeft_RefusesAndChangesNothing()
    {
        // El bug original: TakeTopPart devolvía la fila igual, colgando fuera del panel.
        var rect = new Rect(0f, 0f, 100f, 4f);

        var taken = Layout.TryTakeTop(ref rect, 30f, out var row);

        Assert.That(taken, Is.False);
        Assert.That(row, Is.EqualTo(default(Rect)));
        Assert.That(rect.height, Is.EqualTo(4f).Within(Tolerance), "un intento fallido no debe consumir espacio");
    }

    [Test]
    public void TryTakeTop_RowExactlyAsTallAsWhatIsLeft_IsAllowed()
    {
        var rect = new Rect(0f, 0f, 100f, 30f);

        var taken = Layout.TryTakeTop(ref rect, 30f, out _);

        Assert.That(taken, Is.True, "caber justo es caber");
        Assert.That(rect.height, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void TryTakeTop_NonPositiveHeight_Refuses()
    {
        var rect = Panel();

        Assert.That(Layout.TryTakeTop(ref rect, 0f, out _), Is.False);
        Assert.That(Layout.TryTakeTop(ref rect, -5f, out _), Is.False);
        Assert.That(rect.height, Is.EqualTo(200f).Within(Tolerance));
    }

    // ── TryTakeLeft ───────────────────────────────────────────────────────────────────────────

    [Test]
    public void TryTakeLeft_ColumnThatFits_ReturnsItAndShrinksTheRemainder()
    {
        var rect = Panel();

        var taken = Layout.TryTakeLeft(ref rect, 80f, out var column);

        Assert.That(taken, Is.True);
        Assert.That(column.width, Is.EqualTo(80f).Within(Tolerance));
        Assert.That(column.height, Is.EqualTo(200f).Within(Tolerance));
        Assert.That(rect.width, Is.EqualTo(220f).Within(Tolerance));
        Assert.That(rect.x, Is.EqualTo(90f).Within(Tolerance));
    }

    [Test]
    public void TryTakeLeft_ColumnWiderThanWhatIsLeft_RefusesAndChangesNothing()
    {
        var rect = new Rect(0f, 0f, 20f, 100f);

        var taken = Layout.TryTakeLeft(ref rect, 80f, out _);

        Assert.That(taken, Is.False);
        Assert.That(rect.width, Is.EqualTo(20f).Within(Tolerance));
    }

    // ── Columns ───────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Columns_EqualWeights_SplitsEvenlyAndFillsTheRect()
    {
        var rect = Panel(320f);

        var columns = Layout.Columns(rect, new[] { 1f, 1f, 1f }, spacing: 10f);

        Assert.That(columns.Length, Is.EqualTo(3));
        foreach (var column in columns)
            Assert.That(column.width, Is.EqualTo(100f).Within(Tolerance));

        Assert.That(columns[2].xMax, Is.EqualTo(rect.xMax).Within(Tolerance), "la última columna debe terminar en el borde");
    }

    [Test]
    public void Columns_UnequalWeights_SplitsInProportion()
    {
        var rect = Panel(300f);

        var columns = Layout.Columns(rect, new[] { 1f, 3f });

        Assert.That(columns[0].width, Is.EqualTo(75f).Within(Tolerance));
        Assert.That(columns[1].width, Is.EqualTo(225f).Within(Tolerance));
    }

    [Test]
    public void Columns_NeverSpillOutsideTheParent_WhateverTheInputs()
    {
        // La propiedad que de verdad importa: pase lo que pase, nada se dibuja fuera del panel.
        foreach (var width in new[] { 1f, 37f, 120f, 500f, 1600f })
        {
            var rect = new Rect(5f, 5f, width, 100f);
            var columns = Layout.Columns(rect, new[] { 1f, 1f, 2f }, new[] { 120f, 120f, 120f }, 15f);

            foreach (var column in columns)
            {
                Assert.That(column.xMin, Is.GreaterThanOrEqualTo(rect.xMin - Tolerance), $"ancho {width}");
                Assert.That(column.width, Is.GreaterThanOrEqualTo(0f), $"ancho {width}");
            }

            Assert.That(columns[columns.Length - 1].xMax, Is.LessThanOrEqualTo(rect.xMax + Tolerance),
                $"con ancho {width} la última columna se sale del panel");
        }
    }

    [Test]
    public void Columns_MinimumsThatFit_AreHonoured()
    {
        var rect = Panel(400f);

        // Con pesos 1:9 la primera recibiría 40, por debajo de su mínimo de 150.
        var columns = Layout.Columns(rect, new[] { 1f, 9f }, new[] { 150f, 50f });

        Assert.That(columns[0].width, Is.GreaterThanOrEqualTo(150f - Tolerance));
        Assert.That(columns[1].width, Is.GreaterThanOrEqualTo(50f - Tolerance));
        Assert.That(columns[0].width + columns[1].width, Is.EqualTo(400f).Within(Tolerance), "no debe sobrar ni faltar espacio");
    }

    [Test]
    public void Columns_MinimumsThatDoNotFit_ShrinkTogetherInsteadOfOverflowing()
    {
        var rect = Panel(100f);

        var columns = Layout.Columns(rect, new[] { 1f, 1f }, new[] { 200f, 200f });

        var total = columns[0].width + columns[1].width;
        Assert.That(total, Is.EqualTo(100f).Within(Tolerance), "un layout apretado se recupera; uno desbordado no");
        Assert.That(columns[0].width, Is.EqualTo(columns[1].width).Within(Tolerance), "deben encoger por igual");
    }

    [Test]
    public void Columns_AllWeightsZero_FallsBackToAnEvenSplit()
    {
        var rect = Panel(200f);

        var columns = Layout.Columns(rect, new[] { 0f, 0f });

        Assert.That(columns[0].width, Is.EqualTo(100f).Within(Tolerance));
        Assert.That(columns[1].width, Is.EqualTo(100f).Within(Tolerance));
    }

    [Test]
    public void Columns_RejectsInputThatCannotMeanAnything()
    {
        var rect = Panel();

        Assert.Throws<System.ArgumentException>(() => Layout.Columns(rect, null));
        Assert.Throws<System.ArgumentException>(() => Layout.Columns(rect, new float[0]));
        Assert.Throws<System.ArgumentException>(() => Layout.Columns(rect, new[] { 1f, 1f }, new[] { 10f }));
    }

    // ── ScrollViewWidth ───────────────────────────────────────────────────────────────────────

    [Test]
    public void ScrollViewWidth_ContentOverflows_ReservesRoomForTheBar()
    {
        var outRect = new Rect(0f, 0f, 200f, 100f);

        Assert.That(Layout.ScrollViewWidth(outRect, 400f),
            Is.EqualTo(200f - Layout.ScrollBarWidth).Within(Tolerance));
    }

    [Test]
    public void ScrollViewWidth_ContentFits_UsesTheFullWidth()
    {
        var outRect = new Rect(0f, 0f, 200f, 100f);

        Assert.That(Layout.ScrollViewWidth(outRect, 50f), Is.EqualTo(200f).Within(Tolerance),
            "descontar la barra cuando no hay barra deja un hueco muerto");
    }

    // ── Proportional ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Proportional_WithinBounds_ReturnsTheFraction()
    {
        Assert.That(Layout.Proportional(1000f, 0.32f, 100f, 500f), Is.EqualTo(320f).Within(Tolerance));
    }

    [Test]
    public void Proportional_BelowTheFloor_ReturnsTheFloor()
    {
        Assert.That(Layout.Proportional(200f, 0.1f, 100f, 500f), Is.EqualTo(100f).Within(Tolerance));
    }

    [Test]
    public void Proportional_AboveTheCeiling_ReturnsTheCeiling()
    {
        Assert.That(Layout.Proportional(5000f, 0.5f, 100f, 500f), Is.EqualTo(500f).Within(Tolerance));
    }

    [Test]
    public void Proportional_SwappedBounds_AreTreatedAsAnHonestMistake()
    {
        Assert.That(Layout.Proportional(1000f, 0.3f, 500f, 100f), Is.EqualTo(300f).Within(Tolerance));
    }
}
