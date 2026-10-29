using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Compatibility layer for Vanilla Skills Expanded (VSE).
/// Provides access to VSE's custom passion system (30+ passion types with icons)
/// and expertise system (skill specializations with XP tracking).
/// All access is via reflection to avoid hard dependency on VSE assembly.
/// </summary>
[ModCompat("vanillaexpanded.skills")]
public static class VSECompat
{
    public static bool Active;
    public static string Name = "Vanilla Skills Expanded";

    // ── Reflection fields: Passion system ──
    private static Type passionManager;
    private static Func<Passion, object> passionToDef;
    private static FieldInfo passionDefArray;
    
    private static Type passionUtilities;
    private static Func<Passion, int, Passion> changePassion;
    
    private static Type learnRateFactorCache;
    private static Action<SkillRecord, Passion?> clearCacheFor;

    private static Type passionDefType;
    private static PropertyInfo iconProperty;
    private static FieldInfo labelField;
    private static FieldInfo indexField;
    private static FieldInfo hediffToAddField;

    // ── Reflection fields: Expertise system ──
    private static Type expertiseTrackersType;
    private static Type expertiseTrackerType;
    private static Type expertiseRecordType;
    private static Type expertiseDefType;
    private static MethodInfo expertisePawnMethod;
    private static PropertyInfo allExpertiseProperty;
    private static MethodInfo addExpertiseMethod;
    private static MethodInfo clearExpertiseMethod;
    private static MethodInfo hasExpertiseMethod;
    private static FieldInfo expertiseDefField;
    private static PropertyInfo expertiseLevelProperty;
    private static FieldInfo expertiseXpField;
    
    /// <summary>
    /// Initializes all reflection fields by resolving VSE types, methods, and fields.
    /// Called automatically when the mod is detected as active.
    /// </summary>
    public static void Activate()
    {
        passionManager = AccessTools.TypeByName("VSE.Passions.PassionManager");
        passionToDef = AccessTools.Method(passionManager, "PassionToDef").CreateDelegate<Func<Passion, object>>();
        passionDefArray = AccessTools.Field(passionManager, "Passions");

        passionUtilities = AccessTools.TypeByName("VSE.Passions.PassionUtilities");
        changePassion = AccessTools.Method(passionUtilities, "ChangePassion").CreateDelegate<Func<Passion, int, Passion>>();

        learnRateFactorCache = AccessTools.TypeByName("VSE.Passions.LearnRateFactorCache");
        clearCacheFor = AccessTools.Method(learnRateFactorCache, "ClearCacheFor").CreateDelegate<Action<SkillRecord, Passion?>>();

        passionDefType = AccessTools.TypeByName("VSE.Passions.PassionDef");
        iconProperty = AccessTools.Property(passionDefType, "Icon");
        labelField = AccessTools.Field(passionDefType, "label");
        indexField = AccessTools.Field(passionDefType, "index");
        hediffToAddField = AccessTools.Field(passionDefType, "hediffToAdd");

        // Expertise system
        expertiseTrackersType = AccessTools.TypeByName("VSE.ExpertiseTrackers");
        expertiseTrackerType = AccessTools.TypeByName("VSE.ExpertiseTracker");
        expertiseRecordType = AccessTools.TypeByName("VSE.ExpertiseRecord");
        expertiseDefType = AccessTools.TypeByName("VSE.Expertise.ExpertiseDef");
        if (expertiseDefType != null)
        {
            canApplyOnMethod = AccessTools.Method(expertiseDefType, "CanApplyOn", new[] { typeof(Pawn), typeof(string).MakeByRefType() });
            expertiseHideField = AccessTools.Field(expertiseDefType, "hide");
            expertiseSkillField = AccessTools.Field(expertiseDefType, "skill");
            expertiseEffectsMethod = AccessTools.Method(expertiseDefType, "Effects", new[] { typeof(int), typeof(string) });
        }

        if (expertiseTrackersType != null)
        {
            expertisePawnMethod = AccessTools.Method(expertiseTrackersType, "Expertise", new[] { typeof(Pawn) });
            allExpertiseProperty = AccessTools.Property(expertiseTrackerType, "AllExpertise");
            addExpertiseMethod = AccessTools.Method(expertiseTrackerType, "AddExpertise");
            clearExpertiseMethod = AccessTools.Method(expertiseTrackerType, "ClearExpertise");
            hasExpertiseMethod = AccessTools.Method(expertiseTrackerType, "HasExpertise");

            if (expertiseRecordType != null)
            {
                expertiseDefField = AccessTools.Field(expertiseRecordType, "def");
                expertiseLevelProperty = AccessTools.Property(expertiseRecordType, "Level");
                expertiseXpField = AccessTools.Field(expertiseRecordType, "XpSinceLastLevel");
                xpRequiredField = AccessTools.Field(expertiseRecordType, "XpRequiredForLevelUp");
            }
        }
    }

