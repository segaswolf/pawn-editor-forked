using System;
using System.Collections.Generic;

namespace PawnEditorInGameTests;

/// <summary>A test expectation that did not hold. Reported as a failure, not as a crash.</summary>
public sealed class CheckFailure : Exception
{
    public CheckFailure(string message) : base(message)
    {
    }
}

/// <summary>
/// Minimal assertions for in-game tests. The game has no test framework loaded, so these throw
/// <see cref="CheckFailure"/> and the runner turns that into a readable failure line.
///
/// Messages are written as what went wrong in the game's terms ("the duplicate lost the forced
/// flag"), not as "expected true but was false": the results file is read by a person.
/// </summary>
public static class Check
{
    public static void That(bool condition, string failureMessage)
    {
        if (!condition) throw new CheckFailure(failureMessage);
    }

    public static void NotNull(object value, string what) =>
        That(value != null, $"{what} was null");

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new CheckFailure($"{what}: expected {Show(expected)}, got {Show(actual)}");
    }

    public static void DoesNotThrow(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            throw new CheckFailure($"{what} threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Show(object value) => value == null ? "null" : value.ToString();
}
