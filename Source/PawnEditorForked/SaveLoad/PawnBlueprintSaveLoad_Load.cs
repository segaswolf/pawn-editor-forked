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
    // Colony-load remap: when non-null, cross-pawn references (bonds, master, overseer) resolve
    // against this map (old saved ThingID -> the freshly created clone) FIRST, so a loaded colony
    // links clone<->clone instead of binding to the originals. Null for single-pawn loads.
    internal static Dictionary<string, Pawn> ColonyRemap;

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

        Pawn pawn = PawnEditorProfiler.Measure("Load.GeneratePawn", PawnEditorProfiler.Cadence.PerAction,
            () => PawnGenerator.GeneratePawn(request));

        // PawnGenerator may ignore fixedGender for some xenotypes — force it back
        if (pawn.gender != gender) pawn.gender = gender;

        // ── 3. Apply all blueprint sections ──
        PawnEditorProfiler.Measure("Load.ApplySections", PawnEditorProfiler.Cadence.PerAction, () =>
        {
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
            // Cross-pawn references (relations/bonds, master, overseer) are DEFERRED to a second pass
            // in colony load (ColonyRemap != null) so they resolve clone<->clone via the remap. For a
            // single-pawn load they run inline here as before. See ApplyRelationalSections.
            if (ColonyRemap == null) LoadRelations(pawn, root);
            LoadWorkPriorities(pawn, root);
            LoadInventory(pawn, root);
            LoadRoyalTitles(pawn, root);
            LoadRecords(pawn, root);
            LoadTraining(pawn, root);
            if (ColonyRemap == null) LoadMaster(pawn, root);
            LoadMechanitor(pawn, root);
            if (ColonyRemap == null) LoadMechControl(pawn, root);
            LoadMechUpgrades(pawn, root);
            FacialAnimCompat.LoadFacialData(pawn, root);
        });

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

        // Favorite color — find closest ColorDef by Euclidean RGB distance.
        // [BANDERITA] Loops over ALL ColorDefs. Suspected chunk of the unaccounted ~3.7s.
        PawnEditorProfiler.Measure("Load.FavoriteColor", PawnEditorProfiler.Cadence.PerAction, () =>
        {
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
        });

        // ── 4. Finalize ──
        try { pawn.Notify_DisabledWorkTypesChanged(); } catch { }

        // Refresh gene-driven visuals (body size, draw size, etc.). Note: we deliberately do
        // NOT call pawn.genes.Notify_GenesChanged(null) here — that RimWorld method dereferences
        // its argument immediately (addedOrRemovedGene.skinIsHairColor), so passing null throws a
        // NullReferenceException. That swallowed exception left the pawn's render half-built,
        // which then produced a flood of "null texture passed to GUI.DrawTexture" and a black
        // screen when the portrait drew. The graphics refresh below covers the visual rebuild
        // without needing that call.
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

        // [BANDERITA] These three are reflection into external mods (VAspirE / VSE / Life Lessons).
        // Reflection can be slow, and these were the biggest un-instrumented block — prime suspects
        // for the unaccounted ~3.7s. Measured separately to see which (if any) is the culprit.
        PawnEditorProfiler.Measure("Load.Aspirations", PawnEditorProfiler.Cadence.PerAction,
            () => LoadAspirations(pawn, root));   // VAspirE
        PawnEditorProfiler.Measure("Load.Expertise", PawnEditorProfiler.Cadence.PerAction,
            () => LoadExpertise(pawn, root));      // VSE
        PawnEditorProfiler.Measure("Load.Proficiencies", PawnEditorProfiler.Cadence.PerAction,
            () => LoadProficiencies(pawn, root));  // Life Lessons

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

        // NOTE: no forced GC here. We measured GC.Collect(0, Forced, blocking:true) at ~3.9s on
        // a large modlist (94% of the whole blueprint load) while freeing 0 KB — a blocking
        // collection is stop-the-world regardless of generation, and on a huge heap even gen-0
        // takes seconds. The actual load work (GeneratePawn + ApplySections + compat) is only
        // ~230ms. So we let the automatic GC reclaim the load's short-lived garbage on its own
        // schedule instead of forcing a multi-second freeze that reclaimed nothing.
        // (Confirmed by profiler banderita Load.CollectGen0 = 3920ms peak, 0 KB.)

        return pawn;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Colony load support (two-pass orchestrator lives in ColonyLoadUtility)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Build one clone from a parsed blueprint root. With ColonyRemap set, cross-pawn
    /// sections are deferred (second pass). First pass of the colony loader.</summary>
    internal static Pawn BuildColonyPawnFromRoot(XmlNode root) => BuildPawnFromBlueprint(root);

    /// <summary>Apply the deferred cross-pawn sections (relations/bonds, master, overseer). Second
    /// pass of the colony loader, once every clone exists and ColonyRemap is populated.</summary>
    internal static void ApplyRelationalSections(Pawn pawn, XmlNode root)
    {
        LoadRelations(pawn, root);
        LoadMaster(pawn, root);
        LoadMechControl(pawn, root);
    }

    /// <summary>Clear accumulated load warnings (the colony loader brackets its run with this).</summary>
    internal static void ClearLoadWarnings() => loadWarnings.Clear();

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

    /// <summary>
    /// Resolve a saved pawn reference to an actual pawn. Order:
    ///   1) Colony load: the remapped clone by saved ThingID (unique, unambiguous).
    ///   2) Existing world pawn by ThingID.
    ///   3) Existing world pawn by NameTriple, then full name — AMBIGUITY-SAFE: if more than one pawn
    ///      matches the name (e.g. three pawns named "Alexandra"), we do NOT guess; skip with a warning.
    ///      Names are not unique, only ThingIDs are.
    /// Returns null when nothing resolves — the caller must skip the link and load the pawn anyway.
    /// </summary>
    private static Pawn ResolvePawnRef(List<Pawn> allPawns, Pawn self,
        string id, string first, string last, string full)
    {
        if (ColonyRemap != null && !id.NullOrEmpty() && ColonyRemap.TryGetValue(id, out var clone))
            return clone;

        if (!id.NullOrEmpty())
        {
            var byId = allPawns.FirstOrDefault(p => p != self && p.ThingID == id);
            if (byId != null) return byId;
        }

        if (!first.NullOrEmpty() && !last.NullOrEmpty())
        {
            var byName = allPawns.Where(p => p != self && p.Name is NameTriple nt
                                          && nt.First == first && nt.Last == last).ToList();
            if (byName.Count == 1) return byName[0];
            if (byName.Count > 1) { Warn($"Reference '{first} {last}' is ambiguous ({byName.Count} matches) — skipped"); return null; }
        }

        if (!full.NullOrEmpty())
        {
            var byFull = allPawns.Where(p => p != self && p.Name?.ToStringFull == full).ToList();
            if (byFull.Count == 1) return byFull[0];
            if (byFull.Count > 1) { Warn($"Reference '{full}' is ambiguous — skipped"); return null; }
        }

        return null;
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
