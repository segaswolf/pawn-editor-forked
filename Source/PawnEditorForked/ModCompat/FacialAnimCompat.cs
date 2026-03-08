using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using Verse;

namespace PawnEditor;

[ModCompat("Nals.FacialAnimation")]
public static class FacialAnimCompat
{
    public static bool Active;
    public static string Name = "Facial Animations";
    private static Type faceTypeDef;
    public static List<Def> FaceTypeDefs;

    // Controller comp types — each is a separate ThingComp on the pawn
    private static Type eyeballControllerType;
    private static Type browControllerType;
    private static Type lidControllerType;
    private static Type mouthControllerType;
    private static Type skinControllerType;
    private static Type headControllerType;
    private static Type drawFaceCompType;

    // Def types for resolving saved defs
    private static Type eyeballTypeDef;
    private static Type browTypeDef;
    private static Type lidTypeDef;
    private static Type mouthTypeDef;
    private static Type skinTypeDef;
    private static Type faHeadTypeDef; // FA's HeadTypeDef, not vanilla

    // Paired arrays for iteration
    private static readonly string[] XmlNames = {
        "eyeballType", "browType", "lidType",
        "mouthType", "skinType", "faHeadType"
    };

    [UsedImplicitly]
    public static void Activate()
    {
        faceTypeDef = AccessTools.TypeByName("FacialAnimation.FaceTypeDef");
        FaceTypeDefs = GenDefDatabase.GetAllDefsInDatabaseForDef(faceTypeDef).ToList();

        // Each controller is its own ThingComp on the pawn
        eyeballControllerType = AccessTools.TypeByName("EyeballControllerComp");
        browControllerType = AccessTools.TypeByName("BrowControllerComp");
        lidControllerType = AccessTools.TypeByName("LidControllerComp");
        mouthControllerType = AccessTools.TypeByName("MouthControllerComp");
        skinControllerType = AccessTools.TypeByName("SkinControllerComp");
        headControllerType = AccessTools.TypeByName("HeadControllerComp");
        drawFaceCompType = AccessTools.TypeByName("DrawFaceGraphicsComp");

        // Def types
        eyeballTypeDef = AccessTools.TypeByName("FacialAnimation.EyeballTypeDef");
        browTypeDef = AccessTools.TypeByName("FacialAnimation.BrowTypeDef");
        lidTypeDef = AccessTools.TypeByName("FacialAnimation.LidTypeDef");
        mouthTypeDef = AccessTools.TypeByName("FacialAnimation.MouthTypeDef");
        skinTypeDef = AccessTools.TypeByName("FacialAnimation.SkinTypeDef");
        faHeadTypeDef = AccessTools.TypeByName("FacialAnimation.HeadTypeDef");

        if (Verse.Prefs.DevMode)
            Log.Message($"[Pawn Editor] FA controllers: eye={eyeballControllerType?.Name ?? "?"}, " +
                $"head={headControllerType?.Name ?? "?"}, draw={drawFaceCompType?.Name ?? "?"}");
    }

    private static Type[] ControllerTypes =>
        new[] { eyeballControllerType, browControllerType, lidControllerType,
                mouthControllerType, skinControllerType,
                headControllerType };

    private static Type[] DefTypes =>
        new[] { eyeballTypeDef, browTypeDef, lidTypeDef,
                mouthTypeDef, skinTypeDef, faHeadTypeDef };

    // ── Helpers ──

