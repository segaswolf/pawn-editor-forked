using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Blueprint save/load system for Pawn Editor Forked.
///
/// Split across three partial files — each with a single responsibility:
///
///   PawnBlueprintSaveLoad.cs          (this file)
///     Entry points: SaveBlueprint, LoadBlueprint, IsBlueprintFile.
///     Pawn discovery helper: GetAllReachablePawns / GetAllReachablePawnsPublic.
///     Warning system: Warn, FlushWarnings.
///
///   PawnBlueprintSaveLoad_Save.cs
///     Save side: all Write* methods + shared XML write helpers.
///
///   PawnBlueprintSaveLoad_Load.cs
///     Load side: BuildPawnFromBlueprint, all Load* methods,
///     shared parse/resolve helpers.
///
/// Format summary:
///   Save: Pawn → PawnBlueprint XML (defNames + MayRequire packageIds)
///   Load: PawnBlueprint XML → PawnGenerator fresh pawn → apply fields
///   Cross-modlist portability: missing mods/DLCs are gracefully skipped.
/// </summary>
public static partial class PawnBlueprintSaveLoad
{
    // ─────────────────────────────────────────────────────────────────────────
    //  SAVE — public entry point
    // ─────────────────────────────────────────────────────────────────────────

    public static void SaveBlueprint(Pawn pawn, string filePath)
    {
        var settings = new XmlWriterSettings
        {
            Indent      = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            Encoding    = System.Text.Encoding.UTF8
        };

        using var writer = XmlWriter.Create(filePath, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("PawnBlueprint");
        writer.WriteAttributeString("version", "1");

        // Meta block — the vanilla file picker reads gameVersion for display
        writer.WriteStartElement("meta");
        WriteSimple(writer, "gameVersion", VersionControl.CurrentVersionStringWithRev);
        writer.WriteEndElement();

        WriteIdentity(writer, pawn);
        WriteStory(writer, pawn);
        WriteAppearance(writer, pawn);
        WriteStyle(writer, pawn);
        WriteTraits(writer, pawn);
        WriteGenes(writer, pawn);
        WriteSkills(writer, pawn);
        WriteHediffs(writer, pawn);
        WriteAbilities(writer, pawn);
        WriteApparel(writer, pawn);
        WriteRelations(writer, pawn);
        WriteWorkPriorities(writer, pawn);
        WriteInventory(writer, pawn);
        WriteRoyalTitles(writer, pawn);
        WriteRecords(writer, pawn);
        FacialAnimCompat.WriteFacialData(writer, pawn);
        WriteModList(writer);

        writer.WriteEndElement(); // PawnBlueprint
        writer.WriteEndDocument();

        // Portrait PNG alongside the XML
        try { PawnEditor.SavePawnTex(pawn, Path.ChangeExtension(filePath, ".png"), Rot4.South); }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] SavePawnTex portrait: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  LOAD — public entry point
    // ─────────────────────────────────────────────────────────────────────────

    public static Pawn LoadBlueprint(string filePath)
    {
        loadWarnings.Clear();

        var doc = new XmlDocument();
        doc.Load(filePath);

        var root = doc.DocumentElement;
        if (root == null || root.Name != "PawnBlueprint")
        {
            Log.Warning("[Pawn Editor] Not a PawnBlueprint file, falling back to legacy loader.");
            return null; // Signal to caller: use legacy Scribe loader
        }

        try
        {
            var pawn = BuildPawnFromBlueprint(root);
            FlushWarnings(pawn);
            return pawn;
        }
        catch (Exception ex)
        {
            Log.Error($"[Pawn Editor] Blueprint load failed: {ex}");
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Pawn discovery — used by both Load and CopyDup hybrid pass
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Public wrapper so PawnDuplicationUtility can reuse the same pawn cache
    /// for the hybrid social memory pass without duplicating the discovery logic.
    /// </summary>
    internal static List<Pawn> GetAllReachablePawnsPublic() => GetAllReachablePawns();

    private static List<Pawn> GetAllReachablePawns()
    {
        var result = new List<Pawn>();
        try
        {
            // Pregame starting pawns
            if (Find.GameInitData?.startingAndOptionalPawns != null)
                result.AddRange(Find.GameInitData.startingAndOptionalPawns);

            // All map pawns
            if (Current.Game?.Maps != null)
                foreach (var map in Current.Game.Maps)
                    if (map?.mapPawns?.AllPawns != null)
                        result.AddRange(map.mapPawns.AllPawns);

            // World pawns
            if (Find.World?.worldPawns?.AllPawnsAliveOrDead != null)
                result.AddRange(Find.World.worldPawns.AllPawnsAliveOrDead);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] GetAllReachablePawns: {ex.Message}"); }
        return result.Distinct().ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Warning system
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly List<string> loadWarnings = new();

    private static void Warn(string msg) => loadWarnings.Add(msg);

    private static void FlushWarnings(Pawn pawn)
    {
        if (loadWarnings.Count == 0) return;
        var name = pawn?.Name?.ToStringFull ?? pawn?.LabelCap ?? "unknown";
        Log.Warning($"[Pawn Editor] Blueprint loaded '{name}' with {loadWarnings.Count} adjustment(s):");
        foreach (var w in loadWarnings)
            Log.Warning($"  → {w}");
        Messages.Message(
            $"Pawn Editor: '{name}' loaded with {loadWarnings.Count} adjustment(s) — check log for details.",
            MessageTypeDefOf.CautionInput, false);
        loadWarnings.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Format detection
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Quick check: does this file use the new Blueprint format?
    /// Reads only the root element name — does not parse the whole document.
    /// </summary>
    public static bool IsBlueprintFile(string filePath)
    {
        try
        {
            using var reader = XmlReader.Create(filePath);
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element)
                    return reader.Name == "PawnBlueprint";
        }
        catch { } // Format check — failure means it's not a blueprint, safe to swallow
        return false;
    }
}
