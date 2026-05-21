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
        PortraitsCache.Get(pawn, portraitSize, dir, cameraOffset, cameraZoom,
            renderHeadgear: RenderHeadgear, renderClothes: RenderClothes, stylingStation: true);

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
        var image = GetPawnTex(selectedPawn, rect.size, curRot);
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
