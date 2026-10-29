using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>A pawn's xenotype icon, without the error-every-frame that a missing icon texture causes.</summary>
/// <remarks>
/// <para>
/// Vanilla <c>Pawn_GeneTracker.XenotypeIcon</c> returns <c>XenotypeIconDef.Icon</c> for custom xenotypes, and that
/// property only caches a texture it actually found. When the icon def exists but its texture does not (its mod was
/// removed, or the mod's textures are not packed for 1.7), <c>ContentFinder</c> is asked again on every call and logs
/// a red error each time. Drawn in a button, that is one error per frame.
/// </para>
/// <para>
/// Here each icon def is looked up once, a missing texture is reported once with the def and its mod, and the basic
/// custom-xenotype icon is shown in its place. Xenotypes without a custom icon go through vanilla untouched.
/// </para>
/// </remarks>
public static class XenotypeIconUtility
{
    private static readonly Dictionary<XenotypeIconDef, Texture2D> iconByDef = new();

    /// <summary>The icon to draw for <paramref name="pawn"/>'s xenotype; null without genes or Biotech, as vanilla.</summary>
    public static Texture2D IconFor(Pawn pawn)
    {
        var genes = pawn?.genes;
        if (genes == null || !ModsConfig.BiotechActive) return null;

        var iconDef = genes.iconDef;
        return iconDef == null ? genes.XenotypeIcon : IconFor(iconDef);
    }

    /// <summary>The texture of <paramref name="iconDef"/>, or the basic xenotype icon if its texture is missing.</summary>
    public static Texture2D IconFor(XenotypeIconDef iconDef)
    {
        if (iconByDef.TryGetValue(iconDef, out var icon)) return icon;

        icon = ContentFinder<Texture2D>.Get(iconDef.texPath, reportFailure: false);
        if (icon == null)
        {
            Log.Warning($"[Pawn Editor] Xenotype icon '{iconDef.defName}' from {iconDef.modContentPack?.Name ?? "an unknown mod"} "
                        + $"has no texture at '{iconDef.texPath}'. Showing the basic xenotype icon instead.");
            icon = XenotypeIconDefOf.Basic.Icon;
        }

        iconByDef[iconDef] = icon;
        return icon;
    }
}