    /// <summary>Gets the custom passion icon for a VSE passion level.</summary>
    public static Texture2D GetPassionIcon(Passion passion)
    {
        var passionDef = passionToDef(passion);
        var icon = (Texture2D)iconProperty.GetValue(passionDef);
        return icon;
    }

    /// <summary>Cycles to the next passion level (wraps around).</summary>
    public static Passion ChangePassion(Passion passion) => changePassion(passion, 1);

    /// <summary>Clears the learn rate factor cache for a skill after changing its passion.</summary>
    public static void ClearCacheFor(SkillRecord sr, Passion passion) => clearCacheFor(sr, passion);

    public static string GetPassionTooltip(Passion passion)
    {
        var passionDef = passionToDef(passion) as Def;
        if (passionDef == null)
            return passion.ToString();

        return passionDef.LabelCap + (passionDef.description.NullOrEmpty() ? string.Empty : "\n\n" + passionDef.description);
    }

    /// <summary>
    /// Adds "Set all to [passion]" options to a float menu for bulk passion assignment.
    /// </summary>
    public static void AddPassionPresets(List<FloatMenuOption> floatMenuOptions, Pawn pawn)
    {
        var passionDefs = passionDefArray.GetValue(null) as Array;
        foreach (var passionDef in passionDefs)
        {
            var label = (string)labelField.GetValue(passionDef);
            var index = (ushort)indexField.GetValue(passionDef);
            floatMenuOptions.Add(new("PawnEditor.SetAllTo".Translate("PawnEditor.Passions".Translate(), label),
                TabWorker_Bio_Humanlike.GetSetDelegate(pawn, true, index)));
        }
    }

    /// <summary>
    /// Creates a FloatMenu with all available VSE passions for a specific skill.
    /// Each option shows the passion icon and label. Selecting one applies it immediately.
    /// Much better UX than cycling through 30+ passions one click at a time.
    /// </summary>
    public static List<FloatMenuOption> GetPassionFloatMenuOptions(SkillRecord skill, Pawn pawn)
    {
        var options = new List<FloatMenuOption>();
        var passionDefs = passionDefArray.GetValue(null) as Array;
        if (passionDefs == null) return options;

        foreach (var passionDef in passionDefs)
        {
            var label = (string)labelField.GetValue(passionDef);
            var index = (ushort)indexField.GetValue(passionDef);
            var icon = (Texture2D)iconProperty.GetValue(passionDef);
            var passion = (Passion)index;

            options.Add(new FloatMenuOption(
                label.CapitalizeFirst(),
                () =>
                {
                    ClearCacheFor(skill, passion);
                    skill.passion = passion;
                    // Keep VSE's passion hediffs (e.g. traumatic mood penalty) in sync with the change.
                    SyncPassionHediffs(pawn);
                    PawnEditor.Notify_PointsUsed();
                },
                icon, Color.white));
        }

        return options;
    }

