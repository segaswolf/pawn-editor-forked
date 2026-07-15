using System;
using System.IO;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// Partial — Portrait rendering and graphics helpers.
public static partial class PawnEditor
{
    public static RenderTexture GetPawnTex(Pawn pawn, Vector2 portraitSize, Rot4 dir, Vector3 cameraOffset = default, float cameraZoom = 1f)
    {
        // PortraitsCache.Get returns a CACHED RenderTexture for a given (pawn, size, dir, ...) or
        // renders one on a miss — the fetch itself is a cheap cache hit, not churn.
        //
        // The "PER-FRAME ALLOCATOR" flag was the profiler wrapper, NOT the fetch: writing
        // Measure(..., () => Get(...)) builds a closure + delegate on EVERY call (the lambda captures
        // the 5 args), and that happens even when profiling is OFF because the lambda is constructed
        // as an argument before Measure can early-out. Called per visible pawn per frame, that closure
        // was the entire per-frame allocation. Fast-path around it: when profiling is off we call the
        // cache directly and allocate nothing here. When it's on we keep the measured path.
        if (!PawnEditorProfiler.Enabled)
            return PortraitsCache.Get(pawn, portraitSize, dir, cameraOffset, cameraZoom,
                renderHeadgear: RenderHeadgear, renderClothes: RenderClothes, stylingStation: true);

        return PawnEditorProfiler.Measure("Portrait.GetPawnTex", PawnEditorProfiler.Cadence.PerFrame, () =>
            PortraitsCache.Get(pawn, portraitSize, dir, cameraOffset, cameraZoom,
                renderHeadgear: RenderHeadgear, renderClothes: RenderClothes, stylingStation: true));
    }

    public static void SavePawnTex(Pawn pawn, string path, Rot4 dir)
    {
        var tex = GetPawnTex(pawn, new(128, 128), dir);
        RenderTexture.active = tex;
        var tex2D = new Texture2D(tex.width, tex.width);
        tex2D.ReadPixels(new(0, 0, tex.width, tex.height), 0, 0);
        RenderTexture.active = null;
        tex2D.Apply(true, false);
        var bytes = tex2D.EncodeToPNG();
        File.WriteAllBytes(path, bytes);
    }

    public static void DrawPawnPortrait(Rect rect)
    {
        // Round the requested portrait size to whole pixels. PortraitsCache.Get keys its cached
        // RenderTextures by size, so if rect.size wobbles by sub-pixel amounts between frames
        // (layout rounding, scaling), each frame asks for a "new" size and renders a BRAND NEW
        // RenderTexture, while the slightly-different old ones linger in the cache. The profiler
        // caught this as GetPawnTex peaking at ~19 MB in a single frame. A stable, rounded size
        // means every frame hits the same cache entry instead of churning textures — which is
        // what was feeding the GC and dropping the GUI atlas (the black screen).
        var stableSize = new Vector2(Mathf.Round(rect.size.x), Mathf.Round(rect.size.y));
        var image = GetPawnTex(selectedPawn, stableSize, curRot);
        GUI.color = Command.LowLightBgColor;
        Widgets.DrawBox(rect);
        GUI.color = Color.white;
        GUI.DrawTexture(rect, Command.BGTex);
        // Draw the portrait, or a placeholder if the atlas was dropped (avoids black hole + spam).
        DrawPortraitOrPlaceholder(rect, image);
        if (Widgets.ButtonImage(rect.ContractedBy(8).RightPartPixels(16).TopPartPixels(16), TexUI.RotRightTex))
            curRot.Rotate(RotationDirection.Counterclockwise);

        if (Widgets.InfoCardButtonWorker(rect.ContractedBy(8).LeftPartPixels(16).TopPartPixels(16))) Find.WindowStack.Add(new Dialog_InfoCard(selectedPawn));
    }

    /// <summary>
    /// Draws a pawn portrait into <paramref name="rect"/>, falling back to a neutral placeholder
    /// (filled box) when the cached texture is null. The texture goes null when Unity's
    /// UnloadUnusedAssets pass drops the GUI atlas (a multi-second hitch on heavy modlists), which
    /// otherwise leaves a black hole AND spams "null texture passed to GUI.DrawTexture". This does
    /// NOT fix the underlying hitch (that's Unity reclaiming memory, outside our control); it just
    /// keeps our window looking like it's loading instead of broken, and avoids the per-draw spam.
    /// Returns true if the real portrait was drawn, false if the placeholder was used.
    /// </summary>
    public static bool DrawPortraitOrPlaceholder(Rect rect, RenderTexture tex)
    {
        if (tex != null)
        {
            GUI.DrawTexture(rect, tex);
            return true;
        }
        // Placeholder: subtle filled box so the slot reads as "loading", not as a black gap.
        var prev = GUI.color;
        GUI.color = new Color(prev.r, prev.g, prev.b, prev.a * 0.35f);
        GUI.DrawTexture(rect, BaseContent.GreyTex);
        GUI.color = prev;
        return false;
    }

    // ── Graphics helpers ──

    private static void EnsurePawnGraphicsInitialized(Pawn pawn)
    {
        if (pawn == null) return;

        try
        {
            var renderer = pawn.drawer?.renderer;
            if (renderer == null) return;
            var ensure = AccessTools.Method(renderer.GetType(), "EnsureGraphicsInitialized", Type.EmptyTypes);
            ensure?.Invoke(renderer, null);
            renderer.SetAllGraphicsDirty();
        }
        catch
        {
        }
    }

    private static void NotifyColonistBarsDirty()
    {
        try
        {
            Find.ColonistBar.MarkColonistsDirty();
        }
        catch
        {
        }

        try
        {
            var tgType = AccessTools.TypeByName("TacticalGroups.TacticalColonistBar");
            var markDirty = tgType == null ? null : AccessTools.Method(tgType, "MarkColonistsDirty", Type.EmptyTypes);
            if (markDirty != null && markDirty.IsStatic)
                markDirty.Invoke(null, null);
        }
        catch
        {
        }
    }
}
