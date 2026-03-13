using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PawnEditor;

public class PawnEditorSettings : ModSettings
{
    public enum HediffLocation { RecipeDef, All }

    public bool CountNPCs;
    public HashSet<string> DontShowAgain = new();
    public HediffLocation HediffLocationLimit = HediffLocation.RecipeDef;
    public bool InGameDevButton = true;
    public bool OverrideVanilla;
    public float PointLimit = 100000;
    public bool ShowOpenButton = true;
    public bool UseSilver;
    public bool HideFactions;
    public KeyCode EditorHotkey = KeyCode.KeypadMinus;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref DontShowAgain, nameof(DontShowAgain));
        Scribe_Values.Look(ref OverrideVanilla, nameof(OverrideVanilla));
        Scribe_Values.Look(ref InGameDevButton, nameof(InGameDevButton), true);
        Scribe_Values.Look(ref ShowOpenButton, nameof(ShowOpenButton), true);
        Scribe_Values.Look(ref PointLimit, nameof(PointLimit));
        Scribe_Values.Look(ref UseSilver, nameof(UseSilver));
        Scribe_Values.Look(ref HideFactions, nameof(HideFactions));
        Scribe_Values.Look(ref CountNPCs, nameof(CountNPCs));
        Scribe_Values.Look(ref HediffLocationLimit, nameof(HediffLocationLimit), HediffLocation.RecipeDef);
        Scribe_Values.Look(ref EditorHotkey, nameof(EditorHotkey), KeyCode.KeypadMinus);

        // Nota: HARCompat debería manejar su propia persistencia si es posible, 
        // pero lo dejamos aquí por ahora para no romper guardados existentes.
        if (HARCompat.Active) 
            Scribe_Values.Look(ref HARCompat.EnforceRestrictions, "EnforceHARRestrictions", true);

        DontShowAgain ??= new();
    }
}