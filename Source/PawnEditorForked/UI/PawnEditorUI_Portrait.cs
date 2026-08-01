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

    public static void DrawInteractivePawnPreview(Rect portraitRect, Pawn pawn, ref bool draggingPreview, ref Rot4 previewRotation,
        ref Vector3 previewCameraOffset, ref float previewZoom)
    {
        if (pawn == null || portraitRect.width <= 1f || portraitRect.height <= 1f)
            return;

        GUI.color = Color.white;
        var rotateRect = new Rect(portraitRect.x + 4f, portraitRect.y + 4f, 24f, 24f);
        var apparelRect = new Rect(rotateRect.xMax + 4f, rotateRect.y, 24f, 24f);
        var headgearRect = new Rect(apparelRect.xMax + 4f, rotateRect.y, 24f, 24f);
        var currentEvent = Event.current;
        var pointerInPreview = portraitRect.Contains(currentEvent.mousePosition)
                               && !rotateRect.Contains(currentEvent.mousePosition)
                               && !apparelRect.Contains(currentEvent.mousePosition)
                               && !headgearRect.Contains(currentEvent.mousePosition);

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && pointerInPreview)
        {
            draggingPreview = true;
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.MouseDrag && draggingPreview)
        {
            var visibleWorldSize = 2f / previewZoom;
            previewCameraOffset.x -= currentEvent.delta.x / portraitRect.width * visibleWorldSize;
            previewCameraOffset.z += currentEvent.delta.y / portraitRect.height * visibleWorldSize;
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.MouseUp && draggingPreview)
        {
            draggingPreview = false;
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.ScrollWheel && pointerInPreview)
        {
            previewZoom = Mathf.Clamp(previewZoom - currentEvent.delta.y * 0.1f, 0.8f, 2.5f);
            currentEvent.Use();
        }

        TooltipHandler.TipRegion(portraitRect, "PawnEditor.Preview.DragHint".Translate());
        var stableSize = new Vector2(Mathf.Max(1f, Mathf.Round(portraitRect.width)), Mathf.Max(1f, Mathf.Round(portraitRect.height)));
        var texture = GetPawnTex(pawn, stableSize, previewRotation, previewCameraOffset, previewZoom);
        DrawPortraitOrPlaceholder(portraitRect, texture);

        if (Widgets.ButtonImage(rotateRect, TexUI.RotLeftTex))
            previewRotation.Rotate(RotationDirection.Counterclockwise);
        TooltipHandler.TipRegion(rotateRect, "PawnEditor.Preview.RotateLeft".Translate());

        if (Widgets.ButtonImageWithBG(apparelRect, GetAppearanceToggleIcon("Apparel_BasicShirt"), new Vector2(18f, 18f)))
            RenderClothes = !RenderClothes;
        DrawVisibilitySlash(apparelRect, RenderClothes);
        TooltipHandler.TipRegion(apparelRect, "PawnEditor.ShowApparel".Translate());

        if (Widgets.ButtonImageWithBG(headgearRect, GetAppearanceToggleIcon("Apparel_SimpleHelmet"), new Vector2(18f, 18f)))
            RenderHeadgear = !RenderHeadgear;
        DrawVisibilitySlash(headgearRect, RenderHeadgear);
        TooltipHandler.TipRegion(headgearRect, "PawnEditor.ShowHeadgear".Translate());
    }

    private static Texture2D GetAppearanceToggleIcon(string defName) =>
        DefDatabase<ThingDef>.GetNamedSilentFail(defName)?.uiIcon ?? BaseContent.WhiteTex;

    private static void DrawVisibilitySlash(Rect rect, bool visible)
    {
        if (visible)
            return;

        var originalColor = GUI.color;
        var originalMatrix = GUI.matrix;
        GUI.color = Color.red;
        GUIUtility.RotateAroundPivot(-45f, rect.center);
        GUI.DrawTexture(new Rect(rect.xMin - rect.width * 0.2f, rect.center.y - 1.5f, rect.width * 1.4f, 3f), BaseContent.WhiteTex);
        GUI.matrix = originalMatrix;
        GUI.color = originalColor;
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
