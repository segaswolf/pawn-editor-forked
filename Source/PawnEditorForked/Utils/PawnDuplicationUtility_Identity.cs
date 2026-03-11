using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Identity copy methods.
/// Covers everything that defines WHO the pawn is:
/// backstory, traits, genes, appearance, style, skills, needs and memories.
/// </summary>
public static partial class PawnEditor
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Story and Traits
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies backstory (childhood/adulthood) and traits.
    /// Gene-granted traits are skipped — CopyDup_Genes re-adds them when the
    /// genes themselves are copied.
    /// </summary>
    private static void CopyDup_StoryAndTraits(Pawn src, Pawn dst)
    {
        if (src.story == null || dst.story == null) return;

        dst.story.Childhood = src.story.Childhood;
        dst.story.Adulthood = src.story.Adulthood; // null is valid for children

        dst.story.traits.allTraits.Clear();
        foreach (var trait in src.story.traits.allTraits)
        {
            // Gene-granted traits are handled by CopyDup_Genes
            if (ModsConfig.BiotechActive && trait.sourceGene != null) continue;

            if (trait?.def == null || !DefDatabase<TraitDef>.AllDefsListForReading.Contains(trait.def))
            {
                Log.Warning($"[Pawn Editor] Skipping missing trait: {trait?.def?.defName ?? "null"}");
                continue;
            }
            dst.story.traits.GainTrait(new Trait(trait.def, trait.Degree, trait.ScenForced));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Genes (Biotech DLC)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies xenogenes and endogenes, preserving override relationships.
    /// No-op if Biotech DLC is not active.
    /// </summary>
    private static void CopyDup_Genes(Pawn src, Pawn dst)
    {
        if (!ModsConfig.BiotechActive) return;
        if (src.genes == null || dst.genes == null) return;

        // ── Xenogenes (mod-added, non-inheritable) ──
        dst.genes.Xenogenes.Clear();
        var srcXeno = src.genes.Xenogenes;
        foreach (var gene in srcXeno)
        {
            if (gene?.def == null || !DefDatabase<GeneDef>.AllDefsListForReading.Contains(gene.def))
            {
                Log.Warning($"[Pawn Editor] Skipping missing xenogene: {gene?.def?.defName ?? "null"}");
                continue;
            }
            dst.genes.AddGene(gene.def, xenogene: true);
        }
        // Preserve which genes are overriding which
        for (int i = 0; i < srcXeno.Count && i < dst.genes.Xenogenes.Count; i++)
        {
            var overriderDef = srcXeno[i].Overridden ? srcXeno[i].overriddenByGene?.def : null;
            dst.genes.Xenogenes[i].overriddenByGene = overriderDef != null
                ? dst.genes.GenesListForReading.FirstOrDefault(e => e.def == overriderDef)
                : null;
        }

        // ── Endogenes (inheritable, part of xenotype) ──
        dst.genes.Endogenes.Clear();
        var srcEndo = src.genes.Endogenes;
        foreach (var gene in srcEndo)
        {
            if (gene?.def == null || !DefDatabase<GeneDef>.AllDefsListForReading.Contains(gene.def))
            {
                Log.Warning($"[Pawn Editor] Skipping missing endogene: {gene?.def?.defName ?? "null"}");
                continue;
            }
            dst.genes.AddGene(gene.def, xenogene: false);
        }
        for (int i = 0; i < srcEndo.Count && i < dst.genes.Endogenes.Count; i++)
        {
            var overriderDef = srcEndo[i].Overridden ? srcEndo[i].overriddenByGene?.def : null;
            dst.genes.Endogenes[i].overriddenByGene = overriderDef != null
                ? dst.genes.GenesListForReading.FirstOrDefault(e => e.def == overriderDef)
                : null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Appearance
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies all visual appearance fields: head type, body type, hair, skin, fur.
    /// Called AFTER CopyDup_Genes so we can override any gene-forced appearance
    /// back to the exact values the source pawn had.
    /// </summary>
    private static void CopyDup_Appearance(Pawn src, Pawn dst)
    {
        if (src.story == null || dst.story == null) return;

        dst.story.headType          = src.story.headType;
        dst.story.bodyType          = src.story.bodyType;
        dst.story.hairDef           = src.story.hairDef;
        dst.story.HairColor         = src.story.HairColor;
        dst.story.SkinColorBase     = src.story.SkinColorBase;
        dst.story.skinColorOverride = src.story.skinColorOverride;
        dst.story.furDef            = src.story.furDef;
        try { dst.story.melanin = src.story.melanin; } catch { }

        // Invalidate all cached render data so the game re-bakes from the new values
        try
        {
            dst.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(dst);
            GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(dst);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup graphics refresh (appearance): {ex.Message}"); }
    }

    /// <summary>
    /// Copies beard and tattoo style fields.
    /// Separate from CopyDup_Appearance because style lives on Pawn.style (not
    /// Pawn.story), and tattoos require the Ideology DLC.
    /// </summary>
    private static void CopyDup_Style(Pawn src, Pawn dst)
    {
        if (src.style == null || dst.style == null) return;

        dst.style.beardDef = src.style.beardDef;
        if (ModsConfig.IdeologyActive)
        {
            dst.style.BodyTattoo = src.style.BodyTattoo;
            dst.style.FaceTattoo = src.style.FaceTattoo;
        }

        try
        {
            dst.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(dst);
            GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(dst);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup graphics refresh (style): {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Skills
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies all skill levels, passions, and XP values.
    /// Rebuilds the skill list from scratch to avoid stale references.
    /// </summary>
    private static void CopyDup_Skills(Pawn src, Pawn dst)
    {
        if (src.skills == null || dst.skills == null) return;

        dst.skills.skills.Clear();
        foreach (var skill in src.skills.skills)
        {
            dst.skills.skills.Add(new SkillRecord(dst, skill.def)
            {
                levelInt         = skill.levelInt,
                passion          = skill.passion,
                xpSinceLastLevel = skill.xpSinceLastLevel,
                xpSinceMidnight  = skill.xpSinceMidnight
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Needs and Thought Memories
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies all needs (food, rest, mood, etc.) and non-social thought memories.
    ///
    /// Social memories (ISocialThought) are intentionally skipped here.
    /// They are handled in CopyDup_Relations (Pass 2), where each memory
    /// gets properly bound to the correct otherPawn reference.
    /// </summary>
    private static void CopyDup_Needs(Pawn src, Pawn dst)
    {
        if (src.needs == null || dst.needs == null) return;

        try
        {
            dst.needs.AllNeeds.Clear();
            foreach (var srcNeed in src.needs.AllNeeds)
            {
                if (srcNeed?.def?.needClass == null) continue;
                try
                {
                    var need = (Need)Activator.CreateInstance(srcNeed.def.needClass, dst);
                    need.def = srcNeed.def;
                    dst.needs.AllNeeds.Add(need);
                    need.SetInitialLevel();
                    need.CurLevel = srcNeed.CurLevel;
                    dst.needs.BindDirectNeedFields();
                }
                catch
                {
                    // Some needs cannot be constructed outside their expected context — skip
                }
            }

            if (src.needs.mood?.thoughts?.memories != null && dst.needs.mood?.thoughts?.memories != null)
            {
                var dstMemories = dst.needs.mood.thoughts.memories.Memories;
                dstMemories.Clear();
                foreach (var memory in src.needs.mood.thoughts.memories.Memories)
                {
                    if (memory?.def == null) continue;
                    // Social memories need a live otherPawn ref — handled in CopyDup_Relations
                    if (memory is ISocialThought) continue;
                    try
                    {
                        var copy = (Thought_Memory)ThoughtMaker.MakeThought(memory.def);
                        copy.CopyFrom(memory);
                        copy.pawn = dst;
                        dstMemories.Add(copy);
                    }
                    catch (Exception ex)
                    {
                        if (Prefs.DevMode) Log.Warning($"[Pawn Editor] CopyDup thought memory: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Needs: {ex.Message}"); }
    }
}
