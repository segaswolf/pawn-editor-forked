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
    public static RenderTexture GetPawnTex(Pawn pawn, Vector2 portraitSize, Rot4 dir, Vector3 cameraOffset = default, float cameraZoom = 1f) =>
        // [BANDERITA] Portrait fetch. PortraitsCache.Get returns a cached RenderTexture when one
        // exists for (pawn, size, dir, ...); otherwise it RENDERS A NEW ONE. If the editor keeps
        // clearing the cache (PortraitsCache.Clear in RecachePawnList), every fetch here becomes a
        // fresh render = constant RenderTexture churn = GC pressure = atlas drop = black screen.
        // PerFrame because it's called once per visible pawn per frame in the list.
        PawnEditorProfiler.Measure("Portrait.GetPawnTex", PawnEditorProfiler.Cadence.PerFrame, () =>
            PortraitsCache.Get(pawn, portraitSize, dir, cameraOffset, cameraZoom,
                renderHeadgear: RenderHeadgear, renderClothes: RenderClothes, stylingStation: true));

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
        if (image != null)
            GUI.DrawTexture(rect, image);
        if (Widgets.ButtonImage(rect.ContractedBy(8).RightPartPixels(16).TopPartPixels(16), TexUI.RotRightTex))
            curRot.Rotate(RotationDirection.Counterclockwise);

        if (Widgets.InfoCardButtonWorker(rect.ContractedBy(8).LeftPartPixels(16).TopPartPixels(16))) Find.WindowStack.Add(new Dialog_InfoCard(selectedPawn));
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
