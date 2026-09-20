using System;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// A plain full-screen dim drawn BEHIND the pawn editor.
///
/// Without it, whatever is underneath (most visibly RimWorld's own "Create characters" page during
/// pregame) stays fully lit and competes with the editor for attention — you end up looking at two
/// character screens at once. Dimming keeps the vanilla page where it is, untouched, and simply
/// pushes it into the background.
///
/// Why the ordering works: WindowStack.Add calls PreOpen() BEFORE inserting the window into the list,
/// so opening this backdrop from the editor's PreOpen puts it in the stack first — behind the editor,
/// but in front of anything that was already open.
///
/// HARDENING (crash report from DanForged: "screen fades slightly then crashes" when opening the
/// editor mid-game). This window is purely decorative, so it is built to be incapable of taking the
/// game down with it:
///   - <see cref="ExcludeFromResizePatch"/> keeps Patch_ResizableWindows away from it. That patch
///     applies to every window in this namespace and was turning the backdrop into a resizable,
///     draggable window whose rect it also tracked — while the backdrop reset its own rect every
///     frame. Two things fighting over one rect, for a window the user can't even see.
///   - The rect is set ONCE, in PreOpen, and on resolution change. Reassigning windowRect from inside
///     DoWindowContents (as this did before) mutates the rect mid-layout, which is exactly what makes
///     Unity's IMGUI throw about mismatched groups.
///   - Drawing is wrapped: if anything goes wrong it closes itself quietly instead of erroring every
///     frame. Losing the dim is acceptable; losing the session is not.
/// </summary>
public class Window_EditorBackdrop : Window, IExcludeFromResizePatch
{
    private const float DimAlpha = 0.55f;

    private int lastScreenWidth;
    private int lastScreenHeight;

    public Window_EditorBackdrop()
    {
        doWindowBackground = false;
        drawShadow = false;
        doCloseX = false;
        doCloseButton = false;
        closeOnClickedOutside = false;
        closeOnAccept = false;
        closeOnCancel = false;
        preventCameraMotion = false;
        // Not focus-stealing: the editor on top must keep keyboard focus for its text fields.
        focusWhenOpened = false;
        // Explicitly NOT resizeable/draggable. There is nothing here for the user to grab.
        resizeable = false;
        draggable = false;
    }

    public override Vector2 InitialSize => new(UI.screenWidth, UI.screenHeight);

    // public (not protected): this project references a publicized Assembly-CSharp, so these members
    // are public on the base type — matching Dialog_AppearanceEditor's existing pattern.
    public override float Margin => 0f;

    public override void SetInitialSizeAndPosition()
    {
        lastScreenWidth = UI.screenWidth;
        lastScreenHeight = UI.screenHeight;
        windowRect = new Rect(0f, 0f, lastScreenWidth, lastScreenHeight);
    }

    public override void DoWindowContents(Rect inRect)
    {
        try
        {
            // Only re-fit on an actual resolution change, never mid-layout on a normal frame.
            if (UI.screenWidth != lastScreenWidth || UI.screenHeight != lastScreenHeight)
            {
                lastScreenWidth = UI.screenWidth;
                lastScreenHeight = UI.screenHeight;
                return;
            }

            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, DimAlpha);
            GUI.DrawTexture(inRect, BaseContent.WhiteTex);
            GUI.color = previous;
        }
        catch (Exception ex)
        {
            Log.WarningOnce($"[Pawn Editor] Editor backdrop failed to draw and was disabled: {ex.Message}", 0x5BACD70);
            GUI.color = Color.white;
            Close(false);
        }
    }
}
