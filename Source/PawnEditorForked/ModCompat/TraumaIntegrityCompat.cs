using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[ModCompat("ferny.TraumaAndIntegrity")]
public static class TraumaIntegrityCompat
{
    public static bool Active;
    public static string Name = "Trauma and Integrity";

    private static MethodInfo getDataMethod;
    private static MethodInfo onTraumaChangedMethod;
    private static FieldInfo traumaField;
    private static FieldInfo integrityField;
    private static FieldInfo betrayerField;
    private static FieldInfo gracePeriodEndTickField;
    private static FieldInfo temperedField;
    private static FieldInfo hasWarnedIntegrityField;
    private static MethodInfo rollBetrayalMethod;
    private static FieldInfo enableTraumaField;
    private static FieldInfo enableIntegrityField;
    private static FieldInfo hideIntegrityField;
    private static bool ready;

    public static bool Available => Active && ready;

    public static void Activate()
    {
        var patchType = AccessTools.TypeByName("TraumaAndIntegrity.Pawn_ExposeData_Patch");
        var dataType = AccessTools.TypeByName("TraumaAndIntegrity.TraumaIntegrityData");
        var settingsType = AccessTools.TypeByName("TraumaAndIntegrity.TraumaAndIntegritySettings");

        if (patchType == null || dataType == null || settingsType == null)
        {
            ready = false;
            Log.Error("[Pawn Editor] Trauma and Integrity compatibility could not resolve its required API.");
            return;
        }

        getDataMethod = AccessTools.Method(patchType, "GetTraumaIntegrityData", new[] { typeof(Pawn) });
        onTraumaChangedMethod = AccessTools.Method(patchType, "OnTraumaChanged",
            new[] { typeof(Pawn), typeof(float), typeof(float) });
        traumaField = AccessTools.Field(dataType, "trauma");
        integrityField = AccessTools.Field(dataType, "integrity");
        betrayerField = AccessTools.Field(dataType, "isBetrayer");
        gracePeriodEndTickField = AccessTools.Field(dataType, "gracePeriodEndTick");
        temperedField = AccessTools.Field(dataType, "tempered");
        hasWarnedIntegrityField = AccessTools.Field(dataType, "hasWarnedIntegrity");
        rollBetrayalMethod = AccessTools.Method(dataType, "RollBetrayal", Type.EmptyTypes);
        enableTraumaField = AccessTools.Field(settingsType, "enableTrauma");
        enableIntegrityField = AccessTools.Field(settingsType, "enableIntegrity");
        hideIntegrityField = AccessTools.Field(settingsType, "hideIntegrityUI");

        ready = patchType != null
                && dataType != null
                && getDataMethod != null
                && onTraumaChangedMethod != null
                && traumaField != null
                && integrityField != null
                && betrayerField != null
                && gracePeriodEndTickField != null
                && temperedField != null
                && rollBetrayalMethod != null
                && enableTraumaField != null
                && enableIntegrityField != null
                && hideIntegrityField != null;

        if (!ready)
            Log.Error("[Pawn Editor] Trauma and Integrity compatibility could not resolve its required API.");
    }

    public static float GetTrauma(Pawn pawn)
    {
        var data = GetData(pawn);
        return data == null ? 0f : (float)traumaField.GetValue(data);
    }

    public static float GetIntegrity(Pawn pawn)
    {
        var data = GetData(pawn);
        return data == null ? -1f : (float)integrityField.GetValue(data);
    }

    public static bool IsTempered(Pawn pawn)
    {
        var data = GetData(pawn);
        return data != null && temperedField.GetValue(data) is true;
    }

    public static bool IsBetrayer(Pawn pawn)
    {
        var data = GetData(pawn);
        return data != null && betrayerField.GetValue(data) is true;
    }

    public static float GetGraceDaysRemaining(Pawn pawn)
    {
        var data = GetData(pawn);
        if (data == null || betrayerField.GetValue(data) is not true)
            return 0f;

        var ticksRemaining = (long)(int)gracePeriodEndTickField.GetValue(data) - CurrentTicks;
        return Mathf.Max(0f, ticksRemaining / (float)GenDate.TicksPerDay);
    }

    public static bool TraumaEnabled => Available && enableTraumaField.GetValue(null) is true;
    public static bool IntegrityEnabled => Available && enableIntegrityField.GetValue(null) is true;
    public static bool IntegrityHidden => Available && hideIntegrityField.GetValue(null) is true;

    public static bool SetTrauma(Pawn pawn, float value)
    {
        var data = GetData(pawn);
        if (data == null)
            return false;

        var oldValue = (float)traumaField.GetValue(data);
        var newValue = Mathf.Clamp01(value);
        var shouldBeTempered = newValue >= 1f;
        if (oldValue == newValue && (temperedField.GetValue(data) is true) == shouldBeTempered)
            return false;

        traumaField.SetValue(data, newValue);
        onTraumaChangedMethod.Invoke(null, new object[] { pawn, oldValue, newValue });
        return true;
    }

    public static bool SetIntegrity(Pawn pawn, float value)
    {
        var data = GetData(pawn);
        if (data == null)
            return false;

        var newValue = Mathf.Clamp01(value);
        var oldValue = (float)integrityField.GetValue(data);
        if (oldValue == newValue)
            return false;

        integrityField.SetValue(data, newValue);
        rollBetrayalMethod.Invoke(data, Array.Empty<object>());
        if (betrayerField.GetValue(data) is not true)
            gracePeriodEndTickField.SetValue(data, -1);
        return true;
    }

    public static bool SetBetrayer(Pawn pawn, bool value)
    {
        var data = GetData(pawn);
        if (data == null || (betrayerField.GetValue(data) is true) == value)
            return false;

        betrayerField.SetValue(data, value);
        gracePeriodEndTickField.SetValue(data, value ? CurrentTicks : -1);
        return true;
    }

    public static bool SetGraceDaysRemaining(Pawn pawn, float days)
    {
        var data = GetData(pawn);
        if (data == null || betrayerField.GetValue(data) is not true)
            return false;

        var ticksToAdd = Mathf.RoundToInt(Mathf.Max(0f, days) * GenDate.TicksPerDay);
        var endTick = (int)Math.Min(int.MaxValue, (long)CurrentTicks + ticksToAdd);
        if ((int)gracePeriodEndTickField.GetValue(data) == endTick)
            return false;

        gracePeriodEndTickField.SetValue(data, endTick);
        return true;
    }

    public static void CopyData(Pawn source, Pawn destination)
    {
        if (!Available || source == null || destination == null)
            return;

        var sourceData = GetData(source);
        var destinationData = GetData(destination);
        if (sourceData == null || destinationData == null)
            return;

        traumaField.SetValue(destinationData, traumaField.GetValue(sourceData));
        integrityField.SetValue(destinationData, integrityField.GetValue(sourceData));
        betrayerField.SetValue(destinationData, betrayerField.GetValue(sourceData));
        gracePeriodEndTickField.SetValue(destinationData, gracePeriodEndTickField.GetValue(sourceData));
        temperedField.SetValue(destinationData, temperedField.GetValue(sourceData));
        if (hasWarnedIntegrityField != null)
            hasWarnedIntegrityField.SetValue(destinationData, hasWarnedIntegrityField.GetValue(sourceData));
    }

    private static object GetData(Pawn pawn) =>
        Available && pawn?.RaceProps?.Humanlike == true
            ? getDataMethod.Invoke(null, new object[] { pawn })
            : null;

    private static int CurrentTicks =>
        Current.Game == null || Find.TickManager == null ? 0 : Find.TickManager.TicksGame;

}
