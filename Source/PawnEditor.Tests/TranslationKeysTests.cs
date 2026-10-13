using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace PawnEditor.Tests;

/// <summary>
/// Every <c>PawnEditor.*</c> translation key used in code exists in the English keyed files.
/// </summary>
/// <remarks>
/// A missing key does not throw: RimWorld shows the key itself, or a pseudo-translated version of it
/// in dev mode ("Iŋĥèŕìţàƀłè"). Players reported that twice. Vanilla keys can't be checked without the
/// game, but ours can, by reading the source and the XML.
/// </remarks>
[TestFixture]
public class TranslationKeysTests
{
    // "PawnEditor.Some.Key".Translate(  — the literal key right before a Translate call.
    private static readonly Regex KeyInCode = new("\"(PawnEditor\\.[A-Za-z0-9_.]+)\"\\s*\\.Translate\\(", RegexOptions.Compiled);

    [Test]
    public void EveryPawnEditorKeyUsedInCode_IsDefinedInEnglish()
    {
        var repo = FindRepoRoot();
        var used = Directory.EnumerateFiles(Path.Combine(repo, "Source", "PawnEditorForked"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => KeyInCode.Matches(File.ReadAllText(file)).Cast<Match>().Select(match => match.Groups[1].Value))
            .ToHashSet();

        var defined = Directory.EnumerateFiles(Path.Combine(repo, "Languages", "English", "Keyed"), "*.xml")
            .SelectMany(file => XDocument.Load(file).Root!.Elements().Select(element => element.Name.LocalName))
            .ToHashSet();

        var missing = used.Where(key => !defined.Contains(key)).OrderBy(key => key).ToList();

        Assert.That(used, Is.Not.Empty, "found no translation keys at all; the source path is probably wrong");
        Assert.That(missing, Is.Empty, "keys used in code but missing from Languages/English/Keyed: " + string.Join(", ", missing));
    }

    /// <summary>The folder that holds both Source and Languages, found by walking up from the test binaries.</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !(Directory.Exists(Path.Combine(directory.FullName, "Languages"))
                                      && Directory.Exists(Path.Combine(directory.FullName, "Source"))))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "could not find the repository root above " + TestContext.CurrentContext.TestDirectory);
        return directory!.FullName;
    }
}
