using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimUI;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

public partial class TabWorker_Bio_Humanlike
{
    private Listing_Horizontal listing = new();
    private float height = 999f;
    private const float margin = 6f;

    public TabWorker_Bio_Humanlike()
    {
        listing.InlineSpacing = 4f;
        listing.BlockSpacing = 8f;
    }

    private void DoButtons(ref Rect buttonsRect, Pawn pawn)
    {
        using var block = new TextBlock(TextAnchor.MiddleCenter);
        var outerRect = buttonsRect.TakeTopPart(height + margin);
        Widgets.DrawHighlight(outerRect);
        buttonsRect = outerRect.ContractedBy(margin);
        listing.Begin(buttonsRect);
        string text;
        if (ModsConfig.BiotechActive)
        {
            text = pawn.DevelopmentalStage.ToString().Translate().CapitalizeFirst();
            // if (Widgets.ButtonImageWithBG(devStageRect.TakeTopPart(UIUtility.RegularButtonHeight), pawn.DevelopmentalStage.Icon().Texture, new Vector2(22f, 22f)))
            if (listing.ButtonImageLabeledVStack(text, pawn.DevelopmentalStage.Icon().Texture, 6, text.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + "DevelopmentalAgeSelectionDesc".Translate()))
            {
                // v3d10: Show developmental stages this race supports.
                var options = new List<FloatMenuOption>();
                var raceStages = pawn.RaceProps.lifeStageAges;

                if (raceStages.Any(ls => ls.def.developmentalStage == DevelopmentalStage.Adult))
                    options.Add(new("Adult".Translate().CapitalizeFirst(),
                        () => ConfirmAndSetDevStage(pawn, DevelopmentalStage.Adult),
                        DevelopmentalStageExtensions.AdultTex.Texture, Color.white));
                if (raceStages.Any(ls => ls.def.developmentalStage == DevelopmentalStage.Child))
                    options.Add(new("Child".Translate().CapitalizeFirst(),
                        () => ConfirmAndSetDevStage(pawn, DevelopmentalStage.Child),
                        DevelopmentalStageExtensions.ChildTex.Texture, Color.white));
                if (raceStages.Any(ls => ls.def.developmentalStage == DevelopmentalStage.Baby))
                    options.Add(new("Baby".Translate().CapitalizeFirst(),
                        () => ConfirmAndSetDevStage(pawn, DevelopmentalStage.Baby),
                        DevelopmentalStageExtensions.BabyTex.Texture, Color.white));
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        if (ModsConfig.BiotechActive)
        {
            text = pawn.genes.XenotypeLabelCap;
            if (listing.ButtonImageLabeledVStack(text, pawn.genes.XenotypeIcon, 6, text.Colorize(ColoredText.TipSectionTitleColor) + "\n\n" + "XenotypeSelectionDesc".Translate()))
            {
                Find.WindowStack.Add(new ListingMenu_Xenotypes(pawn, xenotype =>
                {
                    // If pawn already has genes, ask whether to reset
                    var hasExistingGenes = pawn.genes.GenesListForReading.Count > 0;
                    if (hasExistingGenes && xenotype != pawn.genes.Xenotype)
                    {
                        Find.WindowStack.Add(new Dialog_Confirm(
                            "PawnEditor.XenotypeChangeWarning".Translate(pawn.genes.XenotypeLabelCap, xenotype.LabelCap)
                            + "\n\n" + "PawnEditor.ResetGenesToBase".Translate(),
                            "XenotypeChangeConfirm",
                            () =>
                            {
                                // Full reset: clear ALL genes, then apply new xenotype's genes
                                ResetAndSetXenotype(pawn, xenotype);
                                RecacheGraphics(pawn);
                            },
                            destructive: true));
                    }
                    else
                    {
                        SetXenotype(pawn, xenotype);
                        RecacheGraphics(pawn);
                    }
                }));
            }
        }

        if (listing.ButtonImageLabeledVStack("PawnEditor.Sex".Translate(), pawn.gender.GetIcon(), 6)
            && pawn.kindDef.fixedGender == null
            && pawn.RaceProps.hasGenders)
        {
            var list = new List<FloatMenuOption>
            {
                new("Female".Translate().CapitalizeFirst(), () => SetGender(pawn, Gender.Female), GenderUtility.FemaleIcon,
                    Color.white),
                new("Male".Translate().CapitalizeFirst(), () => SetGender(pawn, Gender.Male), GenderUtility.MaleIcon, Color.white)
            };

            Find.WindowStack.Add(new FloatMenu(list));
        }

        if (listing.ButtonImageLabeledVStack("PawnEditor.Shape".Translate(), TexPawnEditor.GetBodyTypeIcon(pawn.story.bodyType), 6))
            Find.WindowStack.Add(new FloatMenu(DefDatabase<BodyTypeDef>.AllDefs.Where(bodyType => pawn.DevelopmentalStage switch
                {
                    DevelopmentalStage.Baby or DevelopmentalStage.Newborn => bodyType == BodyTypeDefOf.Baby,
                    DevelopmentalStage.Child => bodyType == BodyTypeDefOf.Child,
                    DevelopmentalStage.Adult => bodyType != BodyTypeDefOf.Baby && bodyType != BodyTypeDefOf.Child,
                    _ => true
                })
                .Select(bodyType => new FloatMenuOption(bodyType.defName.CapitalizeFirst(), () =>
                {
                    pawn.story.bodyType = bodyType;
                    RecacheGraphics(pawn);
                }, TexPawnEditor.GetBodyTypeIcon(bodyType), Color.white))
                .ToList()));

        if (listing.ButtonText("PawnEditor.EditAppearance".Translate(), 6))
            Find.WindowStack.Add(new Dialog_AppearanceEditor(pawn));

        listing.End();
        height = listing.curHeight;
    }

    /// <summary>
    /// Shows a confirmation dialog before changing developmental stage if the transition
    /// would cause data loss (backstory, relations, aspirations, equipment).
    /// If no data would be lost, applies the change immediately.
    /// </summary>
    private static void ConfirmAndSetDevStage(Pawn pawn, DevelopmentalStage stage)
    {
        // Same stage = no transition, just refresh
        if (pawn.DevelopmentalStage == stage)
        {
            SetDevStage(pawn, stage);
            return;
        }

        // Check if there's anything to warn about
        var warnings = new List<string>();

        if (stage != DevelopmentalStage.Adult)
        {
            // Going TO child/baby — check what will be lost
            if (pawn.story?.Adulthood != null)
                warnings.Add("- Remove adulthood backstory (" + pawn.story.Adulthood.TitleCapFor(pawn.gender) + ")");

            var romanticDefs = new[] { PawnRelationDefOf.Lover, PawnRelationDefOf.Fiance, PawnRelationDefOf.Spouse,
                                       PawnRelationDefOf.ExLover, PawnRelationDefOf.ExSpouse };
            if (pawn.relations != null)
            {
                bool hasRomantic = pawn.relations.DirectRelations.Any(r => romanticDefs.Contains(r.def));
                // Also check reverse (others pointing to this pawn)
                if (!hasRomantic)
                    hasRomantic = pawn.relations.PotentiallyRelatedPawns?.Any(other =>
                        other?.relations?.DirectRelations?.Any(r => romanticDefs.Contains(r.def) && r.otherPawn == pawn) == true) == true;
                if (hasRomantic)
                    warnings.Add("- Remove romantic relations (Spouse, Lover, Fianc\u00e9e, Ex)");

                bool hasParent = pawn.relations.DirectRelations.Any(r => r.def == PawnRelationDefOf.Parent);
                if (hasParent)
                    warnings.Add("- Remove parent relations");
            }

            if (pawn.health?.hediffSet != null)
            {
                bool hasPregnancy = pawn.health.hediffSet.hediffs.Any(h =>
                    h.def == HediffDefOf.Pregnant || h.def == HediffDefOf.PregnantHuman ||
                    h.def.defName.Contains("Pregnant") || h.def.defName.Contains("Gestation") ||
                    h.def.defName.Contains("Parasites") || h.def.defName.Contains("Infestation"));
                if (hasPregnancy)
                    warnings.Add("- Remove pregnancy / parasitic infestation");
            }

            if (pawn.equipment?.AllEquipmentListForReading?.Any() == true)
                warnings.Add("- Move weapons to inventory");

            // VAspirE: warn about losing aspirations
            if (VAspirECompat.Active)
            {
                var fulfillment = VAspirECompat.GetFulfillmentNeed(pawn);
                if (fulfillment != null && VAspirECompat.GetAspirations(fulfillment).Count > 0)
                    warnings.Add("- Remove all aspirations (children cannot have them)");
            }
        }
        else
        {
            // Going TO adult
            if (pawn.story?.Adulthood == null)
                warnings.Add("- Generate a random adulthood backstory");
            warnings.Add("- Change body type to adult");
        }

        // No warnings = nothing to lose, just do it
        if (warnings.Count == 0)
        {
            SetDevStage(pawn, stage);
            return;
        }

        // Build warning message
        string warning = "Changing to " + stage.ToString() + " will:\n\n"
            + string.Join("\n", warnings)
            + "\n\nThis cannot be undone. Continue?";

        Find.WindowStack.Add(new Dialog_Confirm(
            warning,
            "DevStageChangeWarning",
            () => SetDevStage(pawn, stage),
            destructive: stage != DevelopmentalStage.Adult
        ));
    }

    /// <summary>
    /// Applies a developmental stage change to a pawn, handling all side effects:
    /// age, body type, backstory, apparel, equipment, hediffs, relations, and VAspirE aspirations.
    /// Called after user confirms via ConfirmAndSetDevStage.
    /// </summary>
    public static void SetDevStage(Pawn pawn, DevelopmentalStage stage)
    {
        var lifeStage = pawn.RaceProps.lifeStageAges.FirstOrDefault(lifeStage => lifeStage.def.developmentalStage == stage);
        var oldStage = pawn.DevelopmentalStage;

        if (lifeStage != null)
        {
            pawn.ageTracker.AgeBiologicalTicks = (long)(lifeStage.minAge * 3600000L);
        }

        var actualStage = pawn.DevelopmentalStage;

        // If stage didn't change (can happen with modded races where life stage ages overlap),
        // compare against the REQUESTED stage, not what pawn.DevelopmentalStage reports
        if (oldStage != stage)
        {
            // ── Apparel: drop incompatible clothing to inventory ──
            pawn.apparel?.DropAllOrMoveAllToInventory(apparel => !apparel.def.apparel.developmentalStageFilter.Has(stage));

            // ── Equipment: children/babies can't hold weapons ──
            if (stage != DevelopmentalStage.Adult && pawn.equipment != null)
            {
                foreach (var eq in pawn.equipment.AllEquipmentListForReading.ToList())
                {
                    pawn.equipment.Remove(eq);
                    pawn.inventory?.innerContainer?.TryAdd(eq);
                }
            }

            // ── Body type ──
            pawn.story.bodyType = PawnGenerator.GetBodyTypeFor(pawn);

            // ── Backstory transitions ──
            if (stage == DevelopmentalStage.Adult && pawn.story.Adulthood == null)
            {
                // Going TO adult: generate a contextual adulthood backstory.
                pawn.story.Adulthood = BackstoryUtility.GenerateAdultBackstory(pawn);
                Log.Message($"[Pawn Editor] Backstory: assigned={pawn.story.Adulthood?.defName}");
            }
            else if (stage == DevelopmentalStage.Child || stage == DevelopmentalStage.Baby || stage == DevelopmentalStage.Newborn)
            {
                pawn.story.Adulthood = null;
            }

            // ── Hediffs: remove pregnancy for non-adults ──
            if (stage != DevelopmentalStage.Adult && pawn.health?.hediffSet != null)
            {
                try
                {
                    var hediffsToRemove = pawn.health.hediffSet.hediffs
                        .Where(h =>
                            h.def == HediffDefOf.Pregnant ||
                            h.def == HediffDefOf.PregnantHuman ||
                            h.def.defName.Contains("Pregnant") ||
                            h.def.defName.Contains("Gestation") ||
                            h.def.defName.Contains("Parasites") ||
                            h.def.defName.Contains("Infestation"))
                        .ToList();
                    foreach (var h in hediffsToRemove)
                        pawn.health.RemoveHediff(h);
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[Pawn Editor] SetDevStage hediff cleanup failed: {ex}");
                }
            }

            // ── Relations: remove inappropriate relations for non-adults ──
            if (stage != DevelopmentalStage.Adult && pawn.relations != null)
            {
                try
                {
                    var defsToRemove = new[]
                    {
                        PawnRelationDefOf.Lover, PawnRelationDefOf.Fiance, PawnRelationDefOf.Spouse,
                        PawnRelationDefOf.ExLover, PawnRelationDefOf.ExSpouse
                    };

                    var ownedRels = pawn.relations.DirectRelations
                        .Where(r => defsToRemove.Contains(r.def)).ToList();
                    foreach (var rel in ownedRels)
                        pawn.relations.RemoveDirectRelation(rel);

                    // Remove relations owned by OTHER pawns pointing TO this pawn
                    var otherPawns = pawn.relations.PotentiallyRelatedPawns?.ToList();
                    if (otherPawns != null)
                    {
                        foreach (var other in otherPawns)
                        {
                            if (other?.relations == null) continue;
                            var reverseRels = other.relations.DirectRelations
                                .Where(r => defsToRemove.Contains(r.def) && r.otherPawn == pawn).ToList();
                            if (reverseRels.Any())
                            {
                                foreach (var rel in reverseRels)
                                    other.relations.RemoveDirectRelation(rel);
                            }
                        }
                    }

                    // Verify cleanup
                    var remaining = pawn.relations.DirectRelations
                        .Where(r => defsToRemove.Contains(r.def)).Count();
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[Pawn Editor] SetDevStage relation cleanup failed: {ex}");
                }
            }

            // ── VAspirE: handle aspirations on life stage change ──
            if (VAspirECompat.Active)
            {
                var fulfillment = VAspirECompat.GetFulfillmentNeed(pawn);
                if (stage != DevelopmentalStage.Adult)
                {
                    // Going TO child/baby — clear all aspirations (children don't have them)
                    if (fulfillment != null)
                    {
                        VAspirECompat.UncompleteAll(fulfillment);
                        var aspirations = VAspirECompat.GetAspirations(fulfillment);
                        foreach (var asp in aspirations.ToList())
                            VAspirECompat.RemoveAspiration(fulfillment, asp);
                    }
                }
                else if (oldStage != DevelopmentalStage.Adult)
                {
                    // Going FROM child/baby TO adult — generate fresh aspirations
                    if (fulfillment != null)
                    {
                        VAspirECompat.ReinitializeAspirations(fulfillment);
                    }
                }
            }

            // ── Notify editor UI to refresh cached data ──
            try
            {
                TabWorker_Table<Pawn>.ClearCacheFor<TabWorker_Health>();
            }
            catch { /* Tab not yet initialized */ }
            try
            {
                TabWorker_Table<Pawn>.ClearCacheFor<TabWorker_Social>();
            }
            catch { /* Tab not yet initialized */ }
            try
            {
                TabWorker_Table<Pawn>.ClearCacheFor<TabWorker_Needs>();
            }
            catch { /* Tab not yet initialized */ }

            pawn.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty();
            PawnEditor.Notify_PointsUsed();

            RecacheGraphics(pawn);
            resetAgeBuffers = true;
        }
    }

    /// <summary>
    /// Sets the pawn's gender and adjusts body type and head type to match.
    /// </summary>
    public static void SetGender(Pawn pawn, Gender gender)
    {
        pawn.gender = gender;
        if (pawn.story.bodyType == BodyTypeDefOf.Female && gender == Gender.Male) pawn.story.bodyType = BodyTypeDefOf.Male;
        if (pawn.story.bodyType == BodyTypeDefOf.Male && gender == Gender.Female) pawn.story.bodyType = BodyTypeDefOf.Female;

        // HAR doesn't like head types not matching genders, so make sure to fix that
        if (HARCompat.Active && pawn.story.headType.gender != gender
                             && !pawn.story.TryGetRandomHeadFromSet(HARCompat.FilterHeadTypes(DefDatabase<HeadTypeDef>.AllDefs, pawn)))
            Log.Warning("Failed to find head type for " + pawn);

        RecacheGraphics(pawn);
    }

    /// <summary>
    /// Marks the pawn's graphics as dirty so the portrait re-renders.
    /// Called after any visual change (genes, body type, hair, tattoos, etc.).
    ///
    /// Now delegates to the centralized PawnEditor.RefreshPawnGraphics (the canonical, deferred,
    /// fully-guarded refresh). Kept as a thin wrapper so the many existing callers
    /// (SetGender, SetDevStage, SetXenotype, the whole appearance editor) don't all need editing.
    /// Behavior change vs the old inline body: PortraitsCache.SetDirty now runs for ALL pawns (not
    /// just colonists) and the pawn's MAP sprite is refreshed too via GlobalTextureAtlasManager.
    /// </summary>
    public static void RecacheGraphics(Pawn pawn)
    {
        PawnEditor.RefreshPawnGraphics(pawn);
    }

    /// <summary>
    /// Removes all genes belonging to the pawn's current xenotype (both endo and xeno).
    /// Does not touch genes from other sources.
    /// </summary>
    private static void ClearXenotype(Pawn pawn)
    {
        if (pawn.genes == null) return;

        if (pawn.genes.xenotype != null)
            foreach (var xenotypeGene in pawn.genes.xenotype.genes.ToList())
            {
                var gene = (pawn.genes.xenotype.inheritable ? pawn.genes.Endogenes : pawn.genes.Xenogenes)?.FirstOrDefault(g => g.def == xenotypeGene);
                if (gene != null) pawn.genes.RemoveGene(gene);
            }

        if (pawn.genes.CustomXenotype is { } customXenotype)
            foreach (var xenotypeGene in customXenotype.genes.ToList())
            {
                var gene = (customXenotype.inheritable ? pawn.genes.Endogenes : pawn.genes.Xenogenes)?.FirstOrDefault(g => g.def == xenotypeGene);
                if (gene != null) pawn.genes.RemoveGene(gene);
            }
    }

    /// <summary>
    /// Sets a pawn's xenotype by removing the old xenotype's genes and adding the new ones.
    /// Only removes genes that belonged to the previous xenotype — preserves other genes.
    /// </summary>
    public static void SetXenotype(Pawn pawn, XenotypeDef xenotype)
    {
        if (pawn.genes == null) return;
        ClearXenotype(pawn);
        foreach (var gene in xenotype.genes)
            pawn.genes.AddGene(gene, !xenotype.inheritable);

        pawn.genes.SetXenotypeDirect(xenotype);
    }

    /// <summary>
    /// Full xenotype reset: removes ALL genes (endogenes + xenogenes), then applies
    /// only the new xenotype's genes. Use when switching between fundamentally different
    /// xenotypes (e.g. human → android) to avoid leftover genes from the previous type.
    /// </summary>
    public static void ResetAndSetXenotype(Pawn pawn, XenotypeDef xenotype)
    {
        if (pawn.genes == null) return;

        // Remove all endogenes
        foreach (var gene in pawn.genes.Endogenes.ToList())
        {
            try { pawn.genes.RemoveGene(gene); }
            catch (System.Exception ex) { Log.Warning($"[Pawn Editor] Failed to remove endogene {gene.def.defName}: {ex.Message}"); }
        }

        // Remove all xenogenes
        foreach (var gene in pawn.genes.Xenogenes.ToList())
        {
            try { pawn.genes.RemoveGene(gene); }
            catch (System.Exception ex) { Log.Warning($"[Pawn Editor] Failed to remove xenogene {gene.def.defName}: {ex.Message}"); }
        }

        // Apply new xenotype genes
        foreach (var gene in xenotype.genes)
            pawn.genes.AddGene(gene, !xenotype.inheritable);

        pawn.genes.SetXenotypeDirect(xenotype);
        PawnEditor.Notify_PointsUsed();
    }

    /// <summary>
    /// Sets a pawn's custom xenotype by removing the old xenotype's genes and adding the new ones.
    /// </summary>
    public static void SetXenotype(Pawn pawn, CustomXenotype xenotype)
    {
        if (pawn.genes == null || xenotype == null) return;
        ClearXenotype(pawn);
        pawn.genes.xenotypeName = xenotype.name;
        pawn.genes.iconDef = xenotype.IconDef;
        foreach (var geneDef in xenotype.genes) pawn.genes.AddGene(geneDef, !xenotype.inheritable);
    }
}