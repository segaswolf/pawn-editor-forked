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

    private static readonly List<FacePart> faceParts = new()
    {
        new("eyes", "PawnEditor.FA.Eyes", "Eyes", "FacialAnimation.EyeballControllerComp", "FacialAnimation.EyeballTypeDef"),
        new("brows", "PawnEditor.FA.Brows", "Brows", "FacialAnimation.BrowControllerComp", "FacialAnimation.BrowTypeDef"),
        new("lids", "PawnEditor.FA.Lids", "Lids", "FacialAnimation.LidControllerComp", "FacialAnimation.LidTypeDef"),
        new("mouth", "PawnEditor.FA.Mouth", "Mouth", "FacialAnimation.MouthControllerComp", "FacialAnimation.MouthTypeDef"),
        new("skin", "PawnEditor.FA.Skin", "Skin", "FacialAnimation.SkinControllerComp", "FacialAnimation.SkinTypeDef"),
        new("head", "PawnEditor.FA.Head", "Head", "FacialAnimation.HeadControllerComp", "FacialAnimation.HeadTypeDef")
    };

    private static Type faceTypeGeneratorGenericType;
    private static readonly Dictionary<string, Texture2D> faceTypeIcons = new();

    // v3.1: NL FA ships its own full face-editor window. Opening it (with the pawn set on its static
    // field) reuses the entire facial-animation UI instead of us rebuilding it.
    private static Type faceEditorWindowType;
    private static bool faceEditorResolved;

    private static Type FaceEditorWindowType()
    {
        if (!faceEditorResolved)
        {
            faceEditorWindowType = AccessTools.TypeByName("FacialAnimation.NL_SelectPartWindow");
            faceEditorResolved = true;
        }
        return faceEditorWindowType;
    }

    /// <summary>True if NL Facial Animation is present and its face editor can be opened.</summary>
    public static bool CanEditFace(Pawn pawn) => pawn != null && Active && FaceEditorWindowType() != null;

    /// <summary>Opens NL Facial Animation's own face editor window for this pawn (sets its static
    /// selectedPawn field, then adds the window to the stack).</summary>
    public static void OpenFaceEditor(Pawn pawn)
    {
        var t = FaceEditorWindowType();
        if (t == null || pawn == null) return;
        try
        {
            AccessTools.Field(t, "selectedPawn").SetValue(null, pawn);
            var win = (Window)Activator.CreateInstance(t);
            // Put it on the topmost layer so it opens IN FRONT of the appearance editor and the main
            // pawn editor (which can both stay open behind it) instead of getting stuck underneath.
            AccessTools.Field(typeof(Window), "layer")?.SetValue(win, WindowLayer.Super);
            Find.WindowStack.Add(win);
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] Open face editor failed: {ex.Message}"); }
    }

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

        faceTypeGeneratorGenericType = AccessTools.TypeByName("FacialAnimation.FaceTypeGenerator`1");
        foreach (var part in faceParts)
        {
            part.ControllerType = AccessTools.TypeByName(part.ControllerTypeName);
            part.DefType = AccessTools.TypeByName(part.DefTypeName);
        }

        // Each controller is its own ThingComp on the pawn
        eyeballControllerType = faceParts[0].ControllerType ?? AccessTools.TypeByName("EyeballControllerComp");
        browControllerType = faceParts[1].ControllerType ?? AccessTools.TypeByName("BrowControllerComp");
        lidControllerType = faceParts[2].ControllerType ?? AccessTools.TypeByName("LidControllerComp");
        mouthControllerType = faceParts[3].ControllerType ?? AccessTools.TypeByName("MouthControllerComp");
        skinControllerType = faceParts[4].ControllerType ?? AccessTools.TypeByName("SkinControllerComp");
        headControllerType = faceParts[5].ControllerType ?? AccessTools.TypeByName("HeadControllerComp");
        drawFaceCompType = AccessTools.TypeByName("FacialAnimation.DrawFaceGraphicsComp") ?? AccessTools.TypeByName("DrawFaceGraphicsComp");

        // Def types
        eyeballTypeDef = faceParts[0].DefType;
        browTypeDef = faceParts[1].DefType;
        lidTypeDef = faceParts[2].DefType;
        mouthTypeDef = faceParts[3].DefType;
        skinTypeDef = faceParts[4].DefType;
        faHeadTypeDef = faceParts[5].DefType;

        if (Verse.Prefs.DevMode)
            Log.Message($"[Pawn Editor] FA controllers: eye={eyeballControllerType?.Name ?? "?"}, " +
                $"head={headControllerType?.Name ?? "?"}, draw={drawFaceCompType?.Name ?? "?"}");
    }

    public static IReadOnlyList<FacePart> GetFaceParts() =>
        faceParts.Where(part => part.ControllerType != null && part.DefType != null).ToList();

    public static bool HasFaceControls(Pawn pawn) =>
        pawn != null && GetFaceParts().Any(part => FindComp(pawn, part.ControllerType) != null);

    public static IEnumerable<Def> GetAllOptionDefs(Pawn pawn) =>
        GetFaceParts().SelectMany(part => GetApplicableFaceTypeDefs(pawn, part)).Distinct();

    public static List<Def> GetApplicableFaceTypeDefs(Pawn pawn, FacePart part)
    {
        if (pawn == null || part?.DefType == null)
            return new List<Def>();

        var generatedDefs = TryGetGeneratedFaceDefs(pawn, part);
        if (!generatedDefs.NullOrEmpty())
            return generatedDefs;

        return GenDefDatabase.GetAllDefsInDatabaseForDef(part.DefType)
            .OfType<Def>()
            .OrderBy(def => def.label ?? def.defName)
            .ToList();
    }

    public static Def GetFaceType(Pawn pawn, FacePart part)
    {
        if (pawn == null || part == null)
            return null;

        var defName = GetFaceTypeDefName(pawn, part);
        return defName.NullOrEmpty()
            ? null
            : GenDefDatabase.GetAllDefsInDatabaseForDef(part.DefType)
                .OfType<Def>()
                .FirstOrDefault(def => def.defName == defName);
    }

    public static string GetFaceTypeDefName(Pawn pawn, FacePart part)
    {
        var comp = FindComp(pawn, part?.ControllerType);
        if (comp == null)
            return null;

        var propertyValue = AccessTools.Property(comp.GetType(), "FaceTypeDefName")?.GetValue(comp) as string;
        return propertyValue.NullOrEmpty() ? GetCurrentType(comp)?.defName : propertyValue;
    }

    public static void SetFaceType(Pawn pawn, FacePart part, Def def)
    {
        if (pawn == null || part == null || def == null)
            return;

        var comp = FindComp(pawn, part.ControllerType);
        if (comp == null)
            return;

        var property = AccessTools.Property(comp.GetType(), "FaceTypeDefName");
        if (property is { CanWrite: true })
            property.SetValue(comp, def.defName);
        else
            SetCurrentType(comp, def);

        AccessTools.Method(comp.GetType(), "SetDirty")?.Invoke(comp, Array.Empty<object>());
        PawnEditor.RefreshPawnGraphics(pawn);
    }

    public static Color? GetEyeColor(Pawn pawn) => GetEyeballColor(pawn);

    public static void SetEyeColor(Pawn pawn, Color color)
    {
        var comp = FindComp(pawn, eyeballControllerType);
        if (comp == null)
            return;

        SetEyeballColor(pawn, color);
        AccessTools.Method(comp.GetType(), "SetDirty")?.Invoke(comp, Array.Empty<object>());
        PawnEditor.RefreshPawnGraphics(pawn);
    }

    public static Color? GetSecondEyeColor(Pawn pawn)
    {
        var comp = FindComp(pawn, eyeballControllerType);
        if (comp == null)
            return null;

        var property = AccessTools.Property(comp.GetType(), "FaceSecondColor");
        if (property?.GetValue(comp) is Color color)
            return color;

        return FindField(comp.GetType(), "faceSecondColor")?.GetValue(comp) is Color fieldColor ? fieldColor : null;
    }

    public static void SetSecondEyeColor(Pawn pawn, Color color)
    {
        var comp = FindComp(pawn, eyeballControllerType);
        if (comp == null)
            return;

        var property = AccessTools.Property(comp.GetType(), "FaceSecondColor");
        if (property is { CanWrite: true })
            property.SetValue(comp, color);
        else
        {
            var field = FindField(comp.GetType(), "faceSecondColor");
            if (field == null)
                return;
            field.SetValue(comp, color);
        }

        AccessTools.Method(comp.GetType(), "SetDirty")?.Invoke(comp, Array.Empty<object>());
        PawnEditor.RefreshPawnGraphics(pawn);
    }

    public static Texture2D GetFaceTypeIcon(Def def, Pawn pawn)
    {
        if (def == null)
            return null;

        var texPath = FindField(def.GetType(), "texPath")?.GetValue(def) as string;
        if (texPath.NullOrEmpty())
            return null;

        var useUnisexPath = FindField(def.GetType(), "enableUnisexTexPath")?.GetValue(def) is bool value && value;
        var genderFolder = useUnisexPath ? "Unisex" : (pawn?.gender ?? Gender.Female).ToString();
        var cacheKey = texPath + "/" + genderFolder;
        if (faceTypeIcons.TryGetValue(cacheKey, out var texture))
            return texture;

        texture = LoadFaceTypeIcon(texPath, genderFolder);
        if (texture == null && genderFolder != "Unisex")
            texture = LoadFaceTypeIcon(texPath, "Unisex");
        if (texture == null && genderFolder != "Female")
            texture = LoadFaceTypeIcon(texPath, "Female");
        if (texture == null && genderFolder != "Male")
            texture = LoadFaceTypeIcon(texPath, "Male");

        faceTypeIcons[cacheKey] = texture;
        return texture;
    }

    private static List<Def> TryGetGeneratedFaceDefs(Pawn pawn, FacePart part)
    {
        if (faceTypeGeneratorGenericType == null)
            return null;

        var generatorType = faceTypeGeneratorGenericType.MakeGenericType(part.DefType);
        var method = AccessTools.Method(generatorType, "GetApplicableFaceTypeDefsForRaceConsideringGenes", new[] { typeof(Pawn) });
        if (method == null)
            return null;

        var instance = method.IsStatic ? null : Activator.CreateInstance(generatorType);
        if (method.Invoke(instance, new object[] { pawn }) is not System.Collections.IEnumerable enumerable)
            return null;

        return enumerable.OfType<Def>()
            .OrderBy(def => def.label ?? def.defName)
            .ToList();
    }

    private static Texture2D LoadFaceTypeIcon(string texPath, string genderFolder) =>
        ContentFinder<Texture2D>.Get(texPath + "/" + genderFolder + "/normal_south", reportFailure: false);

    private static FieldInfo FindField(Type type, string fieldName)
    {
        while (type != null)
        {
            var field = type.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;

            type = type.BaseType;
        }

        return null;
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
        if (!Active) return;

        var controllerTypes = ControllerTypes;
        var hasAnyData = false;
        for (int i = 0; i < controllerTypes.Length; i++)
        {
            var comp = FindComp(pawn, controllerTypes[i]);
            if (comp != null && GetCurrentType(comp) != null) { hasAnyData = true; break; }
        }

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

    public sealed class FacePart
    {
        public readonly string ControllerTypeName;
        public readonly string DefTypeName;
        public readonly string Key;
        public readonly string LabelFallback;
        public readonly string LabelKey;

        internal Type ControllerType;
        internal Type DefType;

        public FacePart(string key, string labelKey, string labelFallback, string controllerTypeName, string defTypeName)
        {
            Key = key;
            LabelKey = labelKey;
            LabelFallback = labelFallback;
            ControllerTypeName = controllerTypeName;
            DefTypeName = defTypeName;
        }

        public string Label
        {
            get
            {
                var translated = LabelKey.Translate().ToString();
                return translated == LabelKey ? LabelFallback : translated;
            }
        }
    }
}
