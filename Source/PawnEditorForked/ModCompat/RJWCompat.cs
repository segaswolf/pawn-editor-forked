using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using Verse;

namespace PawnEditor;

[ModCompat("lucius.rjw")]
public static class RJWCompat
{
    public static bool Active;
    public static string Name = "Lucius RJW";

    private static Type compRJWType;
    private static Type orientationType;
    private static FieldInfo orientationField;

    [UsedImplicitly]
    public static void Activate()
    {
        compRJWType = AccessTools.TypeByName("rjw.CompRJW");
        orientationType = AccessTools.TypeByName("rjw.Orientation");
        orientationField = compRJWType == null ? null : AccessTools.Field(compRJWType, "orientation");
    }

    public static bool IsAvailableForPawn(Pawn pawn) =>
        Active && pawn?.RaceProps?.Humanlike == true && GetCompRJW(pawn) != null;

    public static void CopyData(Pawn source, Pawn destination)
    {
        if (!IsAvailableForPawn(source) || !IsAvailableForPawn(destination) || orientationField == null)
            return;

        var sourceComp = GetCompRJW(source);
        var destinationComp = GetCompRJW(destination);
        var orientation = orientationField.GetValue(sourceComp);
        if (orientation != null && orientationType?.IsInstanceOfType(orientation) == true)
            orientationField.SetValue(destinationComp, orientation);
    }

    public static string GetOrientation(Pawn pawn)
    {
        var comp = GetCompRJW(pawn);
        var value = orientationField?.GetValue(comp);
        return value?.ToString() ?? "None";
    }

    public static void SetOrientation(Pawn pawn, string orientationName)
    {
        var comp = GetCompRJW(pawn);
        if (comp == null || orientationType == null || orientationField == null || orientationName.NullOrEmpty())
            return;

        if (Enum.GetNames(orientationType).Contains(orientationName))
            orientationField.SetValue(comp, Enum.Parse(orientationType, orientationName));
    }

    public static List<string> OrientationNames() =>
        orientationType == null ? new List<string>() : Enum.GetNames(orientationType).ToList();

    private static object GetCompRJW(Pawn pawn)
    {
        if (pawn == null || compRJWType == null)
            return null;

        foreach (var comp in pawn.AllComps)
        {
            if (compRJWType.IsInstanceOfType(comp))
                return comp;
        }

        return null;
    }
}
