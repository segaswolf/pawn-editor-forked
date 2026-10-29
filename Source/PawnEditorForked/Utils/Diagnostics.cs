using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Error boundaries with context: runs a named piece of work and, if it throws, logs once who failed
/// and on what, instead of taking the whole window down.
/// </summary>
/// <remarks>
/// <para>
/// A useful bug report says where to look. When every section names itself on failure, the log arrives
/// with the culprit already pointed out. Evidence: when the face editor broke, the single line
/// <c>at FacialAnimation.NL_SelectPartWindow.GetSelectedPawn</c> was the whole diagnosis. This extends
/// that idea to our seams and adds what a stack trace lacks: which pawn, which mod, which section.
/// </para>
/// <para>
/// It does not rethrow, on purpose: the player sees one empty section instead of a dead window, and we
/// still get the log.
/// </para>
/// <para>
/// It logs once per section, subject and exception type. This runs in immediate-mode UI; without that
/// rule one failure would write sixty lines a second and bury everything else.
/// </para>
/// <para>
/// Cost on the happy path is one delegate allocation, so boundaries go on coarse seams (a section, a
/// tab, a compat) and never on each row of a grid.
/// </para>
/// </remarks>
public static class Diagnostics
{
    private static readonly HashSet<string> Reported = new();

    /// <summary>Runs the work; if it throws, logs it with context and carries on.</summary>
    /// <param name="section">What was being done, in words. Appears as-is in the log.</param>
    /// <param name="subject">A pawn, a def, or anything that gives context. May be null.</param>
    public static void Run(string section, object subject, Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            Report(section, subject, ex);
        }
    }

    /// <summary>Same as <see cref="Run(string, object, Action)"/>, with no subject to report.</summary>
    public static void Run(string section, Action work) => Run(section, null, work);

    /// <summary>
    /// Variant for work that returns a value. On failure it returns <paramref name="fallback"/>, so the
    /// caller still has something to draw with.
    /// </summary>
    public static T Run<T>(string section, object subject, Func<T> work, T fallback = default)
    {
        try
        {
            return work();
        }
        catch (Exception ex)
        {
            Report(section, subject, ex);
            return fallback;
        }
    }

    private static void Report(string section, object subject, Exception ex)
    {
        var description = Describe(subject);
        var key = $"{section}|{description}|{ex.GetType().FullName}";
        if (!Reported.Add(key)) return;

        var target = description.NullOrEmpty() ? "" : $" for {description}";
        Log.Error($"[Pawn Editor] {section} failed{target}. The rest of the window keeps working; "
                  + $"this section is skipped.\n{ex}");
    }

    /// <summary>
    /// Describes the subject in the most useful way for whoever reads the log: a pawn by name, a def by
    /// defName and the mod that added it, because that pair is what makes the failure reproducible.
    /// </summary>
    private static string Describe(object subject) => subject switch
    {
        null => "",
        Pawn pawn => pawn.Name?.ToStringShort ?? pawn.LabelCap,
        Def def => def.modContentPack?.Name is { } mod ? $"'{def.defName}' (from {mod})" : $"'{def.defName}'",
        string text => text,
        _ => subject.ToString()
    };
}
