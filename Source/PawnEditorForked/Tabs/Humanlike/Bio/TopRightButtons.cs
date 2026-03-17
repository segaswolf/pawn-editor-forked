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
                var list = new List<FloatMenuOption>();
                foreach (var item in DefDatabase<XenotypeDef>.AllDefs.OrderBy(x => 0f - x.displayPriority))
                {
                    if (HARCompat.Active && HARCompat.EnforceRestrictions && !HARCompat.CanUseXenotype(item, pawn))
                        continue;
                    var xenotype = item;
                    list.Add(new(xenotype.LabelCap,
                        () =>
                        {
                            SetXenotype(pawn, xenotype);
                            PawnEditor.Notify_PointsUsed();
                        }, xenotype.Icon, XenotypeDef.IconColor, MenuOptionPriority.Default,
                        r => TooltipHandler.TipRegion(r, xenotype.descriptionShort ?? xenotype.description), null, 24f,
                        r => Widgets.InfoCardButton(r.x, r.y + 3f, xenotype), extraPartRightJustified: true));
                }

                foreach (var customXenotype in CharacterCardUtility.CustomXenotypes)
                {
                    var customInner = customXenotype;
                    list.Add(new(customInner.name.CapitalizeFirst() + " (" + "Custom".Translate() + ")",
                        delegate
                        {
                            SetXenotype(pawn, customInner);
                            PawnEditor.Notify_PointsUsed();
                        }, customInner.IconDef.Icon, XenotypeDef.IconColor, MenuOptionPriority.Default, null, null, 24f, delegate(Rect r)
                        {
                            if (Widgets.ButtonImage(new(r.x, r.y + (r.height - r.width) / 2f, r.width, r.width), TexButton.Delete, GUI.color))
                            {
                                Find.WindowStack.Add(new Dialog_Confirm("ConfirmDelete".Translate(customInner.name.CapitalizeFirst()), "ConfirmDeleteXenotype",
                                    delegate
                                    {
                                        var path = GenFilePaths.AbsFilePathForXenotype(customInner.name);
                                        if (File.Exists(path))
                                        {
                                            File.Delete(path);
                                            CharacterCardUtility.cachedCustomXenotypes = null;
                                        }
                                    }, true));
                                return true;
                            }

                            return false;
                        }, extraPartRightJustified: true));
                }

                list.Add(new("XenotypeEditor".Translate() + "...",
                    delegate
                    {
                        var index = PawnEditor.Pregame ? StartingPawnUtility.PawnIndex(pawn) : CharacterCardUtility.CustomXenotypes.Count;
                        Find.WindowStack.Add(new Dialog_CreateXenotype(index, delegate
                        {
                            CharacterCardUtility.cachedCustomXenotypes = null;
                            SetXenotype(pawn, StartingPawnUtility.GetGenerationRequest(index).ForcedCustomXenotype);
                        }));
                    }));

                Find.WindowStack.Add(new FloatMenu(list));
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

        if (listing.ButtonImageLabeledVStack("PawnEditor.Shape".Translate(), TexPawnEditor.BodyTypeIcons[pawn.story.bodyType], 6))
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
                }, TexPawnEditor.BodyTypeIcons[bodyType], Color.white))
                .ToList()));

        if (listing.ButtonText("PawnEditor.EditAppearance".Translate(), 6))
            Find.WindowStack.Add(new Dialog_AppearanceEditor(pawn));

        listing.End();
        height = listing.curHeight;
    }

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

    public static void SetDevStage(Pawn pawn, DevelopmentalStage stage)
    {
        var lifeStage = pawn.RaceProps.lifeStageAges.FirstOrDefault(lifeStage => lifeStage.def.developmentalStage == stage);
        var oldStage = pawn.DevelopmentalStage;
        Log.Message($"[Pawn Editor] SetDevStage: {pawn.Name} {oldStage}->{stage}");

        if (lifeStage != null)
        {
            var num = lifeStage.minAge;
            pawn.ageTracker.AgeBiologicalTicks = (long)(num * 3600000L);
        }

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
                var allAdult = DefDatabase<BackstoryDef>.AllDefsListForReading
                    .Where(bs => bs.slot == BackstorySlot.Adulthood && bs.shuffleable).ToList();

                var childCategories = pawn.story.Childhood?.spawnCategories;
                Log.Message($"[Pawn Editor] Backstory: childhood={pawn.story.Childhood?.defName}, categories={string.Join(",", childCategories ?? new List<string>())}, allAdult count={allAdult.Count}");

                if (childCategories != null && childCategories.Any())
                {
                    var matched = allAdult
                        .Where(bs => bs.spawnCategories.Any(sc => childCategories.Contains(sc)))
                        .ToList();
                    Log.Message($"[Pawn Editor] Backstory: matched {matched.Count} adult backstories for categories [{string.Join(",", childCategories)}]");
                    if (matched.Any())
                        pawn.story.Adulthood = matched.RandomElement();
                    else if (allAdult.Any())
                        pawn.story.Adulthood = allAdult.RandomElement();
                }
                else if (allAdult.Any())
                {
                    pawn.story.Adulthood = allAdult.RandomElement();
                }
                Log.Message($"[Pawn Editor] Backstory: assigned={pawn.story.Adulthood?.defName} title={pawn.story.Adulthood?.TitleCapFor(pawn.gender)}");
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
                    Log.Message($"[Pawn Editor] Hediffs to remove: {hediffsToRemove.Count} ({string.Join(", ", hediffsToRemove.Select(h => h.def.defName))})");
                    foreach (var h in hediffsToRemove)
                        pawn.health.RemoveHediff(h);
                    Log.Message($"[Pawn Editor] Hediffs after removal: pregnancy count={pawn.health.hediffSet.hediffs.Count(h => h.def.defName.Contains("Pregnant"))}");
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

                    // Remove relations owned BY this pawn
                    var ownedRels = pawn.relations.DirectRelations
                        .Where(r => defsToRemove.Contains(r.def)).ToList();
                    Log.Message($"[Pawn Editor] Own romantic rels: {ownedRels.Count} ({string.Join(", ", ownedRels.Select(r => r.def.defName + "->" + r.otherPawn?.Name))})");
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
                                Log.Message($"[Pawn Editor] Reverse rels from {other.Name}: {string.Join(", ", reverseRels.Select(r => r.def.defName))}");
                                foreach (var rel in reverseRels)
                                    other.relations.RemoveDirectRelation(rel);
                            }
                        }
                    }

                    // Verify
                    var remaining = pawn.relations.DirectRelations
                        .Where(r => defsToRemove.Contains(r.def)).ToList();
                    Log.Message($"[Pawn Editor] Romantic rels remaining after cleanup: {remaining.Count}");
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[Pawn Editor] SetDevStage relation cleanup failed: {ex}");
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

    public static void RecacheGraphics(Pawn pawn)
    {
        LongEventHandler.ExecuteWhenFinished(delegate
        {
            pawn.drawer.renderer.SetAllGraphicsDirty();
            if (pawn.IsColonist) PortraitsCache.SetDirty(pawn);
        });
    }

    // FIX #010: Null checks + snapshot gene lists to prevent NRE and collection-modified errors.
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

    public static void SetXenotype(Pawn pawn, XenotypeDef xenotype)
    {
        if (pawn.genes == null) return;
        ClearXenotype(pawn);
        foreach (var gene in xenotype.genes)
            pawn.genes.AddGene(gene, !xenotype.inheritable);

        pawn.genes.SetXenotypeDirect(xenotype);
    }

    public static void SetXenotype(Pawn pawn, CustomXenotype xenotype)
    {
        if (pawn.genes == null || xenotype == null) return;
        ClearXenotype(pawn);
        pawn.genes.xenotypeName = xenotype.name;
        pawn.genes.iconDef = xenotype.IconDef;
        foreach (var geneDef in xenotype.genes) pawn.genes.AddGene(geneDef, !xenotype.inheritable);
    }
}