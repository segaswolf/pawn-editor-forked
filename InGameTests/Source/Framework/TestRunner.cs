using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RimWorld;
using Verse;

namespace PawnEditorInGameTests;

/// <summary>
/// Discovers every <see cref="InGameTestAttribute"/> method, runs each one isolated from the others,
/// writes a results file, and closes the game.
///
/// THE RESULTS FILE IS THE CONTRACT with run-ingame-tests.ps1. Its last line is always
/// "RESULT: X passed, Y failed, Z total". If that line is missing, the run did not finish (the game
/// crashed or hung) and the script treats it as a failure — a missing result is never a pass.
/// </summary>
public static class TestRunner
{
    public const string LaunchArgument = "pawneditortests";
    public const string ResultsFileName = "PawnEditorTestResults.txt";

    public static bool RequestedOnCommandLine => GenCommandLine.CommandLineArgPassed(LaunchArgument);

    /// <summary>Scratch space for files the tests write, such as blueprints. Inside the isolated data folder.</summary>
    public static string WorkFolder => Path.Combine(GenFilePaths.SaveDataFolderPath, "PawnEditorTests");

    private static string ResultsPath => Path.Combine(GenFilePaths.SaveDataFolderPath, ResultsFileName);

    /// <summary>
    /// Runs everything and quits, whatever happens. The finally block matters: a test run that hangs
    /// the game open or never writes its file would make the script wait until its timeout.
    /// </summary>
    public static void RunAllThenQuit()
    {
        var results = new List<TestResult>();
        try
        {
            Directory.CreateDirectory(WorkFolder);
            var tests = DiscoverTests().ToList();
            Log.Message($"[Pawn Editor Tests] Map ready, running {tests.Count} tests.");
            results.AddRange(tests.Select(Run));
            Log.Message("[Pawn Editor Tests] All tests finished, writing results.");
        }
        catch (Exception ex)
        {
            results.Add(TestResult.Fail("TestRunner itself", ex, TimeSpan.Zero));
        }
        finally
        {
            WriteResults(results);
            Root.Shutdown();
        }
    }

    private static IEnumerable<MethodInfo> DiscoverTests() =>
        typeof(TestRunner).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.GetCustomAttribute<InGameTestAttribute>() != null)
            .OrderBy(method => method.DeclaringType.Name)
            .ThenBy(method => method.Name);

    /// <summary>One test, fully isolated: whatever it throws becomes its own failure line and nothing more.</summary>
    private static TestResult Run(MethodInfo test)
    {
        var name = $"{test.DeclaringType.Name}.{test.Name}";

        // Written BEFORE the test runs, on purpose. A native crash (graphics driver, out of memory)
        // kills the process without running any finally block, so the results file never appears.
        // This line is then the only record of which test the game died in.
        Log.Message($"[Pawn Editor Tests] > {name}");

        var clock = Stopwatch.StartNew();
        try
        {
            test.Invoke(null, null);
            return TestResult.Pass(name, clock.Elapsed);
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException != null)
        {
            return TestResult.Fail(name, wrapped.InnerException, clock.Elapsed);
        }
        catch (Exception ex)
        {
            return TestResult.Fail(name, ex, clock.Elapsed);
        }
    }

    private static void WriteResults(List<TestResult> results)
    {
        var passed = results.Count(result => result.Passed);
        var report = new StringBuilder()
            .AppendLine($"Pawn Editor in-game tests - {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"RimWorld {VersionControl.CurrentVersionStringWithRev}, {ModsConfig.ActiveModsInLoadOrder.Count()} active mods")
            .AppendLine();

        foreach (var result in results)
        {
            report.AppendLine($"{(result.Passed ? "PASS" : "FAIL")}  {result.Name}  ({result.Duration.TotalMilliseconds:0} ms)");
            if (!result.Passed) report.AppendLine($"      {result.Detail}");
        }

        report.AppendLine().AppendLine($"RESULT: {passed} passed, {results.Count - passed} failed, {results.Count} total");

        try
        {
            File.WriteAllText(ResultsPath, report.ToString());
        }
        catch (Exception ex)
        {
            // Last resort: the log still gets it, and the missing file makes the script fail loudly.
            Log.Error($"[Pawn Editor Tests] Could not write {ResultsPath}: {ex}\n{report}");
        }
    }
}
