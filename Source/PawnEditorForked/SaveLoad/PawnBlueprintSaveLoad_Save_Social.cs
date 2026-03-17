using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Save: Social section.
/// Relations (direct + social memories + reverse memories),
/// WorkPriorities, Inventory, RoyalTitles, Records, ModList.
/// </summary>
public static partial class PawnBlueprintSaveLoad
{
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

        // Social memories (opinion values)
        try
        {
            var memories = pawn.needs?.mood?.thoughts?.memories?.Memories;
            if (memories == null) return;

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

        // v3d10: Reverse social memories
        try
        {
            var allPawns = GetAllReachablePawns();
            var reverseMems = new List<(Thought_Memory mem, Pawn source)>();
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

    // ── Save: Mod list ──

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
}
