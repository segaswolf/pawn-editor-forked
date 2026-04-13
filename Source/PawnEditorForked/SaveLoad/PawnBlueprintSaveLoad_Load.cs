using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Load side: Blueprint XML → Fresh Pawn.
/// Orchestrator: BuildPawnFromBlueprint + shared parse/resolve helpers.
///
/// Split across four files by responsibility:
///   _Load.cs              — Orchestrator + helpers (this file)
///   _Load_Identity.cs     — Name, Story, Appearance, Style, Traits, Genes, Skills
///   _Load_Health.cs       — Hediffs, Abilities, Apparel/Equipment
///   _Load_Social.cs       — Relations, WorkPriorities, Inventory, RoyalTitles, Records
/// </summary>
public static partial class PawnBlueprintSaveLoad
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Core build pipeline (called from LoadBlueprint in the main file)
    // ─────────────────────────────────────────────────────────────────────────

    private static Pawn BuildPawnFromBlueprint(XmlNode root)
    {
        // ── 1. Read identity fields needed for PawnGenerator ──
        var gender   = ParseEnum<Gender>(GetText(root, "gender"), Gender.Male);
        float bioAge  = ParseFloat(GetAttrOrText(root, "biologicalAge"),   25f);
        float chronAge= ParseFloat(GetAttrOrText(root, "chronologicalAge"), bioAge);
        if (chronAge < bioAge) chronAge = bioAge;

        var kindDef = ResolveDef<PawnKindDef>(root, "kindDef") ?? PawnKindDefOf.Colonist;

        XenotypeDef xenotype = null;
        if (ModsConfig.BiotechActive)
            xenotype = ResolveDef<XenotypeDef>(root, "xenotypeDef");

        // ── 2. Generate a fresh pawn base ──
        Ideo ideo = null;
        if (ModsConfig.IdeologyActive && Faction.OfPlayer?.ideos?.PrimaryIdeo != null)
            ideo = Faction.OfPlayer.ideos.PrimaryIdeo;

        var request = new PawnGenerationRequest(
            kind:                    kindDef,
            faction:                 Faction.OfPlayer,
            context:                 PawnGenerationContext.NonPlayer,
            forceGenerateNewPawn:    true,
            canGeneratePawnRelations:false,
            allowFood:               true,
            allowAddictions:         false,
            fixedBiologicalAge:      bioAge,
            fixedChronologicalAge:   chronAge,
            fixedGender:             gender,
            fixedIdeo:               ideo,
            forbidAnyTitle:          true,
            forceNoGear:             true
        );
        request.ForceNoIdeoGear       = true;
        request.CanGeneratePawnRelations = false;
        if (xenotype != null) request.ForcedXenotype = xenotype;

        Pawn pawn = PawnGenerator.GeneratePawn(request);

        // PawnGenerator may ignore fixedGender for some xenotypes — force it back
        if (pawn.gender != gender) pawn.gender = gender;

        // ── 3. Apply all blueprint sections ──
        LoadName(pawn, root);
        LoadStory(pawn, root);
        LoadTraits(pawn, root);
        LoadGenes(pawn, root);       // Genes first — they can force hair/body/skin changes
        LoadAppearance(pawn, root);  // Appearance after genes to override back to saved values
        LoadStyle(pawn, root);
        LoadSkills(pawn, root);
        LoadHediffs(pawn, root);
        LoadAbilities(pawn, root);
        LoadApparel(pawn, root);
        LoadRelations(pawn, root);
        LoadWorkPriorities(pawn, root);
        LoadInventory(pawn, root);
        LoadRoyalTitles(pawn, root);
        LoadRecords(pawn, root);
        FacialAnimCompat.LoadFacialData(pawn, root);

        // Biotech extras not covered by LoadGenes
        if (ModsConfig.BiotechActive && pawn.genes != null)
        {
            var xenoName = GetText(root, "xenotypeName");
            if (!xenoName.NullOrEmpty()) pawn.genes.xenotypeName = xenoName;

            var iconDef = ResolveDef<XenotypeIconDef>(root, "xenotypeIconDef");
            if (iconDef != null) pawn.genes.iconDef = iconDef;

            var growthPts = ParseFloat(GetText(root, "growthPoints"), -1f);
            if (growthPts >= 0f) pawn.ageTracker.growthPoints = growthPts;
        }

        // Favorite color — find closest ColorDef by Euclidean RGB distance
        var favColorNode = root.SelectSingleNode("favoriteColor");
        if (favColorNode != null && pawn.story != null)
        {
            var targetColor = ReadColor(favColorNode);
            ColorDef bestMatch = null;
            float bestDist = float.MaxValue;
            foreach (var cd in DefDatabase<ColorDef>.AllDefsListForReading)
            {
                float dist = ColorDistance(cd.color, targetColor);
                if (dist < bestDist) { bestDist = dist; bestMatch = cd; }
            }
            if (bestMatch != null) pawn.story.favoriteColor = bestMatch;
        }

        // ── 4. Finalize ──
        try { pawn.Notify_DisabledWorkTypesChanged(); } catch { }

        try
        {
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(pawn);
            GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] BuildPawnFromBlueprint graphics refresh: {ex.Message}"); }

        // v3d7: Re-apply headType AFTER graphics refresh.
        try
        {
            var savedHeadType = ResolveDef<HeadTypeDef>(root.SelectSingleNode("appearance"), "headType");
            if (savedHeadType != null && pawn.story != null)
            {
                pawn.story.headType = savedHeadType;
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                PortraitsCache.SetDirty(pawn);
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] BuildPawnFromBlueprint headType re-apply: {ex.Message}"); }

        // v3d7: Re-apply FA data AFTER finalize.
        FacialAnimCompat.LoadFacialData(pawn, root);

        // VAspirE: Restore aspirations from blueprint
        LoadAspirations(pawn, root);

        // VSE: Restore expertise from blueprint
        LoadExpertise(pawn, root);

        // Ideo certainty LAST — other steps trigger ideo recalculation
        if (ModsConfig.IdeologyActive && pawn.ideo != null)
        {
            var certNode = root.SelectSingleNode("ideoCertainty");
            if (certNode != null)
            {
                var certainty = ParseFloat(certNode.InnerText?.Trim(), 1f);
                pawn.ideo.SetIdeo(pawn.Ideo ?? ideo);
                pawn.ideo.certaintyInt = certainty;
            }
        }

        return pawn;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Shared parse/resolve helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsAvailable(XmlNode node)
    {
        var mayRequire = node?.Attributes?["MayRequire"]?.Value;
        if (mayRequire.NullOrEmpty()) return true;
        return ModLister.GetActiveModWithIdentifier(mayRequire, ignorePostfix: true) != null;
    }

    private static T ResolveDef<T>(XmlNode parent, string elementName) where T : Def
    {
        var node = parent?.SelectSingleNode(elementName);
        if (node == null) return null;
        var defName = node.Attributes?["defName"]?.Value ?? node.InnerText?.Trim();
        if (defName.NullOrEmpty()) return null;

        if (!IsAvailable(node))
        {
            var fallback = DefDatabase<T>.GetNamedSilentFail(defName);
            if (fallback != null) { Warn($"{typeof(T).Name} '{defName}' found via fallback (original mod not loaded)"); return fallback; }
            Warn($"{typeof(T).Name} '{defName}' skipped — mod '{node.Attributes?["MayRequire"]?.Value}' not loaded");
            return null;
        }

        var def = DefDatabase<T>.GetNamedSilentFail(defName);
        if (def == null) Warn($"{typeof(T).Name} '{defName}' not found");
        return def;
    }

    private static string GetText(XmlNode parent, string xpath)
        => parent?.SelectSingleNode(xpath)?.InnerText?.Trim();

    private static string GetAttrOrText(XmlNode parent, string name)
    {
        var node = parent?.SelectSingleNode(name);
        return node == null ? null : (node.Attributes?["value"]?.Value ?? node.InnerText?.Trim());
    }

    private static Color ReadColor(XmlNode node)
    {
        if (node == null) return Color.white;
        return new Color(
            ParseFloat(node.Attributes?["r"]?.Value, 1f),
            ParseFloat(node.Attributes?["g"]?.Value, 1f),
            ParseFloat(node.Attributes?["b"]?.Value, 1f),
            ParseFloat(node.Attributes?["a"]?.Value, 1f));
    }

    private static float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
        return dr * dr + dg * dg + db * db;
    }

    private static int ParseInt(string text, int fallback)
    {
        if (text.NullOrEmpty()) return fallback;
        return int.TryParse(text, out var v) ? v : fallback;
    }

    private static float ParseFloat(string text, float fallback)
    {
        if (text.NullOrEmpty()) return fallback;
        return float.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static bool ParseBool(string text, bool fallback)
    {
        if (text.NullOrEmpty()) return fallback;
        return bool.TryParse(text, out var v) ? v : fallback;
    }

    private static T ParseEnum<T>(string text, T fallback) where T : struct
    {
        if (text.NullOrEmpty()) return fallback;
        return Enum.TryParse<T>(text, true, out var v) ? v : fallback;
    }
}
