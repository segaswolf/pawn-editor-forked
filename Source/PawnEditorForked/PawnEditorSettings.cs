using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PawnEditor;

public class PawnEditorSettings : ModSettings
{
    public enum HediffLocation
    {
        RecipeDef,
        All
    }

    public bool CountNPCs;
    public HashSet<string> DontShowAgain = new HashSet<string>();
    public HediffLocation HediffLocationLimit = HediffLocation.RecipeDef;
    public bool HideFactions;
    public bool InGameDevButton = true;
    public bool OverrideVanilla;
    public float PointLimit = 100000f;
    public bool ShowOpenButton = true;
    public bool UseSilver;

    // When a loaded/cloned pawn has an exclusive love-partner relation (spouse/lover/fiance) toward
    // someone who already has a partner, we drop it by default so we never force polygamy against a
    // monogamous ideology (which could trip precepts or mood). Players whose ideology/mods allow
    // polygamy can opt in here to keep those relations.
    public bool AllowPolygamyOnLoad;
    public KeyCode EditorHotkey = KeyCode.KeypadMinus;

    // Debug-only: when enabled, PawnEditorProfiler logs timing/allocation "flags" for editor
    // events. Off by default so it never costs anything during normal play.
    public bool ProfilingEnabled;

    // When off (default), every editor window opens centered, so a window left in an awkward or
    // off-screen spot always comes back reachable. When on, windows remember their last position and
    // size between opens.
    public bool RememberWindowPositions;

    // Off by default: the editor normally behaves like vanilla surgery (replaces the part instead of
    // stacking implants, and refuses to put a module where there is no modular part to hold it). Turn
    // it on to place anything anywhere on purpose, warnings included.
    public bool AllowIllegalPlacements;

    public override void ExposeData()
    {
        base.ExposeData();

        Scribe_Collections.Look(ref DontShowAgain, nameof(DontShowAgain));
        Scribe_Values.Look(ref OverrideVanilla, nameof(OverrideVanilla));
        Scribe_Values.Look(ref InGameDevButton, nameof(InGameDevButton), true);
        Scribe_Values.Look(ref ShowOpenButton, nameof(ShowOpenButton), true);
        Scribe_Values.Look(ref PointLimit, nameof(PointLimit), 100000f);
        Scribe_Values.Look(ref UseSilver, nameof(UseSilver));
        Scribe_Values.Look(ref HideFactions, nameof(HideFactions));
        Scribe_Values.Look(ref CountNPCs, nameof(CountNPCs));
        Scribe_Values.Look(ref HediffLocationLimit, nameof(HediffLocationLimit), HediffLocation.RecipeDef);
        Scribe_Values.Look(ref EditorHotkey, nameof(EditorHotkey), KeyCode.KeypadMinus);
        Scribe_Values.Look(ref ProfilingEnabled, nameof(ProfilingEnabled));
        Scribe_Values.Look(ref AllowPolygamyOnLoad, nameof(AllowPolygamyOnLoad));
        Scribe_Values.Look(ref RememberWindowPositions, nameof(RememberWindowPositions));
        Scribe_Values.Look(ref AllowIllegalPlacements, nameof(AllowIllegalPlacements));

        if (HARCompat.Active)
        {
            Scribe_Values.Look(ref HARCompat.EnforceRestrictions, "EnforceHARRestrictions", true);
        }

        if (DontShowAgain == null)
        {
            DontShowAgain = new HashSet<string>();
        }
    }
}