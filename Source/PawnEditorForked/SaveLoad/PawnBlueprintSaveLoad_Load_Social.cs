using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Load: Social section.
/// Relations (direct + social memories + reverse memories),
/// WorkPriorities, Inventory, RoyalTitles, Records.
/// </summary>
public static partial class PawnBlueprintSaveLoad
{
    // ── Load: Social Relations ──

    private static void LoadRelations(Pawn pawn, XmlNode root)
    {
        if (pawn.relations == null) return;

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

        // Social memories (opinion values)
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

        // v3d10: Reverse social memories
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

                // v3d9 fix: Skip Time-type records
                if (rd.type == RecordType.Time) continue;

                pawn.records.AddTo(rd, value - pawn.records.GetValue(rd));
            }
        }
        catch (Exception ex) { Warn($"Records: {ex.Message}"); }
    }

    // ── Load: VAspirE Aspirations ──

    private static void LoadAspirations(Pawn pawn, XmlNode root)
    {
        if (!VAspirECompat.Active) return;

        var aspirationsNode = root.SelectSingleNode("aspirations");
        if (aspirationsNode == null) return;

        try
        {
            var count = ParseInt(GetText(aspirationsNode, "count"), 4);
            var listNode = aspirationsNode.SelectSingleNode("list");
            if (listNode == null) return;

            var snapshot = new VAspirECompat.FulfillmentSnapshot();
            snapshot.AspirationCount = count;

            foreach (XmlNode li in listNode.SelectNodes("li"))
            {
                var defName = GetText(li, "defName");
                if (defName.NullOrEmpty()) continue;

                snapshot.AspirationDefNames.Add(defName);

                var completed = ParseBool(GetText(li, "completed"), false);
                if (completed)
                    snapshot.CompletedDefNames.Add(defName);
            }

            if (snapshot.HasData)
            {
                if (!VAspirECompat.TryRestoreSnapshot(pawn, snapshot))
                    Warn("VAspirE aspirations: failed to restore from blueprint");
            }
        }
        catch (Exception ex) { Warn($"Aspirations: {ex.Message}"); }
    }

    // ── Load: VSE Expertise ──

    private static void LoadExpertise(Pawn pawn, XmlNode root)
    {
        if (!VSECompat.Active || !VSECompat.HasExpertiseSupport) return;

        var expertiseNode = root.SelectSingleNode("expertise");
        if (expertiseNode == null) return;

        try
        {
            var snapshots = new List<VSECompat.ExpertiseSnapshot>();

            foreach (XmlNode li in expertiseNode.SelectNodes("li"))
            {
                var defName = GetText(li, "defName");
                if (defName.NullOrEmpty()) continue;

                snapshots.Add(new VSECompat.ExpertiseSnapshot
                {
                    DefName = defName,
                    Level = ParseInt(GetText(li, "level"), 0),
                    XpSinceLastLevel = ParseFloat(GetText(li, "xp"), 0f)
                });
            }

            if (snapshots.Count > 0)
            {
                if (!VSECompat.RestoreExpertise(pawn, snapshots))
                    Warn("VSE expertise: failed to restore from blueprint");
            }
        }
        catch (Exception ex) { Warn($"Expertise: {ex.Message}"); }
    }
}