    /// <summary>
    /// Find a specific ThingComp on a pawn by type.
    /// </summary>
    private static object FindComp(Pawn pawn, Type compType)
    {
        if (pawn == null || compType == null) return null;
        try
        {
            foreach (var comp in pawn.AllComps)
                if (compType.IsInstanceOfType(comp))
                    return comp;
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] FA FindComp({compType?.Name}): {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Get faceType from a controller comp (field name confirmed via reflection dump).
    /// Lives in ControllerBaseComp&lt;T&gt; as "faceType".
    /// </summary>
    private static Def GetCurrentType(object controllerComp)
    {
        if (controllerComp == null) return null;
        try
        {
            var type = controllerComp.GetType();
            while (type != null)
            {
                var field = type.GetField("faceType",
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field.GetValue(controllerComp) as Def;
                type = type.BaseType;
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] FA GetCurrentType: {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Set faceType on a controller comp (field name confirmed via reflection dump).
    /// </summary>
    private static void SetCurrentType(object controllerComp, Def value)
    {
        if (controllerComp == null || value == null) return;
        try
        {
            var type = controllerComp.GetType();
            while (type != null)
            {
                var field = type.GetField("faceType",
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(controllerComp, value);
                    return;
                }
                type = type.BaseType;
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] FA SetCurrentType: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the FaceType def from DrawFaceGraphicsComp.
    /// </summary>
    private static Def GetFaceType(Pawn pawn)
    {
        try
        {
            var comp = FindComp(pawn, drawFaceCompType);
            if (comp == null) return null;
            var prop = AccessTools.Property(comp.GetType(), "FaceType");
            if (prop != null) return prop.GetValue(comp) as Def;
            var field = AccessTools.Field(comp.GetType(), "faceType");
            if (field != null) return field.GetValue(comp) as Def;
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] FA GetFaceType: {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Set the FaceType def on DrawFaceGraphicsComp.
    /// </summary>
    private static void SetFaceType(Pawn pawn, Def faceType)
    {
        try
        {
            var comp = FindComp(pawn, drawFaceCompType);
            if (comp == null) return;
            var prop = AccessTools.Property(comp.GetType(), "FaceType");
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(comp, faceType);
                return;
            }
            var field = AccessTools.Field(comp.GetType(), "faceType");
            if (field != null)
                field.SetValue(comp, faceType);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] FA SetFaceType: {ex.Message}"); }
    }

    /// <summary>
    /// Get eye color from EyeballControllerComp.
    /// Field confirmed as "color" in ControllerBaseComp&lt;T&gt;.
    /// </summary>
    private static Color? GetEyeballColor(Pawn pawn)
    {
        try
        {
            var comp = FindComp(pawn, eyeballControllerType);
            if (comp == null) return null;
            var type = comp.GetType();
            while (type != null)
            {
                var field = type.GetField("color",
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return (Color)field.GetValue(comp);
                type = type.BaseType;
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] FA GetEyeballColor: {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Set eye color on EyeballControllerComp.
    /// </summary>
    private static void SetEyeballColor(Pawn pawn, Color color)
    {
        try
        {
            var comp = FindComp(pawn, eyeballControllerType);
            if (comp == null) return;
            var type = comp.GetType();
            while (type != null)
            {
                var field = type.GetField("color",
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(comp, color);
                    return;
                }
                type = type.BaseType;
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] FA SetEyeballColor: {ex.Message}"); }
    }

    // ── Public API ──

    /// <summary>Save all FA data to XML.</summary>
    public static void WriteFacialData(XmlWriter w, Pawn pawn)
    {
        Log.Warning($"[Pawn Editor] FA WriteFacialData called for {pawn?.LabelShort ?? "null"}, Active={Active}");
        if (!Active) return;

        var controllerTypes = ControllerTypes;
        var hasAnyData = false;

        // DEBUG: Dump all comps to find where FA data actually lives
        var faComps = new System.Text.StringBuilder();
        foreach (var comp in pawn.AllComps)
        {
            var typeName = comp.GetType().FullName ?? comp.GetType().Name;
            if (typeName.Contains("Facial") || typeName.Contains("Face") || typeName.Contains("Controller")
                || typeName.Contains("Eye") || typeName.Contains("Brow") || typeName.Contains("Lid")
                || typeName.Contains("Mouth") || typeName.Contains("Skin") || typeName.Contains("Head")
                || typeName.Contains("Draw"))
                faComps.Append($" [{typeName}]");
        }
        if (faComps.Length > 0)
            if (Verse.Prefs.DevMode)
                Log.Message($"[Pawn Editor] FA comps on {pawn.LabelShort}:{faComps}");
        else
            Log.Warning($"[Pawn Editor] FA: No face-related comps found on {pawn.LabelShort}. Total comps: {pawn.AllComps.Count}");

        // Pre-check: do we have any data?
        var foundComps = new System.Text.StringBuilder();
        for (int i = 0; i < controllerTypes.Length; i++)
        {
            var comp = FindComp(pawn, controllerTypes[i]);
            if (comp != null)
            {
                var ct = GetCurrentType(comp);
                foundComps.Append($" {XmlNames[i]}={ct?.defName ?? "null"}");
                if (ct != null) hasAnyData = true;
            }
            else
            {
                foundComps.Append($" {XmlNames[i]}=COMP_NOT_FOUND");
            }
        }
        if (foundComps.Length > 0)
            if (Verse.Prefs.DevMode)
                Log.Message($"[Pawn Editor] FA data:{foundComps}");
        else
            Log.Warning($"[Pawn Editor] FA: No controller comps found via FindComp");

        if (!hasAnyData && GetFaceType(pawn) == null && !GetEyeballColor(pawn).HasValue)
            return; // Don't write empty element

        try
        {
            w.WriteStartElement("facialAnimation");
            w.WriteAttributeString("MayRequire", "Nals.FacialAnimation");

            // FaceType
            var ft = GetFaceType(pawn);
            if (ft != null)
                w.WriteAttributeString("faceType", ft.defName);

            // Each controller
            for (int i = 0; i < controllerTypes.Length; i++)
            {
                var comp = FindComp(pawn, controllerTypes[i]);
                var typeDef = GetCurrentType(comp);
                if (typeDef != null)
                    w.WriteAttributeString(XmlNames[i], typeDef.defName);
            }

            // Eye color
            var eyeColor = GetEyeballColor(pawn);
            if (eyeColor.HasValue)
            {
                var c = eyeColor.Value;
                w.WriteAttributeString("eyeColorR", c.r.ToString("F3"));
                w.WriteAttributeString("eyeColorG", c.g.ToString("F3"));
                w.WriteAttributeString("eyeColorB", c.b.ToString("F3"));
            }

            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] FA WriteFacialData: {ex.Message}"); }
    }

    /// <summary>Load all FA data from XML.</summary>
    public static void LoadFacialData(Pawn pawn, XmlNode root)
    {
        if (!Active) return;
        var faNode = root.SelectSingleNode("facialAnimation");
        if (faNode == null) return;

        try
        {
            // Restore FaceType first
            var faceTypeName = faNode.Attributes?["faceType"]?.Value;
            if (!faceTypeName.NullOrEmpty() && faceTypeDef != null)
            {
                var def = GenDefDatabase.GetAllDefsInDatabaseForDef(faceTypeDef)
                    .FirstOrDefault(d => d.defName == faceTypeName);
                if (def != null) SetFaceType(pawn, def);
            }

            // Each controller
            var controllerTypes = ControllerTypes;
            var defTypes = DefTypes;
            for (int i = 0; i < controllerTypes.Length; i++)
            {
                var defName = faNode.Attributes?[XmlNames[i]]?.Value;
                if (defName.NullOrEmpty() || defTypes[i] == null) continue;

                var def = GenDefDatabase.GetAllDefsInDatabaseForDef(defTypes[i])
                    .FirstOrDefault(d => d.defName == defName);
                if (def == null) continue;

                var comp = FindComp(pawn, controllerTypes[i]);
                SetCurrentType(comp, def);
            }

            // Eye color
            var rStr = faNode.Attributes?["eyeColorR"]?.Value;
            var gStr = faNode.Attributes?["eyeColorG"]?.Value;
            var bStr = faNode.Attributes?["eyeColorB"]?.Value;
            if (!rStr.NullOrEmpty() && !gStr.NullOrEmpty() && !bStr.NullOrEmpty())
                if (float.TryParse(rStr, out var r) && float.TryParse(gStr, out var g) && float.TryParse(bStr, out var b))
                    SetEyeballColor(pawn, new Color(r, g, b));
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] FA LoadFacialData: {ex.Message}"); }
    }

    /// <summary>Copy all FA data from one pawn to another.</summary>
    public static void CopyFacialData(Pawn src, Pawn dst)
    {
        if (!Active) return;

        try
        {
            // Copy FaceType
            var ft = GetFaceType(src);
            if (ft != null) SetFaceType(dst, ft);

            // Copy each controller
            var controllerTypes = ControllerTypes;
            for (int i = 0; i < controllerTypes.Length; i++)
            {
                var srcComp = FindComp(src, controllerTypes[i]);
                var dstComp = FindComp(dst, controllerTypes[i]);
                var typeDef = GetCurrentType(srcComp);
                if (typeDef != null)
                    SetCurrentType(dstComp, typeDef);
            }

            // Copy eye color
            var color = GetEyeballColor(src);
            if (color.HasValue) SetEyeballColor(dst, color.Value);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] FA CopyFacialData: {ex.Message}"); }
    }
}
