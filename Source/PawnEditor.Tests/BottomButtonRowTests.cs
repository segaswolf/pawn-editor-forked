using NUnit.Framework;
using UnityEngine;

namespace PawnEditor.Tests;

/// <summary>Pruebas de <see cref="BottomButtonRow"/>, la fila de Delete, Randomize, Save y Load.</summary>
[TestFixture]
public class BottomButtonRowTests
{
    private const float Tolerance = 0.001f;
    private static readonly Vector2 VanillaButton = new(150f, 38f);

    [Test]
    public void WideArea_SlotsHaveVanillaWidthAndAreCentered()
    {
        var area = new Rect(100f, 0f, 1000f, 50f);

        var row = BottomButtonRow.Within(area, VanillaButton);

        Assert.That(row.Delete.width, Is.EqualTo(150f).Within(Tolerance));
        var leftGap = row.Delete.x - area.x;
        var rightGap = area.xMax - row.Load.xMax;
        Assert.That(leftGap, Is.EqualTo(rightGap).Within(Tolerance), "el grupo va centrado");
    }

    [Test]
    public void SlotsAreVerticallyCenteredInTheArea()
    {
        var row = BottomButtonRow.Within(new Rect(0f, 10f, 800f, 50f), VanillaButton);

        Assert.That(row.Save.center.y, Is.EqualTo(35f).Within(Tolerance));
    }

    [Test]
    public void SlotsNeverOverlapAndKeepTheirOrder_WhateverTheWidth()
    {
        // Propiedad: el bug histórico era que Save y Load se montaban sobre Randomize al angostar.
        for (var width = 1f; width <= 1600f; width += 7f)
        {
            var row = BottomButtonRow.Within(new Rect(0f, 0f, width, 50f), VanillaButton);

            Assert.That(row.Randomize.x, Is.GreaterThanOrEqualTo(row.Delete.xMax), $"ancho {width}");
            Assert.That(row.Save.x, Is.GreaterThanOrEqualTo(row.Randomize.xMax), $"ancho {width}");
            Assert.That(row.Load.x, Is.GreaterThanOrEqualTo(row.Save.xMax), $"ancho {width}");
        }
    }

    [Test]
    public void GapAfterDelete_IsWiderThanTheOtherGaps()
    {
        var row = BottomButtonRow.Within(new Rect(0f, 0f, 1000f, 50f), VanillaButton);

        var deleteGap = row.Randomize.x - row.Delete.xMax;
        var saveGap = row.Save.x - row.Randomize.xMax;
        Assert.That(deleteGap, Is.EqualTo(saveGap * BottomButtonRow.DeleteGapFactor).Within(Tolerance),
            "Delete queda más separado para no pulsarlo por error");
    }

    [Test]
    public void NarrowArea_SlotsNeverShrinkBelowTheClickableMinimum()
    {
        var row = BottomButtonRow.Within(new Rect(0f, 0f, 100f, 50f), VanillaButton);

        Assert.That(row.Delete.width, Is.EqualTo(BottomButtonRow.MinButtonWidth).Within(Tolerance));
    }

    [Test]
    public void AreaWideEnoughForTheMinimum_GroupStaysInside()
    {
        for (var width = 330f; width <= 1600f; width += 7f)
        {
            var area = new Rect(0f, 0f, width, 50f);
            var row = BottomButtonRow.Within(area, VanillaButton);

            Assert.That(row.Delete.x, Is.GreaterThanOrEqualTo(area.x), $"ancho {width}");
            Assert.That(row.Load.xMax, Is.LessThanOrEqualTo(area.xMax + Tolerance), $"ancho {width}");
        }
    }
}
