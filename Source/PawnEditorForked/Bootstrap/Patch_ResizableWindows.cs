using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Makes every Pawn Editor window user-resizable (drag the bottom-right corner) and movable (drag it
/// by empty space).
///
/// Hooked on <see cref="Window.WindowOnGUI"/> because none of our windows override it (they override
/// DoWindowContents instead), so a postfix there fires reliably for ALL of them — the main editor,
/// the appearance editor, every ListingMenu&lt;T&gt;, the small dialogs, and any window added later —
/// without having to touch each constructor or worry about PostOpen overrides that skip base calls.
///
/// It's filtered to this mod's namespace so vanilla and other mods' windows are never affected. Our
/// layouts are drawn relative to the window rect, so they adapt to the new size. Setting the flags in
/// the postfix means the resize handle appears one frame after the window opens (imperceptible).
/// </summary>
/// <summary>A Pawn Editor window that can lock itself in place (so an inner splitter can be dragged
/// without the whole window moving). When DragLocked is true, the resizable-windows patch leaves it
/// non-draggable.</summary>
public interface IDragLockable
{
    bool DragLocked { get; }
}

public static class Patch_ResizableWindows
{
    // Last known rect per window type, so "remember position" can restore it on the next open.
    private static readonly Dictionary<Type, Rect> LastRects = new();
    // Instances already positioned this open (first-frame restore). Weak so closed windows are GC'd.
    private static readonly ConditionalWeakTable<Window, object> Positioned = new();
    private static readonly object Marker = new();

    public static void SetResizable(Window __instance)
    {
        var type = __instance.GetType();
        var ns = type.Namespace;
        if (ns == null || !ns.StartsWith("PawnEditor", StringComparison.Ordinal)) return;

        __instance.resizeable = true;
        // A window can opt out of dragging (e.g. the appearance editor, so its inner splitter can be
        // dragged without moving the whole window). Locked windows resize via the corner handle only.
        __instance.draggable = !(__instance is IDragLockable dl && dl.DragLocked);

        // Default (setting off): do nothing else. Each window is a fresh instance opened centered, so
        // one left off-screen always comes back reachable next open. That IS the safeguard.
        if (!(PawnEditorMod.Settings?.RememberWindowPositions ?? false)) return;

        // First frame for this instance: restore the remembered rect (clamped on-screen so a stale or
        // off-screen saved position can't hide the window). Applied after this frame drew centered, so
        // it snaps into place next frame — a one-frame nudge, imperceptible.
        if (!Positioned.TryGetValue(__instance, out _))
        {
            Positioned.Add(__instance, Marker);
            if (LastRects.TryGetValue(type, out var saved))
            {
                saved.x = Mathf.Clamp(saved.x, 0f, UI.screenWidth - 50f);
                saved.y = Mathf.Clamp(saved.y, 0f, UI.screenHeight - 50f);
                __instance.windowRect = saved;
            }
        }

        // Remember the latest rect so it's there after the window closes.
        LastRects[type] = __instance.windowRect;
    }
}
