using System;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Social and Progress copy methods.
/// Covers everything that describes the pawn's place in the world:
/// social relations (3-pass), work priorities, royal titles, records, inventory.
/// </summary>
public static partial class PawnEditor
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Social Relations — 3-pass system
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies social relations from src to dst using three complementary passes.
    ///
    /// Pass 1 — DirectRelation definitions (bidirectional).
    ///   Uses rel.def.AddDirectRelation (the extension/Worker method in RelationUtilities),
    ///   NOT the native pawn.relations.AddDirectRelation. The extension version sets the
    ///   relation on BOTH sides so the other pawn also shows the clone as a relation.
    ///
    /// Pass 2 — src's own social memories toward others (ISocialThought).
    ///   These drive the actual opinion numbers in the Social tab.
    ///   Skips pawns that already have a DirectRelation with dst (Pass 1) to prevent
    ///   the Social tab showing two separate rows for the same pawn.
    ///
    /// Pass 3 — Hybrid: transfer POSITIVE memories OTHER pawns have about src, to dst.
    ///   baseMoodEffect > 0  → copy (goodwill the clone inherits from the original).
    ///   baseMoodEffect &lt;= 0 → skip (fights, betrayals — the clone never did those).
    /// </summary>
    private static void CopyDup_Relations(Pawn src, Pawn dst)
    {
        if (src.relations == null || dst.relations == null) return;

        // ── Pass 1: DirectRelation definitions — bidirectional ──
        try
        {
            foreach (var rel in src.relations.DirectRelations.ToList())
            {
                if (rel.def == null || rel.otherPawn == null) continue;
                if (rel.otherPawn == src) continue;
                if (!dst.relations.DirectRelationExists(rel.def, rel.otherPawn))
                {
                    try { rel.def.AddDirectRelation(dst, rel.otherPawn); }
                    catch (Exception ex)
                    {
                        // Log per-relation failures so we can diagnose issues with
                        // world pawns, factionless pawns, etc. without crashing the whole pass
                        Log.Warning($"[Pawn Editor] CopyDup_Relations skip {rel.def.defName}→{rel.otherPawn.LabelShort}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Relations (direct): {ex.Message}"); }

        // ── Pass 2: dst's own social memories toward others ──
        try
        {
            var srcMems = src.needs?.mood?.thoughts?.memories;
            var dstMems = dst.needs?.mood?.thoughts?.memories;
            if (srcMems != null && dstMems != null)
            {
                foreach (var mem in srcMems.Memories.ToList())
                {
                    if (!(mem is Thought_Memory memBase)) continue;
                    if (!(mem is ISocialThought socialThought)) continue;
                    var otherPawnRef = socialThought.OtherPawn();
                    if (otherPawnRef == null || otherPawnRef == src) continue;
                    if (memBase.def == null) continue;

                    // Skip pawns that already have a DirectRelation with dst.
                    // Adding memories on top would cause two rows for the same pawn
                    // in the Social tab (e.g. Friend + Acquaintance).
                    if (dst.relations?.DirectRelations?.Any(r => r.otherPawn == otherPawnRef) == true) continue;

                    try
                    {
                        var newMem = ThoughtMaker.MakeThought(memBase.def, memBase.CurStageIndex) as Thought_Memory;
                        if (newMem == null || !(newMem is ISocialThought)) continue;
                        newMem.age = memBase.age;
                        dstMems.TryGainMemory(newMem, otherPawnRef);
                    }
                    catch (Exception ex)
                    {
                        if (Prefs.DevMode) Log.Warning($"[Pawn Editor] CopyDup own memory skip: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Relations (own memories): {ex.Message}"); }

        // ── Pass 3: Hybrid — positive memories others have about src, copied to dst ──
        try
        {
            var allPawns = PawnBlueprintSaveLoad.GetAllReachablePawnsPublic();
            foreach (var other in allPawns)
            {
                if (other == src || other == dst) continue;
                var otherMems = other.needs?.mood?.thoughts?.memories;
                if (otherMems == null) continue;

                foreach (var mem in otherMems.Memories.ToList())
                {
                    if (!(mem is Thought_Memory memBase)) continue;
                    if (!(mem is ISocialThought socialThought)) continue;
                    if (socialThought.OtherPawn() != src) continue;
                    if (memBase.def == null) continue;

                    // Only positive impressions — the clone inherits goodwill, not grudges
                    var stage = memBase.CurStage;
                    if (stage == null || stage.baseMoodEffect <= 0) continue;

                    try
                    {
                        var newMem = ThoughtMaker.MakeThought(memBase.def, memBase.CurStageIndex) as Thought_Memory;
                        if (newMem == null || !(newMem is ISocialThought)) continue;
                        newMem.age = memBase.age;
                        otherMems.TryGainMemory(newMem, dst);
                    }
                    catch (Exception ex)
                    {
                        if (Prefs.DevMode) Log.Warning($"[Pawn Editor] CopyDup hybrid memory skip: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Relations (hybrid pass): {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Work Priorities
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies work priority settings for all work types.
    /// Skips work types that are disabled for either pawn (trait/gene/hediff restrictions).
    /// </summary>
    private static void CopyDup_WorkPriorities(Pawn src, Pawn dst)
    {
        if (src.workSettings == null || dst.workSettings == null) return;
        try
        {
            dst.workSettings.EnableAndInitialize();
            foreach (var wd in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (src.WorkTypeIsDisabled(wd) || dst.WorkTypeIsDisabled(wd)) continue;
                dst.workSettings.SetPriority(wd, src.workSettings.GetPriority(wd));
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_WorkPriorities: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Royal Titles (Royalty DLC)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies psylink level, royal titles and favor for all factions.
    /// No-op if the Royalty DLC is not active.
    /// </summary>
    private static void CopyDup_RoyalTitles(Pawn src, Pawn dst)
    {
        if (!ModsConfig.RoyaltyActive) return;
        if (src.royalty == null || dst.royalty == null) return;
        try
        {
            // Psylink level
            var srcLevel = src.GetPsylinkLevel();
            var dstLevel = dst.GetPsylinkLevel();
            for (int i = dstLevel; i < srcLevel; i++)
                dst.ChangePsylinkLevel(1);

            // Titles per faction
            foreach (var title in src.royalty.AllTitlesForReading)
            {
                if (title?.def == null || title.faction == null) continue;
                dst.royalty.SetTitle(title.faction, title.def, false);
            }

            // Favor per faction
            foreach (var faction in Find.FactionManager.AllFactions)
            {
                var favor = src.royalty.GetFavor(faction);
                if (favor > 0) dst.royalty.SetFavor(faction, favor);
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_RoyalTitles: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Records
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies all pawn records (time spent on tasks, kills, etc.).
    /// Uses AddTo with the delta rather than setting directly, to be safe with
    /// records that enforce minimum values.
    /// </summary>
    private static void CopyDup_Records(Pawn src, Pawn dst)
    {
        if (src.records == null || dst.records == null) return;
        try
        {
            foreach (var rd in DefDatabase<RecordDef>.AllDefsListForReading)
            {
                var val = src.records.GetValue(rd);
                if (val != 0f)
                    dst.records.AddTo(rd, val - dst.records.GetValue(rd));
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Records: {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Inventory
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies all items from the pawn's personal inventory (not apparel or weapons —
    /// those are handled by CopyDup_Apparel). Preserves stack count and quality.
    /// </summary>
    private static void CopyDup_Inventory(Pawn src, Pawn dst)
    {
        if (src.inventory?.innerContainer == null || dst.inventory?.innerContainer == null) return;
        try
        {
            dst.inventory.innerContainer.ClearAndDestroyContents();
            foreach (var thing in src.inventory.innerContainer)
            {
                if (thing?.def == null) continue;
                try
                {
                    var copy = thing.Stuff != null
                        ? ThingMaker.MakeThing(thing.def, thing.Stuff)
                        : ThingMaker.MakeThing(thing.def);
                    copy.stackCount = thing.stackCount;

                    if (thing.TryGetComp<CompQuality>() is { } srcQ && copy.TryGetComp<CompQuality>() is { } dstQ)
                        dstQ.SetQuality(srcQ.Quality, ArtGenerationContext.Outsider);

                    dst.inventory.innerContainer.TryAdd(copy);
                }
                catch (Exception ex)
                {
                    if (Prefs.DevMode) Log.Warning($"[Pawn Editor] CopyDup inventory item: {ex.Message}");
                }
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Inventory: {ex.Message}"); }
    }
}
