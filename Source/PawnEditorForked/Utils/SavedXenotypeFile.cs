using System.Collections.Generic;
using System.Xml;

namespace PawnEditor;

/// <summary>
/// What a saved custom xenotype file (<c>.xtp</c>) says, read straight from its XML.
/// </summary>
/// <remarks>
/// <para>
/// When the game loads a custom xenotype whose genes come from a mod that is no longer active, it
/// drops them ("Removed null genes") and only says so in the log. Reading the file ourselves lets the
/// editor tell the player which genes are gone before they apply it.
/// </para>
/// <para>
/// Pure on purpose: no game types, so it is unit-tested without RimWorld. Format verified against a
/// real file saved by RimWorld 1.6: <c>savedXenotype/xenotype/genes/li</c> holds gene defNames, and
/// <c>savedXenotype/meta</c> lists the mods active at save time as parallel <c>modIds</c> /
/// <c>modNames</c> lists. The file does not record which mod each gene came from.
/// </para>
/// </remarks>
public sealed class SavedXenotypeFile
{
    private SavedXenotypeFile(List<string> geneDefNames, List<SavedMod> modsWhenSaved)
    {
        GeneDefNames = geneDefNames;
        ModsWhenSaved = modsWhenSaved;
    }

    /// <summary>The genes the xenotype was saved with, by defName, in file order.</summary>
    public IReadOnlyList<string> GeneDefNames { get; }

    /// <summary>The mods that were active when the file was saved.</summary>
    public IReadOnlyList<SavedMod> ModsWhenSaved { get; }

    /// <summary>Reads the file's XML. Missing sections read as empty; malformed XML throws XmlException.</summary>
    public static SavedXenotypeFile Parse(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);

        var genes = TextOf(document.SelectNodes("/savedXenotype/xenotype/genes/li"));
        var ids = TextOf(document.SelectNodes("/savedXenotype/meta/modIds/li"));
        var names = TextOf(document.SelectNodes("/savedXenotype/meta/modNames/li"));

        var mods = new List<SavedMod>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
            mods.Add(new SavedMod(ids[i], i < names.Count && names[i].Length > 0 ? names[i] : ids[i]));

        return new SavedXenotypeFile(genes, mods);
    }

    private static List<string> TextOf(XmlNodeList nodes)
    {
        var result = new List<string>();
        if (nodes == null) return result;
        foreach (XmlNode node in nodes)
        {
            var text = node.InnerText?.Trim();
            if (!string.IsNullOrEmpty(text)) result.Add(text);
        }
        return result;
    }

    /// <summary>A mod listed in the file's metadata: its packageId and its display name.</summary>
    public readonly struct SavedMod
    {
        public SavedMod(string packageId, string name)
        {
            PackageId = packageId;
            Name = name;
        }

        public string PackageId { get; }
        public string Name { get; }
    }
}
