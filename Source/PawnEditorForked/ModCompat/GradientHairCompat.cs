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
/// Optional support for the Gradient Hair mod (workshop 1687053679), a long-standing community request.
/// That mod gives a pawn a second hair colour rendered through a gradient mask.
///
/// Verified against its shipped source (Source/PublicApi.cs):
///   GradientHair.PublicApi.GetGradientHair(Pawn, out bool enabled, out Color colorB) -> bool (has comp)
///   GradientHair.PublicApi.SetGradientHair(Pawn, bool enabled, Color colorB)
/// ColorA is just the vanilla hair colour (pawn.story.HairColor); colorB is the gradient's second colour,
/// stored on the pawn's CompGradientHair. All reflection by type name, so with the mod absent this class
/// is inert and nothing in the editor changes.
/// </summary>
public static class GradientHairCompat
{
    private static readonly Type ApiType = AccessTools.TypeByName("GradientHair.PublicApi");
    private static readonly MethodInfo GetMethod = ApiType == null ? null : AccessTools.Method(ApiType, "GetGradientHair");
    private static readonly MethodInfo SetMethod = ApiType == null ? null : AccessTools.Method(ApiType, "SetGradientHair");

    public static bool Active => GetMethod != null && SetMethod != null;

    // The pattern (mask) isn't in PublicApi, only on the comp's settings. Verified against workshop 1687053679
    // (Source/GradientHairSettings.cs, GradientHairMaskDef.cs, StylingStation/PatchPawn_StyleTracker.cs, Oct 2026).
    private static readonly Type CompType = AccessTools.TypeByName("GradientHair.CompGradientHair");
    private static readonly PropertyInfo SettingsProperty = CompType == null ? null : AccessTools.Property(CompType, "Settings");
    private static readonly Type SettingsType = AccessTools.TypeByName("GradientHair.GradientHairSettings");
    private static readonly FieldInfo MaskField = SettingsType == null ? null : AccessTools.Field(SettingsType, "mask");
    private static readonly FieldInfo DesiredMaskField = SettingsType == null ? null : AccessTools.Field(SettingsType, "desiredMask");
    private static readonly Type PatternDefType = AccessTools.TypeByName("Verse.GradientHairMaskDef");
    private static readonly FieldInfo PatternPathField = PatternDefType == null ? null : AccessTools.Field(PatternDefType, "mask");
    private static readonly FieldInfo PatternGroupField = PatternDefType == null ? null : AccessTools.Field(PatternDefType, "group");
    private static readonly FieldInfo PatternOrderField = PatternDefType == null ? null : AccessTools.Field(PatternDefType, "order");
    private static List<StyleItemDef> patterns;

    /// <summary>The installed version exposes everything needed to read and change the pattern.</summary>
    public static bool PatternsAvailable => Active && SettingsProperty != null && MaskField != null
        && DesiredMaskField != null && PatternDefType != null && PatternPathField != null;

    /// <summary>Every gradient pattern, in the mod's own group and order. Defs don't change after load, so cached.</summary>
    public static List<StyleItemDef> Patterns => patterns ??= LoadPatterns();

    /// <summary>The pattern the pawn uses now, or null if it can't be read.</summary>
    /// <remarks>Called every frame by the hair row: a plain loop, no LINQ closure.</remarks>
    public static StyleItemDef CurrentPattern(Pawn pawn)
    {
        var settings = GetSettings(pawn);
        if (settings == null) return null;
        var path = MaskField.GetValue(settings) as string;
        foreach (var pattern in Patterns)
        {
            if (PatternPathField.GetValue(pattern) as string == path) return pattern;
        }
        return null;
    }

    /// <summary>The pattern with that defName, or null (mod missing, pattern removed, or old blueprint).</summary>
    public static StyleItemDef PatternNamed(string defName)
    {
        if (defName.NullOrEmpty()) return null;
        foreach (var pattern in Patterns)
        {
            if (pattern.defName == defName) return pattern;
        }
        return null;
    }

    /// <summary>Sets the pattern and drops any pending styling-station change, which would overwrite it later.</summary>
    public static void SetPattern(Pawn pawn, StyleItemDef pattern)
    {
        var settings = GetSettings(pawn);
        if (settings == null || pattern == null) return;
        try
        {
            MaskField.SetValue(settings, PatternPathField.GetValue(pattern));
            DesiredMaskField.SetValue(settings, null);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Gradient Hair pattern write failed for {pawn.LabelShort} ({pattern.defName}): {ex.Message}");
        }
    }

    private static object GetSettings(Pawn pawn)
    {
        if (!PatternsAvailable || pawn?.AllComps == null) return null;
        foreach (var comp in pawn.AllComps)
        {
            if (CompType.IsInstanceOfType(comp)) return SettingsProperty.GetValue(comp);
        }
        return null;
    }

    private static List<StyleItemDef> LoadPatterns()
    {
        if (!PatternsAvailable) return new List<StyleItemDef>();
        return GenDefDatabase.GetAllDefsInDatabaseForDef(PatternDefType).OfType<StyleItemDef>()
            .OrderBy(pattern => PatternGroupField?.GetValue(pattern) as string ?? string.Empty)
            .ThenBy(pattern => PatternOrderField?.GetValue(pattern) is int order ? order : 0)
            .ToList();
    }

    // The community "Gradient Hair Fixes" mod fixes a rendering gap: hairs whose textures use the old
    // _back/_side/_front naming show up blank under Gradient Hair. We deliberately do NOT reimplement
    // that (it's a full takeover of their render method — a bug there would land on us). Instead, when
    // the user has Gradient Hair but not that fix, we surface a gentle hint so a blank hair reads as
    // "install the fix mod", not "Pawn Editor is broken".
    private const string FixesPackageId = "cosmosteller.gradienthair.fixes";
    public static bool ShouldRecommendFixes => Active && !ModsConfig.IsActive(FixesPackageId);

    /// <summary>
    /// Reads the pawn's gradient state. Returns false if the mod is absent or the pawn has no gradient
    /// comp (e.g. a mech), in which case there's nothing to show in the UI.
    /// </summary>
    public static bool TryGet(Pawn pawn, out bool enabled, out Color colorB)
    {
        enabled = false;
        colorB = Color.white;
        if (!Active || pawn == null) return false;

        try
        {
            var args = new object[] { pawn, false, Color.white };
            var has = (bool)GetMethod.Invoke(null, args);
            if (!has) return false;
            enabled = (bool)args[1];
            colorB = (Color)args[2];
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Gradient Hair read failed: {ex.Message}");
            return false;
        }
    }

    public static void Set(Pawn pawn, bool enabled, Color colorB)
    {
        if (!Active || pawn == null) return;
        try
        {
            SetMethod.Invoke(null, new object[] { pawn, enabled, colorB });
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Gradient Hair write failed: {ex.Message}");
        }
    }
}
