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
    public KeyCode EditorHotkey = KeyCode.KeypadMinus;

    // Debug-only: when enabled, PawnEditorProfiler logs timing/allocation "flags" for editor
    // events. Off by default so it never costs anything during normal play.
    public bool ProfilingEnabled;

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