using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

// Changing a pawn's developmental stage: what to warn about, and each step of the change.
public partial class TabWorker_Bio_Humanlike
{
    /// <summary>Asks for confirmation if the change would lose data; otherwise applies it straight away.</summary>
    private static void ConfirmAndSetDevStage(Pawn pawn, DevelopmentalStage stage)
    {
        var warnings = pawn.DevelopmentalStage == stage ? new List<string>() : DevStageChangeWarnings(pawn, stage);
        if (warnings.Count == 0)
        {
            SetDevStage(pawn, stage);
            return;
        }

        var message = "PawnEditor.DevStage.WarnHeader".Translate(stage.ToString()) + "\n\n"
                      + string.Join("\n", warnings)
                      + "\n\n" + "PawnEditor.DevStage.WarnFooter".Translate();
        Find.WindowStack.Add(new Dialog_Confirm(message, "DevStageChangeWarning", () => SetDevStage(pawn, stage),
            destructive: stage != DevelopmentalStage.Adult));
    }

    private static List<string> DevStageChangeWarnings(Pawn pawn, DevelopmentalStage stage) =>
        stage == DevelopmentalStage.Adult ? BecomingAdultWarnings(pawn) : LeavingAdulthoodWarnings(pawn);

    private static List<string> BecomingAdultWarnings(Pawn pawn)
    {
        var warnings = new List<string>();
        if (pawn.story?.Adulthood == null)
            warnings.Add("PawnEditor.DevStage.WarnNewAdulthood".Translate());
        warnings.Add("PawnEditor.DevStage.WarnBodyType".Translate());
        return warnings;
    }

    private static List<string> LeavingAdulthoodWarnings(Pawn pawn)
    {
        var warnings = new List<string>();
        if (pawn.story?.Adulthood != null)
            warnings.Add("PawnEditor.DevStage.WarnAdulthood".Translate(pawn.story.Adulthood.TitleCapFor(pawn.gender)));
        if (pawn.relations != null && HasRomanticRelation(pawn))
            warnings.Add("PawnEditor.DevStage.WarnRomance".Translate());
        // Unverified: SetDevStage does not remove parent relations, yet this warns that it will (BUG_LEDGER #26).
        if (pawn.relations != null && pawn.relations.DirectRelations.Any(r => r.def == PawnRelationDefOf.Parent))
            warnings.Add("PawnEditor.DevStage.WarnParents".Translate());
        if (pawn.health?.hediffSet != null && pawn.health.hediffSet.hediffs.Any(h => IsPregnancyOrParasite(h.def)))
            warnings.Add("PawnEditor.DevStage.WarnPregnancy".Translate());
        if (pawn.equipment?.AllEquipmentListForReading?.Any() == true)
            warnings.Add("PawnEditor.DevStage.WarnWeapons".Translate());
        if (HasAspirations(pawn))
            warnings.Add("PawnEditor.DevStage.WarnAspirations".Translate());
        return warnings;
    }

    // Built on each call, not cached in a static field: the DefOf fields are only bound after defs load.
    private static PawnRelationDef[] RomanticRelationDefs() => new[]
    {
        PawnRelationDefOf.Lover, PawnRelationDefOf.Fiance, PawnRelationDefOf.Spouse,
        PawnRelationDefOf.ExLover, PawnRelationDefOf.ExSpouse
    };

    /// <summary>A romantic relation in either direction: the pawn's own, or another pawn's pointing at it.</summary>
    private static bool HasRomanticRelation(Pawn pawn)
    {
        var romantic = RomanticRelationDefs();
        if (pawn.relations.DirectRelations.Any(r => romantic.Contains(r.def))) return true;
        return pawn.relations.PotentiallyRelatedPawns?.Any(other =>
            other?.relations?.DirectRelations?.Any(r => romantic.Contains(r.def) && r.otherPawn == pawn) == true) == true;
    }

    /// <summary>Vanilla pregnancy, plus modded pregnancy, gestation and parasite hediffs, matched by name.</summary>
    private static bool IsPregnancyOrParasite(HediffDef def) =>
        def == HediffDefOf.Pregnant || def == HediffDefOf.PregnantHuman
        || def.defName.Contains("Pregnant") || def.defName.Contains("Gestation")
        || def.defName.Contains("Parasites") || def.defName.Contains("Infestation");

    private static bool HasAspirations(Pawn pawn)
    {
        if (!VAspirECompat.Active) return false;
        var fulfillment = VAspirECompat.GetFulfillmentNeed(pawn);
        return fulfillment != null && VAspirECompat.GetAspirations(fulfillment).Count > 0;
    }

