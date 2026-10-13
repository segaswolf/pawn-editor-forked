using System.Linq;
using System.Xml;
using NUnit.Framework;

namespace PawnEditor.Tests;

/// <summary>
/// Reading saved custom xenotype files (<c>.xtp</c>).
/// </summary>
/// <remarks>
/// The sample mirrors the structure of a real file saved by RimWorld 1.6 (a wolf xenotype whose tail
/// and ear genes came from Bill Doors' Wolf Girl Gene), trimmed to a few entries.
/// </remarks>
[TestFixture]
public class SavedXenotypeFileTests
{
    private const string Sample = @"<?xml version=""1.0"" encoding=""utf-8""?>
<savedXenotype>
	<meta>
		<gameVersion>1.6.4850 rev646</gameVersion>
		<modIds>
			<li>ludeon.rimworld</li>
			<li>3hstltd.wolf.gene</li>
		</modIds>
		<modSteamIds>
			<li>0</li>
			<li>2878104853</li>
		</modSteamIds>
		<modNames>
			<li>Core</li>
			<li>Bill Doors' Wolf Girl Gene</li>
		</modNames>
	</meta>
	<xenotype>
		<name>Veldrak</name>
		<inheritable>True</inheritable>
		<genes>
			<li>Skin_SheerWhite</li>
			<li>BDWG_Tail_Wolf</li>
			<li>BDWG_Ears_Wolf</li>
		</genes>
		<iconDef>VRESaurids_Saurid</iconDef>
	</xenotype>
</savedXenotype>";

    [Test]
    public void Parse_ReadsGenesInFileOrder()
    {
        var file = SavedXenotypeFile.Parse(Sample);

        Assert.That(file.GeneDefNames, Is.EqualTo(new[] { "Skin_SheerWhite", "BDWG_Tail_Wolf", "BDWG_Ears_Wolf" }));
    }

    [Test]
    public void Parse_PairsModIdsWithTheirNames()
    {
        var file = SavedXenotypeFile.Parse(Sample);

        Assert.That(file.ModsWhenSaved.Select(mod => mod.PackageId), Is.EqualTo(new[] { "ludeon.rimworld", "3hstltd.wolf.gene" }));
        Assert.That(file.ModsWhenSaved.Select(mod => mod.Name), Is.EqualTo(new[] { "Core", "Bill Doors' Wolf Girl Gene" }));
    }

    [Test]
    public void Parse_ModWithoutAName_FallsBackToItsId()
    {
        var xml = "<savedXenotype><meta><modIds><li>a.mod</li><li>b.mod</li></modIds><modNames><li>Mod A</li></modNames></meta></savedXenotype>";

        var file = SavedXenotypeFile.Parse(xml);

        Assert.That(file.ModsWhenSaved.Select(mod => mod.Name), Is.EqualTo(new[] { "Mod A", "b.mod" }));
    }

    [Test]
    public void Parse_MissingSections_ReadAsEmpty()
    {
        var file = SavedXenotypeFile.Parse("<savedXenotype><xenotype><name>Bare</name></xenotype></savedXenotype>");

        Assert.That(file.GeneDefNames, Is.Empty);
        Assert.That(file.ModsWhenSaved, Is.Empty);
    }

    [Test]
    public void Parse_BlankEntries_AreSkipped()
    {
        var file = SavedXenotypeFile.Parse("<savedXenotype><xenotype><genes><li> </li><li>GeneA</li><li></li></genes></xenotype></savedXenotype>");

        Assert.That(file.GeneDefNames, Is.EqualTo(new[] { "GeneA" }));
    }

    [Test]
    public void Parse_MalformedXml_Throws()
    {
        Assert.Throws<XmlException>(() => SavedXenotypeFile.Parse("<savedXenotype><genes>"));
    }
}
