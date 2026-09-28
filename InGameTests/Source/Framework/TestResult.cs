using System;

namespace PawnEditorInGameTests;

/// <summary>The outcome of one test: what ran, whether it held, and if not, why.</summary>
public readonly struct TestResult
{
    private TestResult(string name, bool passed, string detail, TimeSpan duration)
    {
        Name = name;
        Passed = passed;
        Detail = detail;
        Duration = duration;
    }

    public string Name { get; }
    public bool Passed { get; }
    public string Detail { get; }
    public TimeSpan Duration { get; }

    public static TestResult Pass(string name, TimeSpan duration) => new(name, true, null, duration);

    /// <summary>
    /// A failed expectation reads as its message. Anything else is an unexpected crash inside the code
    /// under test, and gets its type and stack so it can be traced.
    /// </summary>
    public static TestResult Fail(string name, Exception exception, TimeSpan duration) =>
        new(name, false, exception is CheckFailure ? exception.Message : $"CRASH {exception}", duration);
}
