using System;
using System.Xml;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Save side: Pawn → Blueprint XML.
/// Split across three files by responsibility:
///   _Save.cs              — Identity (Name/Story/Appearance/Style/Traits/Genes/Skills) + XML helpers (this file)
///   _Save_Health.cs       — Hediffs, Abilities, Apparel/Equipment
///   _Save_Social.cs       — Relations, WorkPriorities, Inventory, RoyalTitles, Records, ModList
/// </summary>
public static partial class PawnBlueprintSaveLoad
{
    // ── Save: Identity ──

    private static void WriteIdentity(XmlWriter w, Pawn pawn)
    {
        if (pawn.Name is NameTriple nt)
        {
            w.WriteStartElement("name");
            w.WriteAttributeString("first", nt.First ?? "");
            w.WriteAttributeString("nick",  nt.Nick  ?? "");
            w.WriteAttributeString("last",  nt.Last  ?? "");
            w.WriteEndElement();
        }
        else if (pawn.Name != null)
            WriteSimple(w, "name", pawn.Name.ToStringFull);

        WriteSimple(w, "gender",          pawn.gender.ToString());
        WriteSimple(w, "biologicalAge",   pawn.ageTracker.AgeBiologicalYearsFloat.ToString("F2"));
        WriteSimple(w, "chronologicalAge",pawn.ageTracker.AgeChronologicalYearsFloat.ToString("F2"));

        WriteDefWithSource(w, "kindDef", pawn.kindDef);

        if (pawn.Faction != null)
            WriteSimple(w, "factionDefName", pawn.Faction.def.defName);

        if (ModsConfig.IdeologyActive && pawn.Ideo != null)
        {
            WriteSimple(w, "ideoName", pawn.Ideo.name);
            w.WriteStartElement("ideoCertainty");
            w.WriteString(pawn.ideo.Certainty.ToString("F4"));
            w.WriteEndElement();
        }

        if (ModsConfig.BiotechActive && pawn.genes != null)
        {
            if (pawn.genes.Xenotype != null)
                WriteDefWithSource(w, "xenotypeDef", pawn.genes.Xenotype);
            if (!pawn.genes.xenotypeName.NullOrEmpty())
                WriteSimple(w, "xenotypeName", pawn.genes.xenotypeName);
            if (pawn.genes.iconDef != null)
                WriteDefWithSource(w, "xenotypeIconDef", pawn.genes.iconDef);
            WriteSimple(w, "growthPoints", pawn.ageTracker.growthPoints.ToString("F2"));
        }

        if (pawn.story?.favoriteColor != null)
            WriteColor(w, "favoriteColor", pawn.story.favoriteColor.color);
    }

    // ── Save: Story (backstories) ──

    private static void WriteStory(XmlWriter w, Pawn pawn)
    {
        if (pawn.story == null) return;
        if (pawn.story.Childhood != null) WriteDefWithSource(w, "childhood", pawn.story.Childhood);
        if (pawn.story.Adulthood != null) WriteDefWithSource(w, "adulthood", pawn.story.Adulthood);
    }

    // ── Save: Appearance ──

    private static void WriteAppearance(XmlWriter w, Pawn pawn)
    {
        if (pawn.story == null) return;
        w.WriteStartElement("appearance");
        if (pawn.story.bodyType != null) WriteDefWithSource(w, "bodyType", pawn.story.bodyType);
        if (pawn.story.headType != null) WriteDefWithSource(w, "headType", pawn.story.headType);
        if (pawn.story.hairDef  != null) WriteDefWithSource(w, "hairDef",  pawn.story.hairDef);
        if (pawn.story.furDef   != null) WriteDefWithSource(w, "furDef",   pawn.story.furDef);
        WriteColor(w, "hairColor",     pawn.story.HairColor);
        WriteColor(w, "skinColorBase", pawn.story.SkinColorBase);
        if (pawn.story.skinColorOverride.HasValue)
            WriteColor(w, "skinColorOverride", pawn.story.skinColorOverride.Value);
        WriteSimple(w, "melanin", pawn.story.melanin.ToString("F4"));
        w.WriteEndElement();
    }

    // ── Save: Style ──

