using System;
using System.Linq;
using System.Xml;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Load: Identity section.
/// Name, Story, Appearance, Style, Traits, Genes, Skills.
/// </summary>
public static partial class PawnBlueprintSaveLoad
{
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
}
