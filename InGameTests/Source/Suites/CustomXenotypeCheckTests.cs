using System.Linq;
using Verse;
using CustomXenotypeCheck = global::PawnEditor.CustomXenotypeCheck;
using SavedXenotypeFile = global::PawnEditor.SavedXenotypeFile;

namespace PawnEditorInGameTests;

/// <summary>
/// Spotting the genes of a saved custom xenotype that no loaded mod provides.
/// </summary>
/// <remarks>
/// Built from a file whose shape matches a real save: one gene that exists in this game and one that
/// no mod defines, plus a mod list with one active mod and one that isn't installed.
/// </remarks>
public static class CustomXenotypeCheckTests
{
    [InGameTest]
    public static void Check_ReportsOnlyTheGenesThatAreNotLoaded()
    {
        if (!ModsConfig.BiotechActive) return; // genes are a Biotech feature

        var realGene = DefDatabase<GeneDef>.AllDefsListForReading.First().defName;
        var file = SavedXenotypeFile.Parse($@"<savedXenotype>
            <meta>
                <modIds><li>ludeon.rimworld</li><li>pawneditortests.notinstalled</li></modIds>
                <modNames><li>Core</li><li>A mod that is not installed</li></modNames>
            </meta>
            <xenotype><genes><li>{realGene}</li><li>PawnEditorTests_GeneFromNowhere</li></genes></xenotype>
        </savedXenotype>");

        var report = CustomXenotypeCheck.Check(file);

        Check.Equal(1, report.MissingGenes.Count, "missing gene count");
        Check.Equal("PawnEditorTests_GeneFromNowhere", report.MissingGenes[0], "missing gene");
        Check.That(report.ModsGoneSinceSaved.Contains("A mod that is not installed"),
            "the mod that is not installed was not listed as gone since saving");
        Check.That(!report.ModsGoneSinceSaved.Contains("Core"), "Core was listed as gone even though it is loaded");
    }

    [InGameTest]
    public static void Check_AllGenesPresent_ReportsNothing()
    {
        if (!ModsConfig.BiotechActive) return;

        var realGene = DefDatabase<GeneDef>.AllDefsListForReading.First().defName;
        var file = SavedXenotypeFile.Parse(
            $"<savedXenotype><meta><modIds><li>pawneditortests.notinstalled</li></modIds></meta><xenotype><genes><li>{realGene}</li></genes></xenotype></savedXenotype>");

        var report = CustomXenotypeCheck.Check(file);

        Check.That(!report.HasMissingGenes, "a xenotype with every gene loaded reported missing genes");
        Check.Equal(0, report.ModsGoneSinceSaved.Count, "mods listed with nothing missing");
    }
}
