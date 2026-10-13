using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Which genes of a saved custom xenotype are missing from the current mod list, and which mods that
/// were active when it was saved are gone now.
/// </summary>
/// <remarks>
/// Results are cached per file and its last-write time, so opening the xenotype menu repeatedly does
/// not re-read every file (each one lists the whole mod list and weighs around 100 KB).
/// </remarks>
public static class CustomXenotypeCheck
{
    private static readonly Dictionary<string, (DateTime written, Report report)> Cache = new();

    /// <summary>What is missing for one custom xenotype. Empty when everything is present or unreadable.</summary>
    public sealed class Report
    {
        public static readonly Report Empty = new(new List<string>(), new List<string>());

        public Report(List<string> missingGenes, List<string> modsGoneSinceSaved)
        {
            MissingGenes = missingGenes;
            ModsGoneSinceSaved = modsGoneSinceSaved;
        }

        public IReadOnlyList<string> MissingGenes { get; }

        /// <summary>Mods active at save time that aren't now: where the missing genes probably came from.</summary>
        public IReadOnlyList<string> ModsGoneSinceSaved { get; }

        public bool HasMissingGenes => MissingGenes.Count > 0;
    }

    /// <summary>Checks the saved file of a custom xenotype by name.</summary>
    public static Report For(string xenotypeName)
    {
        if (string.IsNullOrEmpty(xenotypeName)) return Report.Empty;
        var path = GenFilePaths.AbsFilePathForXenotype(xenotypeName);

        return Diagnostics.Run("Checking custom xenotype file", xenotypeName, () =>
        {
            if (!File.Exists(path)) return Report.Empty;
            var written = File.GetLastWriteTimeUtc(path);
            if (Cache.TryGetValue(path, out var cached) && cached.written == written) return cached.report;

            var report = Check(SavedXenotypeFile.Parse(File.ReadAllText(path)));
            Cache[path] = (written, report);
            return report;
        }, Report.Empty);
    }

    /// <summary>Compares a parsed file against the defs and mods loaded right now.</summary>
    public static Report Check(SavedXenotypeFile file)
    {
        var missing = file.GeneDefNames.Where(defName => DefDatabase<GeneDef>.GetNamedSilentFail(defName) == null).ToList();
        if (missing.Count == 0) return Report.Empty;

        var gone = file.ModsWhenSaved
            .Where(mod => ModLister.GetActiveModWithIdentifier(mod.PackageId, ignorePostfix: true) == null)
            .Select(mod => mod.Name)
            .ToList();
        return new Report(missing, gone);
    }

    /// <summary>Tooltip text: the missing genes, plus the mods gone since saving when there are few enough to list.</summary>
    public static string Describe(Report report)
    {
        if (!report.HasMissingGenes) return null;

        var text = "PawnEditor.MissingGenesTip".Translate(string.Join(", ", report.MissingGenes)).ToString();
        if (report.ModsGoneSinceSaved.Count is > 0 and <= MaxModsListed)
            text += "\n\n" + "PawnEditor.MissingGenesMods".Translate(string.Join("\n", report.ModsGoneSinceSaved));
        else if (report.ModsGoneSinceSaved.Count > MaxModsListed)
            text += "\n\n" + "PawnEditor.MissingGenesManyMods".Translate(report.ModsGoneSinceSaved.Count);
        return text;
    }

    // Past this, the list stops pointing anywhere useful (a whole modlist swap), so only the count shows.
    private const int MaxModsListed = 8;
}
