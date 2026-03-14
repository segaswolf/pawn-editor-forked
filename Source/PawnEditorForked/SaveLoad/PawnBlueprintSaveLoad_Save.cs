using System;
using System.Linq;
using System.Xml;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Save side: Pawn → Blueprint XML.
/// Each Write* method serialises one logical section of the pawn.
/// Shared XML helpers (WriteSimple, WriteColor, WriteDefWithSource, WriteSourceMod)
/// live at the bottom of this file because they are only called from Write* methods.
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
            // Gene-granted traits are re-added when genes load — skip them here
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

        w.WriteEndElement(); // genes
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

    // ── Save: Hediffs ──

    private static void WriteHediffs(XmlWriter w, Pawn pawn)
    {
        if (pawn.health?.hediffSet == null) return;
        w.WriteStartElement("hediffs");
        foreach (var hediff in pawn.health.hediffSet.hediffs)
        {
            if (hediff?.def == null) continue;
            if (!hediff.def.duplicationAllowed) continue;
            // Non-organic implants (bionics) are handled at CopyDup level — skip here
            if ((hediff is Hediff_AddedPart || hediff is Hediff_Implant) && !hediff.def.organicAddedBodypart) continue;

            w.WriteStartElement("li");
            w.WriteAttributeString("defName",  hediff.def.defName);
            w.WriteAttributeString("severity", hediff.Severity.ToString("F3"));
            if (hediff.Part != null)
            {
                w.WriteAttributeString("bodyPart", hediff.Part.def.defName);
                // bodyPartLabel disambiguates left/right (e.g. "left lung" vs "right lung")
                if (!hediff.Part.Label.NullOrEmpty())
                    w.WriteAttributeString("bodyPartLabel", hediff.Part.Label);
            }
            if (hediff.IsPermanent()) w.WriteAttributeString("isPermanent", "true");
            if (hediff.ageTicks > 0)  w.WriteAttributeString("ageTicks", hediff.ageTicks.ToString());
            WriteSourceMod(w, hediff.def);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    // ── Save: Abilities ──

    private static void WriteAbilities(XmlWriter w, Pawn pawn)
    {
        if (pawn.abilities?.abilities == null) return;
        w.WriteStartElement("abilities");
        foreach (var ability in pawn.abilities.abilities)
        {
            if (ability?.def == null) continue;
            w.WriteStartElement("li");
            w.WriteAttributeString("defName", ability.def.defName);
            WriteSourceMod(w, ability.def);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    // ── Save: Apparel & Equipment ──

    private static void WriteApparel(XmlWriter w, Pawn pawn)
    {
        if (pawn.apparel?.WornApparel != null && pawn.apparel.WornApparel.Count > 0)
        {
            w.WriteStartElement("apparel");
            foreach (var worn in pawn.apparel.WornApparel)
            {
                if (worn?.def == null) continue;
                w.WriteStartElement("li");
                w.WriteAttributeString("defName", worn.def.defName);
                WriteSourceMod(w, worn.def);
                if (worn.Stuff != null) w.WriteAttributeString("stuff", worn.Stuff.defName);
                w.WriteAttributeString("hp",    worn.HitPoints.ToString());
                w.WriteAttributeString("maxHp", worn.MaxHitPoints.ToString());
                if (worn.TryGetQuality(out var quality))
                    w.WriteAttributeString("quality", quality.ToString());
                var colorComp = worn.TryGetComp<CompColorable>();
                if (colorComp != null && colorComp.Active)
                {
                    var c = colorComp.Color;
                    w.WriteAttributeString("color", $"{c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}");
                }
                if (pawn.apparel.IsLocked(worn)) w.WriteAttributeString("locked", "true");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        if (pawn.equipment?.AllEquipmentListForReading != null && pawn.equipment.AllEquipmentListForReading.Count > 0)
        {
            w.WriteStartElement("equipment");
            foreach (var equip in pawn.equipment.AllEquipmentListForReading)
            {
                if (equip?.def == null) continue;
                w.WriteStartElement("li");
                w.WriteAttributeString("defName", equip.def.defName);
                WriteSourceMod(w, equip.def);
                if (equip.Stuff != null) w.WriteAttributeString("stuff", equip.Stuff.defName);
                w.WriteAttributeString("hp",    equip.HitPoints.ToString());
                w.WriteAttributeString("maxHp", equip.MaxHitPoints.ToString());
                if (equip.TryGetQuality(out var quality))
                    w.WriteAttributeString("quality", quality.ToString());
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
    }

    // ── Save: Social Relations ──

    private static void WriteRelations(XmlWriter w, Pawn pawn)
    {
        if (pawn.relations == null) return;
        var directRelations = pawn.relations.DirectRelations;
        if (directRelations == null || directRelations.Count == 0) return;

        try
        {
            w.WriteStartElement("relations");
            foreach (var rel in directRelations)
            {
                if (rel.def == null || rel.otherPawn == null) continue;
                // Self-referential relations lose meaning after a blueprint replace
                if (rel.otherPawn == pawn) continue;
                w.WriteStartElement("li");
                WriteDefWithSource(w, "def", rel.def);
                w.WriteElementString("otherPawnID", rel.otherPawn.ThingID ?? "");
                if (rel.otherPawn.Name is NameTriple nt)
                {
                    w.WriteElementString("otherPawnFirst", nt.First ?? "");
                    w.WriteElementString("otherPawnNick",  nt.Nick  ?? "");
                    w.WriteElementString("otherPawnLast",  nt.Last  ?? "");
                }
                else if (rel.otherPawn.Name != null)
                    w.WriteElementString("otherPawnName", rel.otherPawn.Name.ToStringFull ?? "");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteRelations: {ex.Message}"); }

        // Social memories (drive the opinion numbers shown in the Social tab)
        try
        {
            var memories = pawn.needs?.mood?.thoughts?.memories?.Memories;
            if (memories == null) return;

            // ISocialThought is the RimWorld 1.6 interface for memories with an otherPawn
            var socialMems = memories
                .Where(m => m is ISocialThought st && m.def != null
                         && st.OtherPawn() != null && st.OtherPawn() != pawn)
                .Cast<Thought_Memory>()
                .ToList();
            if (socialMems.Count == 0) return;

            w.WriteStartElement("socialMemories");
            foreach (var mem in socialMems)
            {
                var otherPawnRef = ((ISocialThought)mem).OtherPawn();
                try
                {
                    w.WriteStartElement("li");
                    WriteDefWithSource(w, "def", mem.def);
                    w.WriteElementString("stageIndex",    mem.CurStageIndex.ToString());
                    w.WriteElementString("age",           mem.age.ToString());
                    w.WriteElementString("otherPawnID",   otherPawnRef.ThingID ?? "");
                    if (otherPawnRef.Name is NameTriple nt2)
                    {
                        w.WriteElementString("otherPawnFirst", nt2.First ?? "");
                        w.WriteElementString("otherPawnLast",  nt2.Last  ?? "");
                    }
                    w.WriteEndElement();
                }
                catch (Exception ex)
                {
                    if (Prefs.DevMode) Log.Warning($"[Pawn Editor] WriteRelations memory skip: {ex.Message}");
                }
            }
            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteRelations (memories): {ex.Message}"); }

        // v3d10: Reverse social memories — what OTHER pawns think about THIS pawn.
        // Without this, blueprint load only has the pawn's own memories and must
        // guess the reverse via mirroring, which loses asymmetric interactions.
        try
        {
            var allPawns = GetAllReachablePawns();
            var reverseMems = new System.Collections.Generic.List<(Thought_Memory mem, Pawn source)>();
            foreach (var other in allPawns)
            {
                if (other == pawn || other.needs?.mood?.thoughts?.memories == null) continue;
                var otherMemories = other.needs.mood.thoughts.memories.Memories;
                foreach (var mem in otherMemories)
                {
                    if (mem == null || mem.def == null) continue;
                    if (!(mem is ISocialThought st)) continue;
                    var target = st.OtherPawn();
                    if (target == null || target != pawn) continue;
                    reverseMems.Add((mem, other));
                }
            }

            if (reverseMems.Count > 0)
            {
                w.WriteStartElement("reverseSocialMemories");
                foreach (var (mem, source) in reverseMems)
                {
                    try
                    {
                        w.WriteStartElement("li");
                        WriteDefWithSource(w, "def", mem.def);
                        w.WriteElementString("stageIndex", mem.CurStageIndex.ToString());
                        w.WriteElementString("age",        mem.age.ToString());
                        w.WriteElementString("sourcePawnID", source.ThingID ?? "");
                        if (source.Name is NameTriple snt)
                        {
                            w.WriteElementString("sourcePawnFirst", snt.First ?? "");
                            w.WriteElementString("sourcePawnLast",  snt.Last  ?? "");
                        }
                        w.WriteEndElement();
                    }
                    catch (Exception ex)
                    {
                        if (Prefs.DevMode) Log.Warning($"[Pawn Editor] WriteRelations reverse memory skip: {ex.Message}");
                    }
                }
                w.WriteEndElement();
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteRelations (reverse memories): {ex.Message}"); }
    }

    // ── Save: Work Priorities ──

    private static void WriteWorkPriorities(XmlWriter w, Pawn pawn)
    {
        if (pawn.workSettings == null) return;
        try
        {
            var workDefs = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            if (workDefs == null || workDefs.Count == 0) return;

            bool anySet = workDefs.Any(wd =>
                !pawn.WorkTypeIsDisabled(wd) && pawn.workSettings.GetPriority(wd) != 0);
            if (!anySet) return;

            w.WriteStartElement("workPriorities");
            foreach (var wd in workDefs)
            {
                if (pawn.WorkTypeIsDisabled(wd)) continue;
                var pri = pawn.workSettings.GetPriority(wd);
                if (pri == 0) continue;
                w.WriteStartElement("li");
                w.WriteElementString("def",      wd.defName);
                w.WriteElementString("priority", pri.ToString());
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteWorkPriorities: {ex.Message}"); }
    }

    // ── Save: Inventory ──

    private static void WriteInventory(XmlWriter w, Pawn pawn)
    {
        if (pawn.inventory?.innerContainer == null) return;
        if (pawn.inventory.innerContainer.Count == 0) return;
        try
        {
            w.WriteStartElement("inventory");
            foreach (var thing in pawn.inventory.innerContainer)
            {
                if (thing?.def == null) continue;
                w.WriteStartElement("li");
                WriteDefWithSource(w, "def", thing.def);
                if (thing.Stuff != null) WriteDefWithSource(w, "stuff", thing.Stuff);
                w.WriteElementString("stackCount", thing.stackCount.ToString());
                if (thing.TryGetComp<CompQuality>() is { } cq)
                    w.WriteElementString("quality", ((int)cq.Quality).ToString());
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteInventory: {ex.Message}"); }
    }

    // ── Save: Royal Titles (Royalty DLC) ──

    private static void WriteRoyalTitles(XmlWriter w, Pawn pawn)
    {
        if (!ModsConfig.RoyaltyActive || pawn.royalty == null) return;
        var titles = pawn.royalty.AllTitlesForReading;
        if (titles == null || titles.Count == 0) return;
        try
        {
            w.WriteStartElement("royalTitles");
            foreach (var title in titles)
            {
                if (title?.def == null || title.faction == null) continue;
                w.WriteStartElement("li");
                WriteDefWithSource(w, "titleDef", title.def);
                w.WriteElementString("faction",      title.faction.def?.defName ?? "");
                w.WriteElementString("receivedTick", title.receivedTick.ToString());
                w.WriteEndElement();
            }
            w.WriteEndElement();

            var psylinkLevel = pawn.GetPsylinkLevel();
            if (psylinkLevel > 0)
                w.WriteElementString("psylinkLevel", psylinkLevel.ToString());

            w.WriteStartElement("favor");
            foreach (var faction in Find.FactionManager.AllFactions)
            {
                var favor = pawn.royalty.GetFavor(faction);
                if (favor <= 0) continue;
                w.WriteStartElement("li");
                w.WriteElementString("faction", faction.def?.defName ?? "");
                w.WriteElementString("amount",  favor.ToString());
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteRoyalTitles: {ex.Message}"); }
    }

    // ── Save: Records ──

    private static void WriteRecords(XmlWriter w, Pawn pawn)
    {
        if (pawn.records == null) return;
        try
        {
            var allRecordDefs = DefDatabase<RecordDef>.AllDefsListForReading;
            if (allRecordDefs == null || allRecordDefs.Count == 0) return;
            if (!allRecordDefs.Any(rd => pawn.records.GetValue(rd) != 0f)) return;

            w.WriteStartElement("records");
            foreach (var rd in allRecordDefs)
            {
                var val = pawn.records.GetValue(rd);
                if (val == 0f) continue;
                w.WriteStartElement("li");
                w.WriteElementString("def",   rd.defName);
                w.WriteElementString("value", val.ToString("G9"));
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteRecords: {ex.Message}"); }
    }

    // ── Save: Mod list (for future compatibility warnings on load) ──

    private static void WriteModList(XmlWriter w)
    {
        try
        {
            w.WriteStartElement("modList");
            foreach (var mod in LoadedModManager.RunningMods)
            {
                if (mod == null) continue;
                w.WriteStartElement("li");
                w.WriteElementString("packageId", mod.PackageId ?? "");
                w.WriteElementString("name",      mod.Name      ?? "");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteModList: {ex.Message}"); }
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

    /// <summary>
    /// Writes MayRequire="packageId" when the def comes from a non-vanilla mod.
    /// On load, this allows graceful skipping if that mod is absent.
    /// </summary>
    private static void WriteSourceMod(XmlWriter w, Def def)
    {
        if (def?.modContentPack == null) return;
        if (def.modContentPack.IsOfficialMod || def.modContentPack.IsCoreMod) return;
        var packageId = def.modContentPack.PackageId;
        if (!packageId.NullOrEmpty())
            w.WriteAttributeString("MayRequire", packageId.ToLower());
    }
}