    private static void WriteStyle(XmlWriter w, Pawn pawn)
    {
        if (pawn.style == null) return;
        w.WriteStartElement("style");
        if (pawn.style.beardDef != null) WriteDefWithSource(w, "beardDef", pawn.style.beardDef);
        if (ModsConfig.IdeologyActive)
        {
            if (pawn.style.BodyTattoo != null) WriteDefWithSource(w, "bodyTattoo", pawn.style.BodyTattoo);
            if (pawn.style.FaceTattoo != null) WriteDefWithSource(w, "faceTattoo", pawn.style.FaceTattoo);
        }
        w.WriteEndElement();
    }

    // ── Save: Traits ──

    private static void WriteTraits(XmlWriter w, Pawn pawn)
    {
        if (pawn.story?.traits?.allTraits == null) return;
        w.WriteStartElement("traits");
        foreach (var trait in pawn.story.traits.allTraits)
        {
            if (trait?.def == null) continue;
            if (ModsConfig.BiotechActive && trait.sourceGene != null) continue;
            w.WriteStartElement("li");
            w.WriteAttributeString("defName", trait.def.defName);
            w.WriteAttributeString("degree",  trait.Degree.ToString());
            if (trait.ScenForced) w.WriteAttributeString("scenForced", "true");
            WriteSourceMod(w, trait.def);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    // ── Save: Genes ──

    private static void WriteGenes(XmlWriter w, Pawn pawn)
    {
        if (!ModsConfig.BiotechActive || pawn.genes == null) return;
        w.WriteStartElement("genes");

        w.WriteStartElement("endogenes");
        foreach (var gene in pawn.genes.Endogenes)
        {
            if (gene?.def == null) continue;
            w.WriteStartElement("li");
            w.WriteAttributeString("defName", gene.def.defName);
            WriteSourceMod(w, gene.def);
            w.WriteEndElement();
        }
        w.WriteEndElement();

        w.WriteStartElement("xenogenes");
        foreach (var gene in pawn.genes.Xenogenes)
        {
            if (gene?.def == null) continue;
            w.WriteStartElement("li");
            w.WriteAttributeString("defName", gene.def.defName);
            WriteSourceMod(w, gene.def);
            w.WriteEndElement();
        }
        w.WriteEndElement();

        w.WriteEndElement();
    }

    // ── Save: Skills ──

    private static void WriteSkills(XmlWriter w, Pawn pawn)
    {
        if (pawn.skills == null) return;
        w.WriteStartElement("skills");
        foreach (var skill in pawn.skills.skills)
        {
            if (skill?.def == null) continue;
            w.WriteStartElement("li");
            w.WriteAttributeString("defName",          skill.def.defName);
            w.WriteAttributeString("level",            skill.levelInt.ToString());
            w.WriteAttributeString("passion",          ((int)skill.passion).ToString());
            w.WriteAttributeString("passionName",      skill.passion.ToString());
            w.WriteAttributeString("xpSinceLastLevel", skill.xpSinceLastLevel.ToString("F0"));
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Shared XML write helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void WriteSimple(XmlWriter w, string name, string value)
    {
        if (value.NullOrEmpty()) return;
        w.WriteElementString(name, value);
    }

    private static void WriteColor(XmlWriter w, string name, Color c)
    {
        w.WriteStartElement(name);
        w.WriteAttributeString("r", c.r.ToString("F3"));
        w.WriteAttributeString("g", c.g.ToString("F3"));
        w.WriteAttributeString("b", c.b.ToString("F3"));
        w.WriteAttributeString("a", c.a.ToString("F3"));
        w.WriteEndElement();
    }

    private static void WriteDefWithSource(XmlWriter w, string elementName, Def def)
    {
        if (def == null) return;
        w.WriteStartElement(elementName);
        w.WriteAttributeString("defName", def.defName);
        WriteSourceMod(w, def);
        w.WriteEndElement();
    }

    private static void WriteSourceMod(XmlWriter w, Def def)
    {
        if (def?.modContentPack == null) return;
        if (def.modContentPack.IsOfficialMod || def.modContentPack.IsCoreMod) return;
        var packageId = def.modContentPack.PackageId;
        if (!packageId.NullOrEmpty())
            w.WriteAttributeString("MayRequire", packageId.ToLower());
    }
}
