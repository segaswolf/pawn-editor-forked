using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[StaticConstructorOnStartup]
public static class TexPawnEditor
{
    public static readonly Texture2D OpenPawnEditor = ContentFinder<Texture2D>.Get("UI/Buttons/DevRoot/OpenPawnEditor");
    public static readonly Texture2D ArrowLeftHalf = ContentFinder<Texture2D>.Get("UI/Buttons/ArrowLeft");
    public static readonly Texture2D ArrowRightHalf = ContentFinder<Texture2D>.Get("UI/Buttons/ArrowRight");
    public static readonly Texture2D ArrowLeftHalfDouble = ContentFinder<Texture2D>.Get("UI/Buttons/ArrowLeftDouble");
    public static readonly Texture2D ArrowRightHalfDouble = ContentFinder<Texture2D>.Get("UI/Buttons/ArrowRightDouble");
    public static readonly Texture2D PassionEmptyTex = ContentFinder<Texture2D>.Get("UI/Buttons/Icons/PassionEmpty");
    public static readonly Texture2D GoToPawn = ContentFinder<Texture2D>.Get("UI/Buttons/GoToPawn");
    public static readonly Texture2D Randomize = ContentFinder<Texture2D>.Get("UI/Buttons/Randomize");
    public static readonly Texture2D Save = ContentFinder<Texture2D>.Get("UI/Buttons/Save");
    public static readonly Texture2D GendersTex = ContentFinder<Texture2D>.Get("UI/Icons/Gender/Genders");
    public static readonly Texture2D InvertFilter = ContentFinder<Texture2D>.Get("UI/Buttons/InvertFilter_a");
    public static readonly Texture2D InvertFilterActive = ContentFinder<Texture2D>.Get("UI/Buttons/InvertFilter_b");
    public static readonly Dictionary<BodyTypeDef, Texture2D> BodyTypeIcons;
    public static readonly Texture2D SkillBarBGTex = SolidColorMaterials.NewSolidColorTexture(0.137255f, 0.145098f, 0.156863f, 1);

    static TexPawnEditor()
    {
        Shader s = ShaderDatabase.CutoutSkinColorOverride;
        try { s = ShaderUtility.GetSkinShaderAbstract(true, false); } catch { }

        // This used to be a single ToDictionary over every BodyTypeDef with no guards. One modded
        // BodyTypeDef with a null/blank bodyNakedGraphicPath made ContentFinder look up a null key,
        // which threw inside this static constructor. A type initializer that throws is POISONED for
        // the rest of the session: every later use of TexPawnEditor rethrows, so the whole editor UI
        // and our dev-mode buttons silently vanished and the log spammed
        // "TypeInitializationException ... Parameter name: key" forever.
        // Now each icon is built independently: a broken def loses its icon and says so once, and
        // everything else keeps working.
        BodyTypeIcons = new Dictionary<BodyTypeDef, Texture2D>();
        var skipped = new List<string>();

        foreach (var def in DefDatabase<BodyTypeDef>.AllDefsListForReading)
        {
            if (def == null) continue;

            if (def.bodyNakedGraphicPath.NullOrEmpty())
            {
                skipped.Add(def.defName ?? "(unnamed)");
                continue;
            }

            try
            {
                var graphic = GraphicDatabase.Get<Graphic_Multi>(def.bodyNakedGraphicPath, s, Vector2.one, Color.white);
                if (graphic?.MatSouth?.mainTexture is Texture2D tex) BodyTypeIcons[def] = tex;
                else skipped.Add(def.defName ?? "(unnamed)");
            }
            catch (System.Exception ex)
            {
                skipped.Add($"{def.defName ?? "(unnamed)"} ({ex.GetType().Name})");
            }
        }

        if (skipped.Count > 0)
            Log.Warning($"[Pawn Editor] {skipped.Count} body type(s) have no usable graphic and will show a "
                        + $"placeholder icon: {string.Join(", ", skipped)}. This comes from the mod that adds "
                        + "them, not from Pawn Editor, but the editor keeps working.");
    }

    /// <summary>
    /// Icon for a body type, with a placeholder for the ones we could not build. Always use this instead
    /// of indexing BodyTypeIcons directly: a missing key would throw in the middle of drawing the UI.
    /// </summary>
    public static Texture2D GetBodyTypeIcon(BodyTypeDef def)
    {
        if (def != null && BodyTypeIcons.TryGetValue(def, out var tex) && tex != null) return tex;
        return BaseContent.BadTex;
    }
}