    /// <summary>
    /// Reconciles VSE "passion hediffs" with the pawn's CURRENT passions. Several VSE / Alpha Skills
    /// passions carry a <c>hediffToAdd</c> that implements their effect (e.g. the traumatic passion's
    /// mood penalty). Assigning skill.passion directly — as the editor does — bypasses VSE's own hediff
    /// bookkeeping, which left orphan hediffs when a passion was removed (the traumatic penalty then hit
    /// ALL work) and missing hediffs when one was added (the passion did nothing). This adds the wanted
    /// hediffs and removes the stale ones. It only ever touches hediffs that some PassionDef declares,
    /// so unrelated hediffs are never affected.
    /// </summary>
    public static void SyncPassionHediffs(Pawn pawn)
    {
        if (!Active || hediffToAddField == null || pawn?.skills == null || pawn.health == null) return;
        try
        {
            if (passionDefArray.GetValue(null) is not Array passionDefs) return;

            // Every hediff any passion is responsible for — the only set we may add or remove.
            var managed = new HashSet<HediffDef>();
            foreach (var pd in passionDefs)
                if (hediffToAddField.GetValue(pd) is HediffDef h) managed.Add(h);
            if (managed.Count == 0) return;

            // The hediffs the pawn's current passions want.
            var wanted = new HashSet<HediffDef>();
            foreach (var sr in pawn.skills.skills)
            {
                var pd = passionToDef(sr.passion);
                if (pd != null && hediffToAddField.GetValue(pd) is HediffDef h) wanted.Add(h);
            }

            foreach (var def in managed)
            {
                var has = pawn.health.hediffSet.HasHediff(def);
                if (wanted.Contains(def))
                {
                    if (!has) pawn.health.AddHediff(def);
                }
                else if (has)
                {
                    var hd = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                    if (hd != null) pawn.health.RemoveHediff(hd);
                }
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] VSE SyncPassionHediffs: {ex.Message}"); }
    }

    // ── Expertise API ──

    /// <summary>Whether the expertise system is available (some VSE versions may not have it).</summary>
    public static bool HasExpertiseSupport => expertiseTrackersType != null;

    /// <summary>
    /// Gets the ExpertiseTracker for a pawn, or null if not available.
    /// </summary>
    public static object GetExpertiseTracker(Pawn pawn)
    {
        if (expertisePawnMethod == null || pawn == null) return null;
        try { return expertisePawnMethod.Invoke(null, new object[] { pawn }); }
        catch { return null; }
    }

    /// <summary>
    /// Returns a list of (defName, level, xp) for all expertise on a pawn.
    /// </summary>
    public static List<ExpertiseSnapshot> GetExpertiseData(Pawn pawn)
    {
        var result = new List<ExpertiseSnapshot>();
        if (!HasExpertiseSupport) return result;

        try
        {
            var tracker = GetExpertiseTracker(pawn);
            if (tracker == null) return result;

            var allExpertise = allExpertiseProperty?.GetValue(tracker) as System.Collections.IList;
            if (allExpertise == null) return result;

            foreach (var record in allExpertise)
            {
                if (record == null) continue;
                var def = expertiseDefField?.GetValue(record) as Def;
                if (def == null) continue;
                var level = (int)(expertiseLevelProperty?.GetValue(record) ?? 0);
                var xp = (float)(expertiseXpField?.GetValue(record) ?? 0f);
                result.Add(new ExpertiseSnapshot { DefName = def.defName, Level = level, XpSinceLastLevel = xp });
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] VSE GetExpertiseData: {ex.Message}"); }

        return result;
    }

    /// <summary>
    /// Restores expertise on a pawn from snapshot data.
    /// Clears any existing expertise, then adds and levels each one.
    /// </summary>
    public static bool RestoreExpertise(Pawn pawn, List<ExpertiseSnapshot> snapshots)
    {
        if (!HasExpertiseSupport || snapshots == null || snapshots.Count == 0) return false;
        return SetExpertise(pawn, snapshots);
    }

    /// <summary>
    /// Replaces the pawn's expertise with exactly these records. Unlike <see cref="RestoreExpertise"/>, an
    /// empty list is valid: Cancel uses it to take the pawn back to "no expertise".
    /// </summary>
    public static bool SetExpertise(Pawn pawn, List<ExpertiseSnapshot> snapshots)
    {
        if (!HasExpertiseSupport || snapshots == null) return false;

        try
        {
            var tracker = GetExpertiseTracker(pawn);
            if (tracker == null) return false;

            clearExpertiseMethod?.Invoke(tracker, Array.Empty<object>());

            foreach (var snap in snapshots)
            {
                var def = GenDefDatabase.GetDefSilentFail(expertiseDefType, snap.DefName, false);
                if (def == null)
                {
                    Log.Warning($"[Pawn Editor] VSE expertise '{snap.DefName}' not found, skipping");
                    continue;
                }

                addExpertiseMethod?.Invoke(tracker, new object[] { def });

                // AddExpertise appends, so the new record is the last one.
                var allExpertise = allExpertiseProperty?.GetValue(tracker) as System.Collections.IList;
                if (allExpertise == null || allExpertise.Count == 0) continue;
                var record = allExpertise[allExpertise.Count - 1];
                if (record == null) continue;

                SetRecordLevel(record, snap.Level);
                expertiseXpField?.SetValue(record, snap.XpSinceLastLevel);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] VSE SetExpertise: {ex.Message}");
            return false;
        }
    }

    // ── Expertise editing (Skills column) ──
    // What a pawn may take is VSE's call, through its own ExpertiseDef.CanApplyOn: no "bad" passion on the
    // skill, skill level at least LevelToGetExpertise (15 by default), fewer than MaxExpertise (1 by
    // default), and no two of the same skill unless AllowExpertiseOverlap. Those are VSE settings, so we ask
    // VSE rather than copy the numbers. Verified against VSE's decompiled source (Dev Notes, Oct 2026).

    /// <summary>VSE stops levelling an expertise at 20 (ExpertiseRecord level-up loop).</summary>
    public const int MaxExpertiseLevel = 20;

    /// <summary>VSE's "max expertise" setting (1 by default, up to 75), or null if it can't be read.</summary>
    public static int? MaxExpertise
    {
        get
        {
            try
            {
                var settings = AccessTools.Field(AccessTools.TypeByName("VSE.SkillsMod"), "Settings")?.GetValue(null);
                return settings == null ? null : AccessTools.Field(settings.GetType(), "MaxExpertise")?.GetValue(settings) as int?;
            }
            catch { return null; }
        }
    }

    private static MethodInfo canApplyOnMethod;
    private static FieldInfo expertiseHideField;
    private static FieldInfo expertiseSkillField;
    private static FieldInfo xpRequiredField;
    private static MethodInfo expertiseEffectsMethod;
    private static List<Def> expertiseDefs;

    /// <summary>The installed VSE exposes everything the expertise row needs.</summary>
    public static bool CanEditExpertise => HasExpertiseSupport && canApplyOnMethod != null && expertiseDefField != null
        && expertiseLevelProperty != null && addExpertiseMethod != null && allExpertiseProperty != null;

    /// <summary>Every expertise a player could pick (VSE's hidden ones left out), in skill-list order.</summary>
    public static List<Def> ExpertiseDefs => expertiseDefs ??= LoadExpertiseDefs();

    private static List<Def> LoadExpertiseDefs()
    {
        if (expertiseDefType == null) return new List<Def>();
        return GenDefDatabase.GetAllDefsInDatabaseForDef(expertiseDefType)
            .Where(def => expertiseHideField?.GetValue(def) is not true)
            .OrderByDescending(def => SkillOf(def)?.listOrder ?? 0)
            .ThenBy(def => def.label)
            .ToList();
    }

    /// <summary>
    /// What the expertise changes at that level, in VSE's own words (ExpertiseDef.Effects: one line per stat).
    /// Empty if the installed VSE doesn't expose it. Effects grow linearly with the level.
    /// </summary>
    public static string ExpertiseEffects(Def expertise, int level)
    {
        if (expertiseEffectsMethod == null || expertise == null) return string.Empty;
        try { return (expertiseEffectsMethod.Invoke(expertise, new object[] { level, "  " }) as string)?.Trim('\n') ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>The skill an expertise belongs to, or null if it can't be read.</summary>
    public static SkillDef SkillOf(Def expertise) => expertiseSkillField?.GetValue(expertise) as SkillDef;

    /// <summary>The pawn's expertise and its level, in VSE's order. Empty when the pawn has none.</summary>
    public static List<(Def def, int level)> CurrentExpertise(Pawn pawn)
    {
        var result = new List<(Def, int)>();
        var records = Records(pawn);
        if (records == null) return result;
        foreach (var record in records)
        {
            if (record == null || expertiseDefField.GetValue(record) is not Def def) continue;
            result.Add((def, (int)expertiseLevelProperty.GetValue(record)));
        }
        return result;
    }

    /// <summary>Whether VSE lets the pawn take this expertise now. The reason is VSE's own text.</summary>
    public static bool CanApplyExpertise(Def expertise, Pawn pawn, out string reason)
    {
        var args = new object[] { pawn, null };
        try
        {
            var allowed = (bool)canApplyOnMethod.Invoke(expertise, args);
            reason = args[1] as string;
            return allowed;
        }
        catch (Exception ex)
        {
            reason = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
    }

    public static void AddExpertise(Pawn pawn, Def expertise)
    {
        var tracker = GetExpertiseTracker(pawn);
        if (tracker == null) return;
        try { addExpertiseMethod.Invoke(tracker, new object[] { expertise }); }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] VSE AddExpertise {expertise?.defName}: {ex.InnerException?.Message ?? ex.Message}"); }
    }

    public static void RemoveExpertise(Pawn pawn, Def expertise)
    {
        var records = Records(pawn);
        var index = IndexOf(records, expertise);
        if (index >= 0) records.RemoveAt(index);
    }

    /// <summary>Sets the level (0 to 20) and starts that level's progress from zero.</summary>
    public static void SetExpertiseLevel(Pawn pawn, Def expertise, int level)
    {
        var records = Records(pawn);
        var index = IndexOf(records, expertise);
        if (index < 0) return;
        SetRecordLevel(records[index], level);
        expertiseXpField?.SetValue(records[index], 0f);
    }

    // VSE derives XpRequiredForLevelUp from the level only when it adds a record or loads a save, so setting
    // the level alone left the bar measuring progress against level 0 until the next load.
    private static void SetRecordLevel(object record, int level)
    {
        level = Mathf.Clamp(level, 0, MaxExpertiseLevel);
        expertiseLevelProperty?.SetValue(record, level);
        xpRequiredField?.SetValue(record, SkillRecord.XpRequiredToLevelUpFrom(level));
    }

    private static System.Collections.IList Records(Pawn pawn)
    {
        var tracker = GetExpertiseTracker(pawn);
        if (tracker == null || allExpertiseProperty == null) return null;
        try { return allExpertiseProperty.GetValue(tracker) as System.Collections.IList; }
        catch { return null; }
    }

    private static int IndexOf(System.Collections.IList records, Def expertise)
    {
        if (records == null || expertise == null || expertiseDefField == null) return -1;
        for (var i = 0; i < records.Count; i++)
            if (records[i] != null && expertiseDefField.GetValue(records[i]) == expertise) return i;
        return -1;
    }

    /// <summary>
    /// Snapshot of a single expertise record for serialization.
    /// Used by blueprint save/load and pawn duplication.
    /// </summary>
    public class ExpertiseSnapshot
    {
        public string DefName;
        public int Level;
        public float XpSinceLastLevel;
    }
}
