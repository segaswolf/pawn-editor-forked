using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditorInGameTests;

/// <summary>
/// DIAGNOSTIC, not a check on Pawn Editor. Backstory Constructor's "+" trait menu (Forced traits and
/// Disallowed traits share one method) throws a NullReferenceException with some mod lists, and a single
/// bad trait is enough to kill the whole menu. This replays, trait by trait, exactly what that menu does
/// and names every trait that would break it, so the culprit mod can be reported to its author.
///
/// Pawn Editor's own trait picker is not affected: it reads modContentPack null-safely.
/// Expected to FAIL while a culprit is loaded; that failure line is the diagnosis.
/// </summary>
public static class ExternalTraitMenuDiagnostics
{
    private const string BackstoryConstructorId = "DimonSever000.BackstoryConstructor.Specific";

    [InGameTest]
    public static void BackstoryConstructorTraitMenu_EveryTraitResolves()
    {
        Check.Requires(ModsConfig.IsActive(BackstoryConstructorId), "Backstory Constructor not loaded");

        var broken = new List<string>();
        foreach (var traitDef in DefDatabase<TraitDef>.AllDefsListForReading)
        {
            if (traitDef.degreeDatas == null)
            {
                broken.Add(Describe(traitDef, null, "degreeDatas is null"));
                continue;
            }

            foreach (var degreeData in traitDef.degreeDatas)
            {
                var problem = ProblemFor(traitDef, degreeData);
                if (problem != null) broken.Add(Describe(traitDef, degreeData, problem));
            }
        }

        Check.That(broken.Count == 0,
            $"{broken.Count} trait entries would break Backstory Constructor's trait menu:\n        - "
            + string.Join("\n        - ", broken));
    }

    /// <summary>
    /// The same string round trip and the same three lookups as
    /// Window_BackstoryConstructor.DrawBackstoryTraits: TraitDef.Named, degreeDatas.Find(...).LabelCap
    /// and modContentPack.Name. Returns what would throw, or null if the entry is safe.
    /// </summary>
    private static string ProblemFor(TraitDef traitDef, TraitDegreeData degreeData)
    {
        if (degreeData == null) return "null degree entry";

        var entry = $"{traitDef.defName} ~ ({degreeData.label}) [{degreeData.degree}]";
        var defName = entry.Split('~').First().Trim();
        int.TryParse(entry.Split('[').Last().Split(']').First(), out var degree);

        var resolved = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
        if (resolved == null) return $"defName \"{defName}\" does not resolve";

        var match = resolved.degreeDatas?.Find(d => d.degree == degree);
        if (match == null) return $"degree {degree} not found after parsing \"{entry}\"";

        try
        {
            _ = match.LabelCap;
        }
        catch (Exception ex)
        {
            return $"LabelCap threw {ex.GetType().Name}";
        }

        if (resolved.modContentPack == null) return "no modContentPack (probably created by code, not XML)";
        return null;
    }

    private static string Describe(TraitDef def, TraitDegreeData degree, string problem) =>
        $"{def.defName} [degree {degree?.degree.ToString() ?? "-"}] \"{degree?.label ?? def.label}\""
        + $" (class {def.GetType().FullName}): {problem}";
}
