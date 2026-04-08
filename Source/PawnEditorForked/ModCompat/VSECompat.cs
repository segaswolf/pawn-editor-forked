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
}