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
/// Each Load* method deserialises one logical section of the blueprint XML
/// and applies it to the freshly generated pawn.
/// Shared parse/resolve helpers live at the bottom of this file.
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
        // Genes and Facial Animations can override head visuals during SetAllGraphicsDirty,
        // so we force it back to the blueprint value.
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
        // FA Genetic Heads overrides head/eyes/brows during SetAllGraphicsDirty
        // based on genes, so we call LoadFacialData a second time to fix it.
        FacialAnimCompat.LoadFacialData(pawn, root);

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
    //  Load: individual sections
    // ─────────────────────────────────────────────────────────────────────────

    // ── Load: Name ──

    private static void LoadName(Pawn pawn, XmlNode root)
    {
        try
        {
            var nameNode = root.SelectSingleNode("name");
            if (nameNode == null) return;
            var first = nameNode.Attributes?["first"]?.Value;
            var nick  = nameNode.Attributes?["nick"]?.Value;
            var last  = nameNode.Attributes?["last"]?.Value;
            if (first != null || last != null)
                pawn.Name = new NameTriple(first ?? "", nick ?? "", last ?? "");
            else if (!nameNode.InnerText.NullOrEmpty())
                pawn.Name = NameTriple.FromString(nameNode.InnerText);
        }
        catch (Exception ex) { Warn($"Name: {ex.Message}"); }
    }

    // ── Load: Story ──

    private static void LoadStory(Pawn pawn, XmlNode root)
    {
        if (pawn.story == null) return;
        try
        {
            var childhood = ResolveDef<BackstoryDef>(root, "childhood");
            if (childhood != null) pawn.story.Childhood = childhood;
            var adulthood = ResolveDef<BackstoryDef>(root, "adulthood");
            if (adulthood != null) pawn.story.Adulthood = adulthood;
        }
        catch (Exception ex) { Warn($"Story: {ex.Message}"); }
    }

    // ── Load: Appearance ──

    private static void LoadAppearance(Pawn pawn, XmlNode root)
    {
        if (pawn.story == null) return;
        var app = root.SelectSingleNode("appearance");
        if (app == null) return;
        try
        {
            var bodyType = ResolveDef<BodyTypeDef>(app, "bodyType");
            if (bodyType != null) pawn.story.bodyType = bodyType;
            var headType = ResolveDef<HeadTypeDef>(app, "headType");
            if (headType != null) pawn.story.headType = headType;
            var hairDef = ResolveDef<HairDef>(app, "hairDef");
            if (hairDef != null) pawn.story.hairDef = hairDef;
            var furDef = ResolveDef<FurDef>(app, "furDef");
            if (furDef != null) pawn.story.furDef = furDef;

            var hairColorNode = app.SelectSingleNode("hairColor");
            if (hairColorNode != null) pawn.story.HairColor = ReadColor(hairColorNode);
            var skinBaseNode = app.SelectSingleNode("skinColorBase");
            if (skinBaseNode != null) pawn.story.SkinColorBase = ReadColor(skinBaseNode);
            var skinOverNode = app.SelectSingleNode("skinColorOverride");
            if (skinOverNode != null) pawn.story.skinColorOverride = ReadColor(skinOverNode);

            var melaninStr = GetText(app, "melanin");
            if (!melaninStr.NullOrEmpty())
            {
                var melanin = ParseFloat(melaninStr, -1f);
                if (melanin >= 0f) pawn.story.melanin = melanin;
            }

            try
            {
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                PortraitsCache.SetDirty(pawn);
                GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn);
            }
            catch (Exception ex) { Log.Warning($"[Pawn Editor] LoadAppearance graphics: {ex.Message}"); }
        }
        catch (Exception ex) { Warn($"Appearance: {ex.Message}"); }
    }

    // ── Load: Style ──

    private static void LoadStyle(Pawn pawn, XmlNode root)
    {
        if (pawn.style == null) return;
        var styleNode = root.SelectSingleNode("style");
        if (styleNode == null) return;
        try
        {
            var beardDef = ResolveDef<BeardDef>(styleNode, "beardDef");
            if (beardDef != null) pawn.style.beardDef = beardDef;
            if (ModsConfig.IdeologyActive)
            {
                var bodyTattoo = ResolveDef<TattooDef>(styleNode, "bodyTattoo");
                if (bodyTattoo != null) pawn.style.BodyTattoo = bodyTattoo;
                var faceTattoo = ResolveDef<TattooDef>(styleNode, "faceTattoo");
                if (faceTattoo != null) pawn.style.FaceTattoo = faceTattoo;
            }
            try
            {
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                PortraitsCache.SetDirty(pawn);
                GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn);
            }
            catch (Exception ex) { Log.Warning($"[Pawn Editor] LoadStyle graphics: {ex.Message}"); }
        }
        catch (Exception ex) { Warn($"Style: {ex.Message}"); }
    }

    // ── Load: Traits ──

    private static void LoadTraits(Pawn pawn, XmlNode root)
    {
        if (pawn.story?.traits == null) return;
        var traitsNode = root.SelectSingleNode("traits");
        if (traitsNode == null) return;
        try
        {
            pawn.story.traits.allTraits.Clear();
            foreach (XmlNode li in traitsNode.SelectNodes("li"))
            {
                if (!IsAvailable(li)) continue;
                var defName = li.Attributes?["defName"]?.Value;
                if (defName.NullOrEmpty()) continue;
                var def = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
                if (def == null) { Warn($"Trait '{defName}' not found, skipping"); continue; }
                int degree    = ParseInt(li.Attributes?["degree"]?.Value, 0);
                bool scenForced = ParseBool(li.Attributes?["scenForced"]?.Value, false);
                pawn.story.traits.GainTrait(new Trait(def, degree, scenForced));
            }
        }
        catch (Exception ex) { Warn($"Traits: {ex.Message}"); }
    }

    // ── Load: Genes ──

    private static void LoadGenes(Pawn pawn, XmlNode root)
    {
        if (!ModsConfig.BiotechActive || pawn.genes == null) return;
        var genesNode = root.SelectSingleNode("genes");
        if (genesNode == null) return;
        try
        {
            var endoNode = genesNode.SelectSingleNode("endogenes");
            if (endoNode != null)
            {
                pawn.genes.Endogenes.Clear();
                foreach (XmlNode li in endoNode.SelectNodes("li"))
                {
                    if (!IsAvailable(li)) continue;
                    var defName = li.Attributes?["defName"]?.Value;
                    if (defName.NullOrEmpty()) continue;
                    var def = DefDatabase<GeneDef>.GetNamedSilentFail(defName);
                    if (def == null) { Warn($"Endogene '{defName}' not found, skipping"); continue; }
                    pawn.genes.AddGene(def, xenogene: false);
                }
            }

            var xenoNode = genesNode.SelectSingleNode("xenogenes");
            if (xenoNode != null)
            {
                pawn.genes.Xenogenes.Clear();
                foreach (XmlNode li in xenoNode.SelectNodes("li"))
                {
                    if (!IsAvailable(li)) continue;
                    var defName = li.Attributes?["defName"]?.Value;
                    if (defName.NullOrEmpty()) continue;
                    var def = DefDatabase<GeneDef>.GetNamedSilentFail(defName);
                    if (def == null) { Warn($"Xenogene '{defName}' not found, skipping"); continue; }
                    pawn.genes.AddGene(def, xenogene: true);
                }
            }
        }
        catch (Exception ex) { Warn($"Genes: {ex.Message}"); }
    }

    // ── Load: Skills ──

    private static void LoadSkills(Pawn pawn, XmlNode root)
    {
        if (pawn.skills == null) return;
        var skillsNode = root.SelectSingleNode("skills");
        if (skillsNode == null) return;
        try
        {
            pawn.skills.skills.Clear();
            foreach (XmlNode li in skillsNode.SelectNodes("li"))
            {
                var defName = li.Attributes?["defName"]?.Value;
                if (defName.NullOrEmpty()) continue;
                var def = DefDatabase<SkillDef>.GetNamedSilentFail(defName);
                if (def == null) { Warn($"Skill '{defName}' not found, skipping"); continue; }

                var passionStr = li.Attributes?["passionName"]?.Value ?? li.Attributes?["passion"]?.Value;
                Passion passion = Passion.None;
                if (!passionStr.NullOrEmpty())
                {
                    if (Enum.TryParse<Passion>(passionStr, true, out var parsed))
                        passion = parsed;
                    else if (int.TryParse(passionStr, out var intVal))
                        passion = (Passion)intVal;
                }

                pawn.skills.skills.Add(new SkillRecord(pawn, def)
                {
                    levelInt         = ParseInt(li.Attributes?["level"]?.Value, 0),
                    passion          = passion,
                    xpSinceLastLevel = ParseFloat(li.Attributes?["xpSinceLastLevel"]?.Value, 0f)
                });
            }
        }
        catch (Exception ex) { Warn($"Skills: {ex.Message}"); }
    }

    // ── Load: Hediffs ──

    private static void LoadHediffs(Pawn pawn, XmlNode root)
    {
        if (pawn.health?.hediffSet == null) return;
        var hediffsNode = root.SelectSingleNode("hediffs");
        if (hediffsNode == null) return;
        try
        {
            foreach (XmlNode li in hediffsNode.SelectNodes("li"))
            {
                if (!IsAvailable(li)) continue;
                var defName = li.Attributes?["defName"]?.Value;
                if (defName.NullOrEmpty()) continue;
                var def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                if (def == null) { Warn($"Hediff '{defName}' not found, skipping"); continue; }

                // Resolve body part, using label for left/right disambiguation
                BodyPartRecord part = null;
                var bodyPartDefName = li.Attributes?["bodyPart"]?.Value;
                var bodyPartLabel   = li.Attributes?["bodyPartLabel"]?.Value;
                if (!bodyPartDefName.NullOrEmpty())
                {
                    var bpDef = DefDatabase<BodyPartDef>.GetNamedSilentFail(bodyPartDefName);
                    if (bpDef != null)
                    {
                        var candidates = pawn.health.hediffSet.GetNotMissingParts()
                            .Where(p => p.def == bpDef).ToList();
                        if (!bodyPartLabel.NullOrEmpty())
                            part = candidates.FirstOrDefault(p => p.Label == bodyPartLabel) ?? candidates.FirstOrDefault();
                        else
                            part = candidates.FirstOrDefault();
                    }
                }
                if (bodyPartDefName != null && part == null) continue; // Body part gone — skip

                try
                {
                    var hediff = HediffMaker.MakeHediff(def, pawn, part);
                    var severityStr = li.Attributes?["severity"]?.Value;
                    if (!severityStr.NullOrEmpty() && float.TryParse(severityStr,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var sev))
                        hediff.Severity = sev;

                    // Preserve permanent scar state
                    if (li.Attributes?["isPermanent"]?.Value == "true" && hediff is HediffWithComps hwc)
                    {
                        var permComp = hwc.TryGetComp<HediffComp_GetsPermanent>();
                        if (permComp != null) permComp.IsPermanent = true;
                    }

                    var ageTicksStr = li.Attributes?["ageTicks"]?.Value;
                    if (!ageTicksStr.NullOrEmpty() && int.TryParse(ageTicksStr, out var ageTicks))
                        hediff.ageTicks = ageTicks;

                    pawn.health.hediffSet.AddDirect(hediff);
                }
                catch (Exception ex) { Warn($"Hediff '{defName}': {ex.Message}"); }
            }
        }
        catch (Exception ex) { Warn($"Hediffs: {ex.Message}"); }
    }

    // ── Load: Abilities ──

    private static void LoadAbilities(Pawn pawn, XmlNode root)
    {
        if (pawn.abilities == null) return;
        var abNode = root.SelectSingleNode("abilities");
        if (abNode == null) return;
        try
        {
            foreach (XmlNode li in abNode.SelectNodes("li"))
            {
                if (!IsAvailable(li)) continue;
                var defName = li.Attributes?["defName"]?.Value;
                if (defName.NullOrEmpty()) continue;
                var def = DefDatabase<AbilityDef>.GetNamedSilentFail(defName);
                if (def == null) { Warn($"Ability '{defName}' not found, skipping"); continue; }
                if (pawn.abilities.GetAbility(def) == null)
                    pawn.abilities.GainAbility(def);
            }
        }
        catch (Exception ex) { Warn($"Abilities: {ex.Message}"); }
    }

    // ── Load: Apparel & Equipment ──

    private static void LoadApparel(Pawn pawn, XmlNode root)
    {
        var apparelNode = root.SelectSingleNode("apparel");
        if (apparelNode != null && pawn.apparel != null)
        {
            try
            {
                foreach (XmlNode li in apparelNode.SelectNodes("li"))
                {
                    if (!IsAvailable(li)) continue;
                    var defName = li.Attributes?["defName"]?.Value;
                    if (defName.NullOrEmpty()) continue;
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def == null) { Warn($"Apparel '{defName}' not found, skipping"); continue; }

                    ThingDef stuffDef = null;
                    var stuffName = li.Attributes?["stuff"]?.Value;
                    if (!stuffName.NullOrEmpty())
                        stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(stuffName);

                    var apparel = (Apparel)ThingMaker.MakeThing(def, stuffDef);

                    var hpStr = li.Attributes?["hp"]?.Value;
                    if (!hpStr.NullOrEmpty() && int.TryParse(hpStr, out var hp))
                        apparel.HitPoints = hp;

                    var qualStr = li.Attributes?["quality"]?.Value;
                    if (!qualStr.NullOrEmpty())
                    {
                        if (ParseEnum<QualityCategory>(qualStr, QualityCategory.Normal) is var qual)
                            apparel.TryGetComp<CompQuality>()?.SetQuality(qual, ArtGenerationContext.Outsider);
                    }

                    var colorStr = li.Attributes?["color"]?.Value;
                    if (!colorStr.NullOrEmpty())
                    {
                        var parts = colorStr.Split(',');
                        if (parts.Length >= 3)
                        {
                            var ci = System.Globalization.CultureInfo.InvariantCulture;
                            var color = new Color(
                                float.Parse(parts[0], ci), float.Parse(parts[1], ci),
                                float.Parse(parts[2], ci), parts.Length >= 4 ? float.Parse(parts[3], ci) : 1f);
                            apparel.TryGetComp<CompColorable>()?.SetColor(color);
                        }
                    }

                    pawn.apparel.Wear(apparel, dropReplacedApparel: false,
                        locked: li.Attributes?["locked"]?.Value == "true");
                }
            }
            catch (Exception ex) { Warn($"Apparel: {ex.Message}"); }
        }

        var equipNode = root.SelectSingleNode("equipment");
        if (equipNode != null && pawn.equipment != null)
        {
            try
            {
                foreach (XmlNode li in equipNode.SelectNodes("li"))
                {
                    if (!IsAvailable(li)) continue;
                    var defName = li.Attributes?["defName"]?.Value;
                    if (defName.NullOrEmpty()) continue;
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def == null) { Warn($"Equipment '{defName}' not found, skipping"); continue; }

                    ThingDef stuffDef = null;
                    var stuffName = li.Attributes?["stuff"]?.Value;
                    if (!stuffName.NullOrEmpty())
                        stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(stuffName);

                    var weapon = (ThingWithComps)ThingMaker.MakeThing(def, stuffDef);

                    var hpStr = li.Attributes?["hp"]?.Value;
                    if (!hpStr.NullOrEmpty() && int.TryParse(hpStr, out var hp))
                        weapon.HitPoints = hp;

                    var qualStr = li.Attributes?["quality"]?.Value;
                    if (!qualStr.NullOrEmpty())
                        weapon.TryGetComp<CompQuality>()?.SetQuality(
                            ParseEnum<QualityCategory>(qualStr, QualityCategory.Normal), ArtGenerationContext.Outsider);

                    pawn.equipment.AddEquipment(weapon);
                }
            }
            catch (Exception ex) { Warn($"Equipment: {ex.Message}"); }
        }
    }

    // ── Load: Social Relations ──

    private static void LoadRelations(Pawn pawn, XmlNode root)
    {
        if (pawn.relations == null) return;

        // Cache once — avoids iterating all maps/world pawns multiple times per load
        var allPawns = GetAllReachablePawns();

        var relationsNode = root.SelectSingleNode("relations");
        if (relationsNode != null)
        {
            try
            {
                foreach (XmlNode li in relationsNode.SelectNodes("li"))
                {
                    var relDef    = ResolveDef<PawnRelationDef>(li, "def");
                    if (relDef == null) continue;
                    var otherPawnID = GetText(li, "otherPawnID");
                    var otherFirst  = GetText(li, "otherPawnFirst");
                    var otherNick   = GetText(li, "otherPawnNick");
                    var otherLast   = GetText(li, "otherPawnLast");
                    var otherName   = GetText(li, "otherPawnName");

                    // Self-pawn check: skip if the saved name/ID matches this pawn itself
                    bool isSelf = (!otherPawnID.NullOrEmpty() && otherPawnID == pawn.ThingID)
                                || (!otherFirst.NullOrEmpty() && !otherLast.NullOrEmpty()
                                    && pawn.Name is NameTriple selfNt
                                    && selfNt.First == otherFirst && selfNt.Last == otherLast);
                    if (isSelf) continue;

                    Pawn otherPawn = null;
                    if (!otherPawnID.NullOrEmpty())
                        otherPawn = allPawns.FirstOrDefault(p => p != pawn && p.ThingID == otherPawnID);
                    if (otherPawn == null && !otherFirst.NullOrEmpty())
                        otherPawn = allPawns.FirstOrDefault(p =>
                            p != pawn && p.Name is NameTriple nt &&
                            nt.First == otherFirst && nt.Last == otherLast);
                    if (otherPawn == null && !otherName.NullOrEmpty())
                        otherPawn = allPawns.FirstOrDefault(p =>
                            p != pawn && p.Name?.ToStringFull == otherName);

                    if (otherPawn == null)
                    {
                        Warn($"Relation '{relDef.defName}': could not find '{otherFirst} {otherLast}'");
                        continue;
                    }

                    // v3d9 fix: Use the bidirectional extension method (relDef.AddDirectRelation)
                    // instead of the native unidirectional method (pawn.relations.AddDirectRelation).
                    // The extension method sets the relation on BOTH sides so the other pawn
                    // also shows the loaded pawn as a relation.
                    // OLD: pawn.relations.AddDirectRelation(relDef, otherPawn);
                    if (!pawn.relations.DirectRelationExists(relDef, otherPawn))
                    {
                        try { relDef.AddDirectRelation(pawn, otherPawn); }
                        catch (Exception ex)
                        {
                            Log.Warning($"[Pawn Editor] LoadRelations skip {relDef.defName}→{otherPawn.LabelShort}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex) { Warn($"Relations: {ex.Message}"); }
        }

        // Social memories (opinion values) — the pawn's own memories toward others
        var memNode = root.SelectSingleNode("socialMemories");
        if (memNode == null) return;
        var dstMems = pawn.needs?.mood?.thoughts?.memories;
        if (dstMems == null) return;
        try
        {
            foreach (XmlNode li in memNode.SelectNodes("li"))
            {
                try
                {
                    var def = ResolveDef<ThoughtDef>(li, "def");
                    if (def == null) continue;
                    var otherPawnID = GetText(li, "otherPawnID");
                    var otherFirst  = GetText(li, "otherPawnFirst");
                    var otherLast   = GetText(li, "otherPawnLast");
                    int.TryParse(GetText(li, "stageIndex"), out int stageIndex);
                    int.TryParse(GetText(li, "age"),        out int age);

                    bool isSelf = (!otherPawnID.NullOrEmpty() && otherPawnID == pawn.ThingID)
                                || (!otherFirst.NullOrEmpty() && !otherLast.NullOrEmpty()
                                    && pawn.Name is NameTriple selfNt
                                    && selfNt.First == otherFirst && selfNt.Last == otherLast);
                    if (isSelf) continue;

                    Pawn otherPawn = null;
                    if (!otherPawnID.NullOrEmpty())
                        otherPawn = allPawns.FirstOrDefault(p => p != pawn && p.ThingID == otherPawnID);
                    if (otherPawn == null && !otherFirst.NullOrEmpty())
                        otherPawn = allPawns.FirstOrDefault(p =>
                            p != pawn && p.Name is NameTriple nt &&
                            nt.First == otherFirst && nt.Last == otherLast);
                    if (otherPawn == null) continue;

                    var newMem = ThoughtMaker.MakeThought(def, stageIndex) as Thought_Memory;
                    if (newMem == null || !(newMem is ISocialThought)) continue;
                    newMem.age = age;
                    dstMems.TryGainMemory(newMem, otherPawn);
                }
                catch (Exception ex)
                {
                    if (Prefs.DevMode) Log.Warning($"[Pawn Editor] LoadRelations memory skip: {ex.Message}");
                }
            }
        }
        catch (Exception ex) { Warn($"SocialMemories: {ex.Message}"); }

        // v3d10: Load REAL reverse memories — what other pawns actually thought about this pawn.
        // Saved as <reverseSocialMemories> by WriteRelations. This replaces the old mirror hack
        // which guessed reverse opinions from the pawn's own memories.
        var reverseNode = root.SelectSingleNode("reverseSocialMemories");
        if (reverseNode != null)
        {
            try
            {
                foreach (XmlNode li in reverseNode.SelectNodes("li"))
                {
                    try
                    {
                        var def = ResolveDef<ThoughtDef>(li, "def");
                        if (def == null) continue;
                        var sourcePawnID = GetText(li, "sourcePawnID");
                        var sourceFirst  = GetText(li, "sourcePawnFirst");
                        var sourceLast   = GetText(li, "sourcePawnLast");
                        int.TryParse(GetText(li, "stageIndex"), out int stageIdx);
                        int.TryParse(GetText(li, "age"),        out int memAge);

                        Pawn sourcePawn = null;
                        if (!sourcePawnID.NullOrEmpty())
                            sourcePawn = allPawns.FirstOrDefault(p => p != pawn && p.ThingID == sourcePawnID);
                        if (sourcePawn == null && !sourceFirst.NullOrEmpty())
                            sourcePawn = allPawns.FirstOrDefault(p =>
                                p != pawn && p.Name is NameTriple nt &&
                                nt.First == sourceFirst && nt.Last == sourceLast);
                        if (sourcePawn == null) continue;

                        var sourceMems = sourcePawn.needs?.mood?.thoughts?.memories;
                        if (sourceMems == null) continue;

                        var reverseMem = ThoughtMaker.MakeThought(def, stageIdx) as Thought_Memory;
                        if (reverseMem == null || !(reverseMem is ISocialThought)) continue;
                        reverseMem.age = memAge;
                        sourceMems.TryGainMemory(reverseMem, pawn);
                    }
                    catch (Exception ex)
                    {
                        if (Prefs.DevMode) Log.Warning($"[Pawn Editor] LoadRelations reverse memory skip: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) { Warn($"ReverseSocialMemories: {ex.Message}"); }
        }
    }

    // ── Load: Work Priorities ──

    private static void LoadWorkPriorities(Pawn pawn, XmlNode root)
    {
        if (pawn.workSettings == null) return;
        var wpNode = root.SelectSingleNode("workPriorities");
        if (wpNode == null) return;
        try
        {
            pawn.workSettings.EnableAndInitialize();
            foreach (XmlNode li in wpNode.SelectNodes("li"))
            {
                var defName  = GetText(li, "def");
                var priority = ParseInt(GetText(li, "priority"), 0);
                if (defName.NullOrEmpty() || priority == 0) continue;
                var wd = DefDatabase<WorkTypeDef>.GetNamedSilentFail(defName);
                if (wd == null || pawn.WorkTypeIsDisabled(wd)) continue;
                pawn.workSettings.SetPriority(wd, priority);
            }
        }
        catch (Exception ex) { Warn($"WorkPriorities: {ex.Message}"); }
    }

    // ── Load: Inventory ──

    private static void LoadInventory(Pawn pawn, XmlNode root)
    {
        if (pawn.inventory?.innerContainer == null) return;
        var invNode = root.SelectSingleNode("inventory");
        if (invNode == null) return;
        try
        {
            foreach (XmlNode li in invNode.SelectNodes("li"))
            {
                var thingDef = ResolveDef<ThingDef>(li, "def");
                if (thingDef == null) continue;
                var stuffDef   = ResolveDef<ThingDef>(li, "stuff");
                var stackCount = ParseInt(GetText(li, "stackCount"), 1);

                var thing = (stuffDef != null && thingDef.MadeFromStuff)
                    ? ThingMaker.MakeThing(thingDef, stuffDef)
                    : ThingMaker.MakeThing(thingDef);
                thing.stackCount = stackCount;

                var qualityStr = GetText(li, "quality");
                if (!qualityStr.NullOrEmpty() && thing.TryGetComp<CompQuality>() is { } cq)
                {
                    QualityCategory qual;
                    if (int.TryParse(qualityStr, out var qi))
                        qual = (QualityCategory)qi;
                    else
                        qual = ParseEnum<QualityCategory>(qualityStr, QualityCategory.Normal);
                    cq.SetQuality(qual, ArtGenerationContext.Outsider);
                }

                pawn.inventory.innerContainer.TryAdd(thing);
            }
        }
        catch (Exception ex) { Warn($"Inventory: {ex.Message}"); }
    }

    // ── Load: Royal Titles (Royalty DLC) ──

    private static void LoadRoyalTitles(Pawn pawn, XmlNode root)
    {
        if (!ModsConfig.RoyaltyActive || pawn.royalty == null) return;
        var titlesNode = root.SelectSingleNode("royalTitles");
        var psylinkStr = GetText(root, "psylinkLevel");
        try
        {
            if (!psylinkStr.NullOrEmpty())
            {
                var targetLevel  = ParseInt(psylinkStr, 0);
                var currentLevel = pawn.GetPsylinkLevel();
                for (int i = currentLevel; i < targetLevel; i++)
                    pawn.ChangePsylinkLevel(1);
            }

            if (titlesNode != null)
            {
                foreach (XmlNode li in titlesNode.SelectNodes("li"))
                {
                    var titleDef = ResolveDef<RoyalTitleDef>(li, "titleDef");
                    if (titleDef == null) continue;
                    var factionDefName = GetText(li, "faction");
                    if (factionDefName.NullOrEmpty()) continue;
                    var faction = Find.FactionManager?.AllFactions?
                        .FirstOrDefault(f => f.def?.defName == factionDefName);
                    if (faction == null) { Warn($"Royal title '{titleDef.defName}': faction '{factionDefName}' not found"); continue; }
                    pawn.royalty.SetTitle(faction, titleDef, false);
                }
            }

            var favorNode = root.SelectSingleNode("favor");
            if (favorNode != null)
            {
                foreach (XmlNode li in favorNode.SelectNodes("li"))
                {
                    var factionDefName = GetText(li, "faction");
                    var amount = ParseInt(GetText(li, "amount"), 0);
                    if (factionDefName.NullOrEmpty() || amount <= 0) continue;
                    var faction = Find.FactionManager?.AllFactions?
                        .FirstOrDefault(f => f.def?.defName == factionDefName);
                    if (faction != null) pawn.royalty.SetFavor(faction, amount);
                }
            }
        }
        catch (Exception ex) { Warn($"RoyalTitles: {ex.Message}"); }
    }

    // ── Load: Records ──

    private static void LoadRecords(Pawn pawn, XmlNode root)
    {
        if (pawn.records == null) return;
        var recordsNode = root.SelectSingleNode("records");
        if (recordsNode == null) return;
        try
        {
            foreach (XmlNode li in recordsNode.SelectNodes("li"))
            {
                var defName = GetText(li, "def");
                var value   = ParseFloat(GetText(li, "value"), 0f);
                if (defName.NullOrEmpty()) continue;
                var rd = DefDatabase<RecordDef>.GetNamedSilentFail(defName);
                if (rd == null) continue;

                // v3d9 fix: Skip Time-type records — RimWorld tracks these internally
                // via ticks. Calling AddTo() on them logs "Tried to add value to record
                // whose record type is Time" and does nothing useful.
                if (rd.type == RecordType.Time) continue;

                pawn.records.AddTo(rd, value - pawn.records.GetValue(rd));
            }
        }
        catch (Exception ex) { Warn($"Records: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Shared parse/resolve helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks if an XML element's MayRequire mod is loaded.
    /// If no MayRequire attribute is present the element is always considered available.
    /// </summary>
    private static bool IsAvailable(XmlNode node)
    {
        var mayRequire = node?.Attributes?["MayRequire"]?.Value;
        if (mayRequire.NullOrEmpty()) return true;
        return ModLister.GetActiveModWithIdentifier(mayRequire, ignorePostfix: true) != null;
    }

    /// <summary>
    /// Resolves a Def from a child element with a defName attribute, respecting MayRequire.
    /// Returns null gracefully when the def or its mod cannot be found.
    /// </summary>
    private static T ResolveDef<T>(XmlNode parent, string elementName) where T : Def
    {
        var node = parent?.SelectSingleNode(elementName);
        if (node == null) return null;
        var defName = node.Attributes?["defName"]?.Value ?? node.InnerText?.Trim();
        if (defName.NullOrEmpty()) return null;

        if (!IsAvailable(node))
        {
            // Mod missing — try anyway in case another mod provides the same defName
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

    private static int   ParseInt(string text, int fallback)
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
