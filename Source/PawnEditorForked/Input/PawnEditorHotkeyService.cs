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

        var hotkeyLabel = _waitingForHotkey
            ? "PawnEditor.PressAnyKey".Translate().ToString()
            : settings.EditorHotkey.ToString();

        if (Widgets.ButtonText(hotkeyRect.RightHalf(), hotkeyLabel))
        {
            _waitingForHotkey = true;
        }

        if (_waitingForHotkey && Event.current.type == EventType.KeyDown && Event.current.keyCode != KeyCode.None)
        {
            settings.EditorHotkey = Event.current.keyCode;
            _waitingForHotkey = false;
            Event.current.Use();
        }
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