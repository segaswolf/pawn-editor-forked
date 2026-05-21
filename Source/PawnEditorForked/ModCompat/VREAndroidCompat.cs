using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Compatibility layer for Vanilla Races Expanded - Android.
/// Detects VRE Androids and provides access to the Android Xenotype Editor
/// that was previously injected into CharacterCardUtility's FloatMenu.
/// </summary>
[ModCompat("vanillaracesexpanded.android")]
public static class VREAndroidCompat
{
    public static bool Active;
    public static string Name = "Vanilla Races Expanded - Android";

    // Window_CreateAndroidXenotype(int index, Action callback)
    private static Type windowCreateAndroidXenotypeType;
    private static ConstructorInfo windowConstructor;

    // Utils.IsAndroid(Pawn) — checks if a pawn is an android
    private static MethodInfo isAndroidMethod;

    public static void Activate()
    {
        windowCreateAndroidXenotypeType = AccessTools.TypeByName("VREAndroids.Window_CreateAndroidXenotype");
        if (windowCreateAndroidXenotypeType != null)
            windowConstructor = AccessTools.Constructor(windowCreateAndroidXenotypeType, new[] { typeof(int), typeof(Action) });

        var utilsType = AccessTools.TypeByName("VREAndroids.Utils");
        if (utilsType != null)
            isAndroidMethod = AccessTools.Method(utilsType, "IsAndroid", new[] { typeof(Pawn) });

        if (windowConstructor == null)
        {
            Log.Warning("[Pawn Editor] VRE Android: Window_CreateAndroidXenotype constructor not found. Android editor button will be disabled.");
            Active = false;
        }
    }

    /// <summary>
    /// Returns true if the pawn is a VRE Android.
    /// </summary>
    public static bool IsAndroid(Pawn pawn)
    {
        if (isAndroidMethod == null || pawn == null) return false;
        try
        {
            return (bool)isAndroidMethod.Invoke(null, new object[] { pawn });
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Opens the Android Xenotype Editor window.
    /// In pre-colony, uses the pawn's starting index.
    /// In-game, passes -1 to avoid StartingPawnUtility NullRef.
    /// </summary>
    public static void OpenAndroidEditor(Pawn pawn, Action callback = null)
    {
        if (windowConstructor == null) return;

        try
        {
            var index = PawnEditor.Pregame
                ? StartingPawnUtility.PawnIndex(pawn)
                : -1;

            var onDone = callback ?? new Action(() =>
            {
                CharacterCardUtility.cachedCustomXenotypes = null;
            });

            var window = (Window)windowConstructor.Invoke(new object[] { index, onDone });
            Find.WindowStack.Add(window);
        }
        catch (Exception ex)
        {
            Log.Error($"[Pawn Editor] Failed to open Android Editor: {ex.Message}");
            Messages.Message("Pawn Editor: Android editor failed to open.", MessageTypeDefOf.RejectInput, false);
        }
    }
}
