using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// The "open editor" hotkey. There is ONE: the game's key binding PawnEditor_OpenEditor (Options > Controls),
/// and the row in our mod settings edits that same binding.
/// <para>There used to be two, both on keypad minus: that binding and a key of our own in the mod settings,
/// and either one opened the editor. Clearing one did nothing, and right-click never cleared ours: the button
/// takes any mouse button, so a right-click arrived as a click and started "Press any key..." (IkeFanboy64,
/// 14 Nov). The settings row now does what vanilla's controls screen does, with vanilla's own dialog.</para>
/// </summary>
public sealed class PawnEditorHotkeyService
{
    private static KeyBindingDef Binding => KeyBindingDefOf.PawnEditor_OpenEditor;

    public void DrawHotkeyPicker(Listing_Standard listing, PawnEditorSettings settings)
    {
        if (KeyPrefs.KeyPrefsData == null || Binding == null) return;
        MoveLegacyHotkey(settings);

        var rect = listing.GetRect(30f);
        Widgets.Label(rect.LeftHalf(), "PawnEditor.EditorHotkey".Translate());

        var button = rect.RightHalf();
        var bound = KeyPrefs.KeyPrefsData.GetBoundKeyCode(Binding, KeyPrefs.BindingSlot.A);
        if (Widgets.ButtonText(button, bound.ToStringReadable()))
        {
            // Which mouse button is only known inside the click, the way Dialog_KeyBindings asks.
            if (Event.current.button == 1)
            {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new("ResetBinding".Translate(), () => SetBinding(Binding.defaultKeyCodeA)),
                    new("ClearBinding".Translate(), () => SetBinding(KeyCode.None))
                }));
            }
            else
            {
                Find.WindowStack.Add(new Dialog_DefineSavedBinding(KeyPrefs.KeyPrefsData, Binding, KeyPrefs.BindingSlot.A));
            }
            Event.current.Use();
        }

        TooltipHandler.TipRegion(button, "PawnEditor.EditorHotkey.Tip".Translate());
    }

    public void HandleOpenEditorHotkey(PawnEditorSettings settings)
    {
        if (Event.current == null || KeyPrefs.KeyPrefsData == null || Binding == null) return;
        MoveLegacyHotkey(settings);

        bool triggered;
        try { triggered = Binding.KeyDownEvent; }
        catch { return; }

        if (!triggered || PawnEditor.Pregame) return;
        Event.current.Use();

        if (Find.WindowStack.IsOpen<Dialog_PawnEditor_InGame>())
            Find.WindowStack.TryRemove(typeof(Dialog_PawnEditor_InGame));
        else
            Find.WindowStack.Add(new Dialog_PawnEditor_InGame());
    }

    private static void SetBinding(KeyCode key)
    {
        if (key != KeyCode.None)
            KeyPrefs.KeyPrefsData.EraseConflictingBindingsForKeyCode(Binding, key, old =>
                Messages.Message("KeyBindingOverwritten".Translate(old.LabelCap), MessageTypeDefOf.TaskCompletion, historical: false));
        KeyPrefs.KeyPrefsData.SetBinding(Binding, KeyPrefs.BindingSlot.A, key);
        KeyPrefs.Save();
    }

    /// <summary>
    /// Once: a key someone set in our old setting moves to the game's binding, so updating keeps their key.
    /// Only if the binding is still at its default (they never changed it in Controls) and the key isn't
    /// taken by another binding; otherwise the binding wins and the log says so.
    /// </summary>
    internal static void MoveLegacyHotkey(PawnEditorSettings settings)
    {
        if (settings == null || settings.EditorHotkey == PawnEditorSettings.LegacyHotkeyDefault) return;
        var legacy = settings.EditorHotkey;
        settings.EditorHotkey = PawnEditorSettings.LegacyHotkeyDefault; // never again, whatever happens below

        var current = KeyPrefs.KeyPrefsData.GetBoundKeyCode(Binding, KeyPrefs.BindingSlot.A);
        var taken = false;
        if (legacy != KeyCode.None)
            KeyPrefs.KeyPrefsData.Clone().EraseConflictingBindingsForKeyCode(Binding, legacy, _ => taken = true); // a probe: the clone is thrown away

        if (current == Binding.defaultKeyCodeA && !taken)
        {
            KeyPrefs.KeyPrefsData.SetBinding(Binding, KeyPrefs.BindingSlot.A, legacy);
            KeyPrefs.Save();
            Log.Message($"[Pawn Editor] The editor hotkey now lives in Options > Controls; moved yours ({legacy}) there.");
        }
        else
        {
            Log.Message($"[Pawn Editor] The editor hotkey now lives in Options > Controls. Kept the binding there "
                        + $"({current}) instead of the old setting ({legacy})" + (taken ? ", which another binding uses." : "."));
        }
        settings.Write();
    }

    /// <summary>Vanilla's "press any key" dialog, saving the binding when it closes as the controls screen would.</summary>
    private sealed class Dialog_DefineSavedBinding : Dialog_DefineBinding
    {
        public Dialog_DefineSavedBinding(KeyPrefsData data, KeyBindingDef def, KeyPrefs.BindingSlot slot) : base(data, def, slot) { }

        public override void PostClose()
        {
            base.PostClose();
            KeyPrefs.Save();
        }
    }
}
