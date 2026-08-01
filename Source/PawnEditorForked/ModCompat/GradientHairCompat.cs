using System;
using System.Reflection;
using HarmonyLib;
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
