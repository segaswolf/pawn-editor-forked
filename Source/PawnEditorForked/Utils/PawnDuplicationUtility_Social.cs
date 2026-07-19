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
    ///   Copies for ALL pawns, including those with DirectRelations — the Social tab
    ///   groups entries by pawn (SocialCardUtility.cachedEntries), so memories on top
    ///   of a DirectRelation don't create duplicate rows; they add to the opinion total.
    ///
    /// Pass 3 — Hybrid: transfer POSITIVE opinion memories OTHER pawns have about src, to dst.
    ///   baseOpinionOffset > 0  → copy (goodwill the clone inherits from the original).
    ///   baseOpinionOffset &lt;= 0 → skip (fights, insults — the clone never did those).
    ///   NOTE: We check baseOpinionOffset, NOT baseMoodEffect. Social interaction memories
    ///   (chatted, joked, played) carry their opinion impact in baseOpinionOffset while
    ///   baseMoodEffect is typically 0 for social thoughts.
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
        // v3d9 fix: No longer skips pawns with DirectRelations.
        // The Social tab groups by pawn (SocialCardUtility.cachedEntries) so memories
        // on top of a DirectRelation (Mother, Sister, etc.) just increase the opinion
        // total — they don't create duplicate rows. Previously, skipping these pawns
        // meant the clone had only the raw relation bonus (e.g. Mother +30) without
        // the accumulated social memories that brought the original to +100.
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

                    // OLD (v3d8): skipped pawns with DirectRelation — removed in v3d9
                    // if (dst.relations?.DirectRelations?.Any(r => r.otherPawn == otherPawnRef) == true) continue;

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
        // v3d9 fix: Filter on baseOpinionOffset instead of baseMoodEffect.
        // Social interaction memories (chatted, joked, played, made giggle, etc.)
        // carry their opinion impact in baseOpinionOffset. baseMoodEffect is typically 0
        // for social thoughts, so the old filter was discarding ALL positive social
        // memories, leaving the clone with 0 opinion from other pawns.
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

                    // Only positive opinion — the clone inherits goodwill, not grudges
                    var stage = memBase.CurStage;
                    if (stage == null) continue;

                    // v3d9 fix: Check opinion offset (social impact), not mood effect
                    if (stage.baseOpinionOffset <= 0) continue;

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

        // Psylink LEVEL is copied above, but Vanilla Psycasts Expanded stores the actual psycasts
        // (paths/foci/points/abilities) separately — mirror those too.
        CopyDup_Psycasts(src, dst);
    }

    private static ThingComp GetCompByTypeName(Pawn p, string simpleName)
    {
        if (p?.AllComps == null) return null;
        foreach (var c in p.AllComps)
            if (c.GetType().Name == simpleName) return c;
        return null;
    }

    /// <summary>Best-effort copy of learned psycast abilities from src to dst via the (VEF) CompAbilities
    /// comp. Resolves the comp by type name and calls its GiveAbility method by reflection, so it works
    /// without a hard dependency on VEF. No-op if the comp/method isn't found.</summary>
    private static void CopyLearnedAbilities(Pawn src, Pawn dst)
    {
        var srcComp = GetCompByTypeName(src, "CompAbilities");
        var dstComp = GetCompByTypeName(dst, "CompAbilities");
        if (srcComp == null || dstComp == null) return;

        var learned = HarmonyLib.AccessTools.Property(srcComp.GetType(), "LearnedAbilities")?.GetValue(srcComp)
                   ?? HarmonyLib.AccessTools.Field(srcComp.GetType(), "LearnedAbilities")?.GetValue(srcComp);
        if (learned is not System.Collections.IEnumerable list) return;

        var give = HarmonyLib.AccessTools.Method(dstComp.GetType(), "GiveAbility");
        var giveParams = give?.GetParameters();
        if (give == null || giveParams == null || giveParams.Length != 1) return;
        var pType = giveParams[0].ParameterType;

        foreach (var ability in list)
        {
            if (ability == null) continue;
            var def = HarmonyLib.AccessTools.Field(ability.GetType(), "def")?.GetValue(ability);
            object arg = pType.IsInstanceOfType(def) ? def : (pType.IsInstanceOfType(ability) ? ability : null);
            if (arg != null) give.Invoke(dstComp, new[] { arg });
        }
    }

    private static object FindPsycastHediff(Pawn pawn, Type type)
    {
        if (pawn?.health?.hediffSet?.hediffs == null) return null;
        foreach (var h in pawn.health.hediffSet.hediffs)
            if (type.IsInstanceOfType(h)) return h;
        return null;
    }

    /// <summary>
    /// Copies Vanilla Psycasts Expanded psycasts from src to dst. VPE keeps its state on a
    /// Hediff_PsycastAbilities (unlocked paths, meditation foci, points, and the learned psycast
    /// abilities in CompAbilities) — none of which vanilla duplication copies, so the clone kept the
    /// RANDOM psycasts PawnGenerator/ChangePsylinkLevel gave it. This resets the clone's psycasts
    /// (keeping the psylink level) and re-applies the source's paths, which re-grants the right
    /// abilities. All via reflection; no-op if VPE isn't installed or the pawn isn't a psycaster.
    /// </summary>
    private static void CopyDup_Psycasts(Pawn src, Pawn dst)
    {
        var type = HarmonyLib.AccessTools.TypeByName("VanillaPsycastsExpanded.Hediff_PsycastAbilities");
        if (type == null) return;
        try
        {
            var srcH = FindPsycastHediff(src, type);
            var dstH = FindPsycastHediff(dst, type);
            if (srcH == null || dstH == null) return;

            // Clear the clone's random psycasts (Reset keeps the psylink level).
            HarmonyLib.AccessTools.Method(type, "Reset")?.Invoke(dstH, null);

            var unlockPath  = HarmonyLib.AccessTools.Method(type, "UnlockPath");
            var unlockFocus = HarmonyLib.AccessTools.Method(type, "UnlockMeditationFocus");

            if (HarmonyLib.AccessTools.Field(type, "unlockedPaths")?.GetValue(srcH) is System.Collections.IEnumerable paths && unlockPath != null)
                foreach (var p in paths) unlockPath.Invoke(dstH, new[] { p });
            if (HarmonyLib.AccessTools.Field(type, "unlockedMeditationFoci")?.GetValue(srcH) is System.Collections.IEnumerable foci && unlockFocus != null)
                foreach (var f in foci) unlockFocus.Invoke(dstH, new[] { f });

            // Mirror the point pools (UnlockPath may have spent some).
            var pointsF = HarmonyLib.AccessTools.Field(type, "points");
            if (pointsF != null) pointsF.SetValue(dstH, pointsF.GetValue(srcH));
            var statF = HarmonyLib.AccessTools.Field(type, "statPoints");
            if (statF != null) statF.SetValue(dstH, statF.GetValue(srcH));
            var expF = HarmonyLib.AccessTools.Field(type, "experience");
            if (expF != null) expF.SetValue(dstH, expF.GetValue(srcH));

            // UnlockPath only makes a path available; the abilities the pawn actually LEARNED live on a
            // (VEF) CompAbilities comp. Copy those too, best-effort by reflection.
            CopyLearnedAbilities(src, dst);

            HarmonyLib.AccessTools.Method(type, "RecacheCurStage")?.Invoke(dstH, null);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Psycasts (VPE): {ex.Message}"); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Records
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copies all pawn records (time spent on tasks, kills, etc.).
    /// v3d9 fix: Skips Time-type records — RimWorld doesn't allow AddTo() on those
    /// (they are tick-tracked internally). Only copies Int and Float records.
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
                // v3d9 fix: Skip Time-type records — RimWorld tracks these internally
                // via ticks. Calling AddTo() on them logs "Tried to add value to record
                // whose record type is Time" and does nothing useful.
                if (rd.type == RecordType.Time) continue;

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

    // ────────────────────────────────────────────────────────
    //  Life Lessons Proficiencies
    // ────────────────────────────────────────────────────────

    /// <summary>
    /// Copies the pawn's completed Life Lessons proficiencies to the duplicate.
    /// No-op if Life Lessons isn't active. Uses force: true so prerequisites are
    /// granted alongside each proficiency, then refreshes the derived modifiers.
    /// Mirrors WriteProficiencies / LoadProficiencies in the blueprint save/load.
    /// </summary>
    private static void CopyDup_Proficiencies(Pawn src, Pawn dst)
    {
        if (!LifeLessonsCompat.Active) return;
        try
        {
            var completed = LifeLessonsCompat.GetCompletedProficiencies(src);
            if (completed.Count == 0) return;

            // The clone is born with its own backstory-resolved proficiencies. Clear them first
            // so the result matches the source exactly instead of being source + clone's own.
            LifeLessonsCompat.ClearProficiencies(dst);

            foreach (var prof in completed)
            {
                if (prof == null) continue;
                LifeLessonsCompat.TryGainProficiency(dst, prof, force: true);
            }
            LifeLessonsCompat.RefreshModifiers(dst);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] CopyDup_Proficiencies: {ex.Message}"); }
    }
}
