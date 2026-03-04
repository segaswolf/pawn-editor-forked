using System;
using System.Collections.Generic;
using System.Linq;
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

    // Cached reflection types
    private static Type compType;           // FacialAnimationControllerComp
    private static Type eyeballTypeDef;     // EyeballTypeDef
    private static Type browTypeDef;        // BrowTypeDef
    private static Type lidTypeDef;         // LidTypeDef
    private static Type lidOptionTypeDef;   // LidOptionTypeDef
    private static Type mouthTypeDef;       // MouthTypeDef
    private static Type skinTypeDef;        // SkinTypeDef

    // Controller field names on the main comp
    private static readonly string[] ControllerNames = {
        "eyeballController", "browController", "lidController",
        "lidOptionController", "mouthController", "skinController"
    };

    // Matching TypeDef type names
    private static readonly string[] TypeDefNames = {
        "FacialAnimation.EyeballTypeDef", "FacialAnimation.BrowTypeDef",
        "FacialAnimation.LidTypeDef", "FacialAnimation.LidOptionTypeDef",
        "FacialAnimation.MouthTypeDef", "FacialAnimation.SkinTypeDef"
    };

    // XML element names for save/load
    private static readonly string[] XmlNames = {
        "eyeballType", "browType", "lidType",
        "lidOptionType", "mouthType", "skinType"
    };

    [UsedImplicitly]
    public static void Activate()
    {
        faceTypeDef = AccessTools.TypeByName("FacialAnimation.FaceTypeDef");
        FaceTypeDefs = GenDefDatabase.GetAllDefsInDatabaseForDef(faceTypeDef).ToList();

        // Cache types for save/load
        compType = AccessTools.TypeByName("FacialAnimation.FacialAnimationControllerComp");
        eyeballTypeDef = AccessTools.TypeByName("FacialAnimation.EyeballTypeDef");
        browTypeDef = AccessTools.TypeByName("FacialAnimation.BrowTypeDef");
        lidTypeDef = AccessTools.TypeByName("FacialAnimation.LidTypeDef");
        lidOptionTypeDef = AccessTools.TypeByName("FacialAnimation.LidOptionTypeDef");
        mouthTypeDef = AccessTools.TypeByName("FacialAnimation.MouthTypeDef");
        skinTypeDef = AccessTools.TypeByName("FacialAnimation.SkinTypeDef");
    }

    /// <summary>
    /// Get the FacialAnimationControllerComp from a pawn (via reflection).
    /// </summary>
    private static object GetFAComp(Pawn pawn)
    {
        if (compType == null || pawn == null) return null;
        try
        {
            foreach (var comp in pawn.AllComps)
            {
                if (compType.IsInstanceOfType(comp))
                    return comp;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Read the currentType def from a controller field on the FA comp.
    /// </summary>
    private static Def GetControllerCurrentType(object faComp, string controllerFieldName)
    {
        try
        {
            var controllerField = AccessTools.Field(compType, controllerFieldName);
            if (controllerField == null) return null;
            var controller = controllerField.GetValue(faComp);
            if (controller == null) return null;

            var currentTypeField = AccessTools.Field(controller.GetType(), "currentType");
            if (currentTypeField == null) return null;
            return currentTypeField.GetValue(controller) as Def;
        }
        catch { return null; }
    }

    /// <summary>
    /// Set the currentType def on a controller field on the FA comp.
    /// </summary>
    private static void SetControllerCurrentType(object faComp, string controllerFieldName, Def typeDef)
    {
        try
        {
            var controllerField = AccessTools.Field(compType, controllerFieldName);
            if (controllerField == null) return;
            var controller = controllerField.GetValue(faComp);
            if (controller == null) return;

            var currentTypeField = AccessTools.Field(controller.GetType(), "currentType");
            if (currentTypeField == null) return;
            currentTypeField.SetValue(controller, typeDef);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] FA SetControllerCurrentType({controllerFieldName}): {ex.Message}");
        }
    }

    /// <summary>
    /// Read the eyeball color from the FA comp.
    /// </summary>
    private static Color? GetEyeballColor(object faComp)
    {
        try
        {
            var controllerField = AccessTools.Field(compType, "eyeballController");
            if (controllerField == null) return null;
            var controller = controllerField.GetValue(faComp);
            if (controller == null) return null;

            var colorField = AccessTools.Field(controller.GetType(), "eyeballColor");
            if (colorField == null) return null;
            return (Color)colorField.GetValue(controller);
        }
        catch { return null; }
    }

    /// <summary>
    /// Set the eyeball color on the FA comp.
    /// </summary>
    private static void SetEyeballColor(object faComp, Color color)
    {
        try
        {
            var controllerField = AccessTools.Field(compType, "eyeballController");
            if (controllerField == null) return;
            var controller = controllerField.GetValue(faComp);
            if (controller == null) return;

            var colorField = AccessTools.Field(controller.GetType(), "eyeballColor");
            if (colorField == null) return;
            colorField.SetValue(controller, color);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] FA SetEyeballColor: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the FaceType def from the main comp.
    /// </summary>
    private static Def GetFaceType(object faComp)
    {
        try
        {
            var prop = AccessTools.Property(compType, "FaceType");
            if (prop != null) return prop.GetValue(faComp) as Def;
            var field = AccessTools.Field(compType, "faceType");
            if (field != null) return field.GetValue(faComp) as Def;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Set the FaceType def on the main comp.
    /// </summary>
    private static void SetFaceType(object faComp, Def faceType)
    {
        try
        {
            var prop = AccessTools.Property(compType, "FaceType");
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(faComp, faceType);
                return;
            }
            var field = AccessTools.Field(compType, "faceType");
            if (field != null)
                field.SetValue(faComp, faceType);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] FA SetFaceType: {ex.Message}");
        }
    }

    // ── Public API for Blueprint Save/Load ──

    /// <summary>
    /// Save all Facial Animation data for a pawn to XML.
    /// Called from PawnBlueprintSaveLoad.
    /// </summary>
    public static void WriteFacialData(XmlWriter w, Pawn pawn)
    {
        if (!Active) return;
        var faComp = GetFAComp(pawn);
        if (faComp == null) return;

        try
        {
            w.WriteStartElement("facialAnimation");
            w.WriteAttributeString("MayRequire", "Nals.FacialAnimation");

            // Save FaceType (master def)
            var faceType = GetFaceType(faComp);
            if (faceType != null)
                w.WriteAttributeString("faceType", faceType.defName);

            // Save each controller's currentType
            for (int i = 0; i < ControllerNames.Length; i++)
            {
                var typeDef = GetControllerCurrentType(faComp, ControllerNames[i]);
                if (typeDef != null)
                    w.WriteAttributeString(XmlNames[i], typeDef.defName);
            }

            // Save eyeball color
            var eyeColor = GetEyeballColor(faComp);
            if (eyeColor.HasValue)
            {
                var c = eyeColor.Value;
                w.WriteAttributeString("eyeColorR", c.r.ToString("F3"));
                w.WriteAttributeString("eyeColorG", c.g.ToString("F3"));
                w.WriteAttributeString("eyeColorB", c.b.ToString("F3"));
            }

            w.WriteEndElement();
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] FA WriteFacialData: {ex.Message}");
        }
    }

    /// <summary>
    /// Load all Facial Animation data for a pawn from XML.
    /// Called from PawnBlueprintSaveLoad.
    /// </summary>
    public static void LoadFacialData(Pawn pawn, XmlNode root)
    {
        if (!Active) return;
        var faNode = root.SelectSingleNode("facialAnimation");
        if (faNode == null) return;

        var faComp = GetFAComp(pawn);
        if (faComp == null) return;

        try
        {
            // Restore FaceType (master def) first
            var faceTypeName = faNode.Attributes?["faceType"]?.Value;
            if (!faceTypeName.NullOrEmpty() && faceTypeDef != null)
            {
                var def = GenDefDatabase.GetAllDefsInDatabaseForDef(faceTypeDef)
                    .FirstOrDefault(d => d.defName == faceTypeName);
                if (def != null)
                    SetFaceType(faComp, def);
            }

            // Restore each controller's currentType
            Type[] typeDefTypes = { eyeballTypeDef, browTypeDef, lidTypeDef, lidOptionTypeDef, mouthTypeDef, skinTypeDef };
            for (int i = 0; i < ControllerNames.Length; i++)
            {
                var defName = faNode.Attributes?[XmlNames[i]]?.Value;
                if (defName.NullOrEmpty() || typeDefTypes[i] == null) continue;

                var def = GenDefDatabase.GetAllDefsInDatabaseForDef(typeDefTypes[i])
                    .FirstOrDefault(d => d.defName == defName);
                if (def != null)
                    SetControllerCurrentType(faComp, ControllerNames[i], def);
            }

            // Restore eyeball color
            var rStr = faNode.Attributes?["eyeColorR"]?.Value;
            var gStr = faNode.Attributes?["eyeColorG"]?.Value;
            var bStr = faNode.Attributes?["eyeColorB"]?.Value;
            if (!rStr.NullOrEmpty() && !gStr.NullOrEmpty() && !bStr.NullOrEmpty())
            {
                if (float.TryParse(rStr, out var r) && float.TryParse(gStr, out var g) && float.TryParse(bStr, out var b))
                    SetEyeballColor(faComp, new Color(r, g, b));
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] FA LoadFacialData: {ex.Message}");
        }
    }

    /// <summary>
    /// Copy all Facial Animation data from one pawn to another.
    /// Called from PawnDuplicationUtility.
    /// </summary>
    public static void CopyFacialData(Pawn src, Pawn dst)
    {
        if (!Active) return;
        var srcComp = GetFAComp(src);
        var dstComp = GetFAComp(dst);
        if (srcComp == null || dstComp == null) return;

        try
        {
            // Copy FaceType
            var faceType = GetFaceType(srcComp);
            if (faceType != null)
                SetFaceType(dstComp, faceType);

            // Copy each controller
            for (int i = 0; i < ControllerNames.Length; i++)
            {
                var typeDef = GetControllerCurrentType(srcComp, ControllerNames[i]);
                if (typeDef != null)
                    SetControllerCurrentType(dstComp, ControllerNames[i], typeDef);
            }

            // Copy eyeball color
            var color = GetEyeballColor(srcComp);
            if (color.HasValue)
                SetEyeballColor(dstComp, color.Value);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] FA CopyFacialData: {ex.Message}");
        }
    }
}
