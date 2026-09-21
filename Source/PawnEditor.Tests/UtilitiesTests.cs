using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace PawnEditor.Tests;

/// <summary>
/// Pruebas de <see cref="Utilities"/>, los helpers de propósito general que usa todo el mod.
///
/// Son código que se llama desde muchos sitios, así que un error aquí se manifiesta lejos de su
/// causa. Eso los vuelve caros de diagnosticar y baratos de proteger.
/// </summary>
[TestFixture]
public class UtilitiesTests
{
    // ── Get: índice con envolvente ────────────────────────────────────────────────────────────

    [Test]
    public void Get_IndexInsideTheList_ReturnsThatItem()
    {
        var list = new List<string> { "a", "b", "c" };

        Assert.That(list.Get(0), Is.EqualTo("a"));
        Assert.That(list.Get(2), Is.EqualTo("c"));
    }

    [Test]
    public void Get_IndexPastTheEnd_WrapsAround()
    {
        var list = new List<string> { "a", "b", "c" };

        Assert.That(list.Get(3), Is.EqualTo("a"));
        Assert.That(list.Get(4), Is.EqualTo("b"));
        Assert.That(list.Get(7), Is.EqualTo("b"), "también con varias vueltas");
    }

    [Test]
    public void Get_NegativeIndex_CountsFromTheEnd()
    {
        var list = new List<string> { "a", "b", "c" };

        Assert.That(list.Get(-1), Is.EqualTo("c"));
        Assert.That(list.Get(-3), Is.EqualTo("a"));
        Assert.That(list.Get(-4), Is.EqualTo("c"), "también con varias vueltas");
    }

    [Test, Timeout(2000)]
    public void Get_EmptyList_ThrowsInsteadOfHangingTheGame()
    {
        // La implementación anterior restaba list.Count en un bucle. Con la lista vacía eso resta
        // cero para siempre: no lanzaba nada, congelaba el juego. El Timeout está a propósito, para
        // que una regresión aparezca como prueba fallida y no como suite colgada.
        var empty = new List<string>();

        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Get(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Get(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Get(5));
    }

    // ── Set: asignación que extiende ──────────────────────────────────────────────────────────

    [Test]
    public void Set_IndexInsideTheList_ReplacesInPlace()
    {
        var list = new List<string> { "a", "b", "c" };

        list.Set(1, "B");

        Assert.That(list, Is.EqualTo(new[] { "a", "B", "c" }));
    }

    [Test]
    public void Set_IndexPastTheEnd_GrowsTheListWithDefaults()
    {
        var list = new List<string> { "a" };

        list.Set(3, "d");

        Assert.That(list.Count, Is.EqualTo(4));
        Assert.That(list[1], Is.Null);
        Assert.That(list[2], Is.Null);
        Assert.That(list[3], Is.EqualTo("d"));
    }

    // ── Deconstruct ───────────────────────────────────────────────────────────────────────────

    [Test]
    public void Deconstruct_ShorterArrayThanRequested_FillsTheRestWithDefaults()
    {
        var (first, second, third) = new[] { "a" };

        Assert.That(first, Is.EqualTo("a"));
        Assert.That(second, Is.Null, "un array corto no debe reventar, debe rellenar");
        Assert.That(third, Is.Null);
    }

    [Test]
    public void Deconstruct_EmptyArray_GivesAllDefaults()
    {
        var (first, second) = new string[0];

        Assert.That(first, Is.Null);
        Assert.That(second, Is.Null);
    }

    // ── Except y NotNullAndAny ────────────────────────────────────────────────────────────────

    [Test]
    public void Except_RemovesEverythingInTheSet()
    {
        var source = new[] { 1, 2, 3, 4 };
        var without = new HashSet<int> { 2, 4 };

        Assert.That(source.Except(without).ToArray(), Is.EqualTo(new[] { 1, 3 }));
    }

    [Test]
    public void NotNullAndAny_NullSource_IsFalseInsteadOfThrowing()
    {
        IEnumerable<int> source = null;

        Assert.That(source.NotNullAndAny(x => x > 0), Is.False);
    }

    [Test]
    public void NotNullAndAny_EmptyOrNoMatch_IsFalse()
    {
        Assert.That(new int[0].NotNullAndAny(x => x > 0), Is.False);
        Assert.That(new[] { 1, 2 }.NotNullAndAny(x => x > 5), Is.False);
    }

    [Test]
    public void NotNullAndAny_HasAMatch_IsTrue()
    {
        Assert.That(new[] { 1, 9 }.NotNullAndAny(x => x > 5), Is.True);
    }

    // ── SelectValues ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void SelectValues_KeepsTheKeysAndProjectsTheValues()
    {
        var source = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        var result = source.SelectValues((key, value) => key + value);

        Assert.That(result.Keys, Is.EquivalentTo(new[] { "a", "b" }));
        Assert.That(result["a"], Is.EqualTo("a1"));
        Assert.That(result["b"], Is.EqualTo("b2"));
    }

    // ── StepValue ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public void StepValue_NormalStep_AddsAndRoundsToTwoDecimals()
    {
        Assert.That(Utilities.StepValue(0.5f, 0.1f), Is.EqualTo(0.6f).Within(0.0001f));
        Assert.That(Utilities.StepValue(0.333f, 0.1f), Is.EqualTo(0.43f).Within(0.0001f));
    }

    [Test]
    public void StepValue_PastTheBounds_IsClamped()
    {
        Assert.That(Utilities.StepValue(0.95f, 0.5f), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(Utilities.StepValue(0.05f, -0.5f), Is.EqualTo(0f).Within(0.0001f));
        Assert.That(Utilities.StepValue(5f, 1f, 0f, 10f), Is.EqualTo(6f).Within(0.0001f));
    }

    // ── ConvertCamelCase ──────────────────────────────────────────────────────────────────────

    [Test]
    public void ConvertCamelCase_SplitsOnCapitalsAndLowercasesTheRest()
    {
        Assert.That("CamelCase".ConvertCamelCase(), Is.EqualTo("Camel case"));
        Assert.That("SkinColorOverride".ConvertCamelCase(), Is.EqualTo("Skin color override"));
        Assert.That("lowercase".ConvertCamelCase(), Is.EqualTo("Lowercase"), "la primera letra siempre sube");
    }

    [Test]
    public void ConvertCamelCase_EmptyOrNull_IsReturnedUnchanged()
    {
        Assert.That(((string)null).ConvertCamelCase(), Is.Null);
        Assert.That("".ConvertCamelCase(), Is.EqualTo(""));
    }

    [Test]
    public void ConvertCamelCase_ConsecutiveCapitals_SplitEachOne()
    {
        // Comportamiento actual, no necesariamente el deseado: las siglas se desarman.
        // Se documenta aquí para que un cambio futuro sea una decisión y no un accidente.
        Assert.That("UIScale".ConvertCamelCase(), Is.EqualTo("U i scale"));
    }
}
