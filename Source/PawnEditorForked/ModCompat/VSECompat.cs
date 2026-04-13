using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[ModCompat("vanillaexpanded.skills")]
public static class VSECompat
{
    public static bool Active;
    public static string Name = "Vanilla Skills Expanded";

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

    // Expertise system
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

        // Expertise system
        expertiseTrackersType = AccessTools.TypeByName("VSE.ExpertiseTrackers");
        expertiseTrackerType = AccessTools.TypeByName("VSE.ExpertiseTracker");
        expertiseRecordType = AccessTools.TypeByName("VSE.ExpertiseRecord");
        expertiseDefType = AccessTools.TypeByName("VSE.Expertise.ExpertiseDef");

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
            }
        }
    }

    public static Texture2D GetPassionIcon(Passion passion)
    {
        var passionDef = passionToDef(passion);
        var icon = (Texture2D)iconProperty.GetValue(passionDef);
        return icon;
    }

    public static Passion ChangePassion(Passion passion) => changePassion(passion, 1);
    public static void ClearCacheFor(SkillRecord sr, Passion passion) => clearCacheFor(sr, passion);

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
    public static List<FloatMenuOption> GetPassionFloatMenuOptions(SkillRecord skill)
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
                },
                icon, Color.white));
        }

        return options;
    }

    // ── Expertise API ──

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

        try
        {
            var tracker = GetExpertiseTracker(pawn);
            if (tracker == null) return false;

            // Clear existing
            clearExpertiseMethod?.Invoke(tracker, Array.Empty<object>());

            foreach (var snap in snapshots)
            {
                // Resolve ExpertiseDef by defName
                var def = GenDefDatabase.GetDefSilentFail(expertiseDefType, snap.DefName, false);
                if (def == null)
                {
                    Log.Warning($"[Pawn Editor] VSE expertise '{snap.DefName}' not found, skipping");
                    continue;
                }

                // AddExpertise(ExpertiseDef)
                addExpertiseMethod?.Invoke(tracker, new object[] { def });

                // Now set level and XP on the newly added record
                var allExpertise = allExpertiseProperty?.GetValue(tracker) as System.Collections.IList;
                if (allExpertise == null || allExpertise.Count == 0) continue;

                // The one we just added is the last one
                var record = allExpertise[allExpertise.Count - 1];
                if (record == null) continue;

                expertiseLevelProperty?.SetValue(record, snap.Level);
                expertiseXpField?.SetValue(record, snap.XpSinceLastLevel);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] VSE RestoreExpertise: {ex.Message}");
            return false;
        }
    }

    public class ExpertiseSnapshot
    {
        public string DefName;
        public int Level;
        public float XpSinceLastLevel;
    }
}