    /// <summary>
    /// Sets the age to the stage's minimum and applies every side effect of the change: apparel,
    /// equipment, body type, backstory, hediffs, relations and VAspirE aspirations.
    /// </summary>
    public static void SetDevStage(Pawn pawn, DevelopmentalStage stage)
    {
        var oldStage = pawn.DevelopmentalStage;
        SetAgeToStageMinimum(pawn, stage);
        // Compared with the REQUESTED stage: on modded races with overlapping life stages, the stage the
        // pawn reports may not follow its new age.
        if (oldStage == stage) return;

        var leavingAdulthood = stage != DevelopmentalStage.Adult;
        pawn.apparel?.DropAllOrMoveAllToInventory(apparel => !apparel.def.apparel.developmentalStageFilter.Has(stage));
        if (leavingAdulthood)
            MoveEquipmentToInventory(pawn);
        pawn.story.bodyType = PawnGenerator.GetBodyTypeFor(pawn);
        UpdateAdulthoodBackstory(pawn, stage);
        if (leavingAdulthood)
        {
            Diagnostics.Run("Removing pregnancy and parasites (developmental stage change)", pawn, () => RemovePregnancyAndParasites(pawn));
            Diagnostics.Run("Removing romantic relations (developmental stage change)", pawn, () => RemoveRomanticRelations(pawn));
        }

        if (VAspirECompat.Active)
            UpdateAspirations(pawn, oldStage, stage);
        RefreshAfterDevStageChange(pawn);
    }

    private static void SetAgeToStageMinimum(Pawn pawn, DevelopmentalStage stage)
    {
        var lifeStage = pawn.RaceProps.lifeStageAges.FirstOrDefault(ls => ls.def.developmentalStage == stage);
        if (lifeStage != null)
            pawn.ageTracker.AgeBiologicalTicks = (long)(lifeStage.minAge * TicksPerYear);
    }

    /// <summary>Children and babies can't hold weapons: they go to the inventory.</summary>
    private static void MoveEquipmentToInventory(Pawn pawn)
    {
        if (pawn.equipment == null) return;
        foreach (var equipment in pawn.equipment.AllEquipmentListForReading.ToList())
        {
            pawn.equipment.Remove(equipment);
            pawn.inventory?.innerContainer?.TryAdd(equipment);
        }
    }

    /// <summary>Becoming adult generates a fitting adulthood backstory; leaving adulthood removes it.</summary>
    private static void UpdateAdulthoodBackstory(Pawn pawn, DevelopmentalStage stage)
    {
        if (stage == DevelopmentalStage.Adult && pawn.story.Adulthood == null)
        {
            pawn.story.Adulthood = BackstoryUtility.GenerateAdultBackstory(pawn);
            Log.Message($"[Pawn Editor] Backstory: assigned={pawn.story.Adulthood?.defName}");
        }
        else if (stage is DevelopmentalStage.Child or DevelopmentalStage.Baby or DevelopmentalStage.Newborn)
            pawn.story.Adulthood = null;
    }

    private static void RemovePregnancyAndParasites(Pawn pawn)
    {
        if (pawn.health?.hediffSet == null) return;
        foreach (var hediff in pawn.health.hediffSet.hediffs.Where(h => IsPregnancyOrParasite(h.def)).ToList())
            pawn.health.RemoveHediff(hediff);
    }

    /// <summary>Removes romantic relations both ways: the pawn's own, and other pawns' pointing at it.</summary>
    private static void RemoveRomanticRelations(Pawn pawn)
    {
        if (pawn.relations == null) return;
        var romantic = RomanticRelationDefs();

        foreach (var relation in pawn.relations.DirectRelations.Where(r => romantic.Contains(r.def)).ToList())
            pawn.relations.RemoveDirectRelation(relation);

        foreach (var other in pawn.relations.PotentiallyRelatedPawns?.ToList() ?? new List<Pawn>())
        {
            if (other?.relations == null) continue;
            foreach (var relation in other.relations.DirectRelations.Where(r => romantic.Contains(r.def) && r.otherPawn == pawn).ToList())
                other.relations.RemoveDirectRelation(relation);
        }
    }

    /// <summary>Children have no aspirations: leaving adulthood clears them; becoming adult rolls new ones.</summary>
    private static void UpdateAspirations(Pawn pawn, DevelopmentalStage oldStage, DevelopmentalStage stage)
    {
        var fulfillment = VAspirECompat.GetFulfillmentNeed(pawn);
        if (fulfillment == null) return;

        if (stage != DevelopmentalStage.Adult)
        {
            VAspirECompat.UncompleteAll(fulfillment);
            foreach (var aspiration in VAspirECompat.GetAspirations(fulfillment).ToList())
                VAspirECompat.RemoveAspiration(fulfillment, aspiration);
        }
        else if (oldStage != DevelopmentalStage.Adult)
            VAspirECompat.ReinitializeAspirations(fulfillment);
    }

    private static void RefreshAfterDevStageChange(Pawn pawn)
    {
        TabWorker_Table<Pawn>.ClearCacheIfBuilt(typeof(TabWorker_Health));
        TabWorker_Table<Pawn>.ClearCacheIfBuilt(typeof(TabWorker_Social));
        TabWorker_Table<Pawn>.ClearCacheIfBuilt(typeof(TabWorker_Needs));
        pawn.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty();
        PawnEditor.Notify_PointsUsed();
        RecacheGraphics(pawn);
        resetAgeBuffers = true;
    }
}
