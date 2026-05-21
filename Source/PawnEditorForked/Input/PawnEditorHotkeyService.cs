using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

public sealed class PawnEditorHotkeyService
{
    private bool _waitingForHotkey;

    public void DrawHotkeyPicker(Listing_Standard listing, PawnEditorSettings settings)
    {
        var hotkeyRect = listing.GetRect(30f);
        Widgets.Label(hotkeyRect.LeftHalf(), "PawnEditor.EditorHotkey".Translate());

        string hotkeyLabel;
        if (_waitingForHotkey)
            hotkeyLabel = "PawnEditor.PressAnyKey".Translate().ToString();
        else if (settings.EditorHotkey == KeyCode.None)
            hotkeyLabel = "None";
        else
            hotkeyLabel = settings.EditorHotkey.ToString();

        // Left click: set new hotkey. Right click: clear hotkey.
        var buttonRect = hotkeyRect.RightHalf();
        if (Widgets.ButtonText(buttonRect, hotkeyLabel))
        {
            _waitingForHotkey = true;
        }
        if (Mouse.IsOver(buttonRect) && Event.current.type == EventType.MouseDown && Event.current.button == 1)
        {
            settings.EditorHotkey = KeyCode.None;
            _waitingForHotkey = false;
            Event.current.Use();
        }

        if (_waitingForHotkey && Event.current.type == EventType.KeyDown)
        {
            if (Event.current.keyCode == KeyCode.Escape)
            {
                // Cancel without changing
                _waitingForHotkey = false;
            }
            else if (Event.current.keyCode != KeyCode.None)
            {
                settings.EditorHotkey = Event.current.keyCode;
                _waitingForHotkey = false;
            }
            Event.current.Use();
        }

        // Tooltip
        TooltipHandler.TipRegion(buttonRect, "Right-click to disable hotkey. Escape to cancel.");
    }

    public void HandleOpenEditorHotkey(PawnEditorSettings settings)
    {
        var triggered = false;

        try
        {
            triggered = KeyBindingDefOf.PawnEditor_OpenEditor.KeyDownEvent;
        }
        catch
        {
        }

        if (!triggered &&
            settings.EditorHotkey != KeyCode.None &&
            Event.current != null &&
            Event.current.type == EventType.KeyDown &&
            Event.current.keyCode == settings.EditorHotkey)
        {
            triggered = true;
            Event.current.Use();
        }

        if (!triggered || PawnEditor.Pregame)
        {
            return;
        }

        if (Find.WindowStack.IsOpen<Dialog_PawnEditor_InGame>())
        {
            Find.WindowStack.TryRemove(typeof(Dialog_PawnEditor_InGame));
        }
        else
        {
            Find.WindowStack.Add(new Dialog_PawnEditor_InGame());
        }
    }
}