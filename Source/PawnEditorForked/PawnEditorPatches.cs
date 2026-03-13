using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

public static class PawnEditorPatches
{
    // Parche para sobrescribir la ventana vanilla de creación de personajes
    public static bool OverrideVanilla(Rect rect, Page_ConfigureStartingPawns __instance)
    {
        PawnEditor.Pregame = true;
        PawnEditor.DoUI(rect, __instance.DoBack, __instance.DoNext);
        return false;
    }

    // Parche para añadir el botón de "Edit" en pawns (modo Dios)
    public static IEnumerable<Gizmo> AddEditButton(IEnumerable<Gizmo> gizmos, Pawn __instance)
    {
        foreach (var g in gizmos) yield return g;

        if (!Prefs.DevMode || !DebugSettings.godMode) yield break;

        yield return new Command_Action
        {
            defaultLabel = "PawnEditor.Edit".Translate(),
            defaultDesc = "PawnEditor.Edit.Desc".Translate(),
            action = () =>
            {
                Find.WindowStack.Add(new Dialog_PawnEditor_InGame());
                PawnEditor.Select(__instance);
            }
        };
    }

    // Parche para el botón en la pantalla de selección
    public static bool AddEditorButton(Rect rect, Page_ConfigureStartingPawns __instance)
    {
        float x, y;
        if (ModsConfig.BiotechActive)
        {
            Text.Font = GameFont.Small;
            x = rect.x + rect.width / 2 + 2;
            y = rect.y + rect.height - 38f;
            if (Widgets.ButtonText(new(x, y, Page.BottomButSize.x, Page.BottomButSize.y), "XenotypeEditor".Translate()))
                Find.WindowStack.Add(new Dialog_CreateXenotype(__instance.curPawnIndex, delegate
                {
                    CharacterCardUtility.cachedCustomXenotypes = null;
                    StartingPawnUtility.RandomizePawn(__instance.curPawnIndex);
                }));
            x = rect.x + rect.width / 2 - 2 - Page.BottomButSize.x;
            y = rect.y + rect.height - 38f;
        }
        else
        {
            x = (rect.width - Page.BottomButSize.x) / 2f;
            y = rect.y + rect.height - 38f;
        }

        if (Widgets.ButtonText(new(x, y, Page.BottomButSize.x, Page.BottomButSize.y), "PawnEditor.CharacterEditor".Translate()))
            Find.WindowStack.Add(new Dialog_PawnEditor_Pregame(__instance.DoNext));

        return false;
    }

    // Transpiler para el botón de Debug
    public static IEnumerable<CodeInstruction> AddDevButton(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var codes = instructions.ToList();
        var info = AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.ToggleGodMode));
        var idx = codes.FindIndex(ins => ins.Calls(info));
        
        // Manejo de seguridad si el índice no se encuentra (evita crashes en updates de RimWorld)
        if (idx == -1) 
        {
            Log.Error("[PawnEditor] Could not find injection point for DevButton patch.");
            return codes;
        }

        var idx2 = codes.FindLastIndex(idx, ins => ins.opcode == OpCodes.Brfalse_S);
        var label2 = (Label)codes[idx2].operand;
        var label1 = generator.DefineLabel();
        
        codes[idx + 1].labels.Remove(label2);
        codes[idx + 1].labels.Add(label1);
        
        // Inyección de código
        codes.InsertRange(idx + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0).WithLabels(label2),
            CodeInstruction.LoadField(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.widgetRow)),
            CodeInstruction.LoadField(typeof(TexPawnEditor), nameof(TexPawnEditor.OpenPawnEditor)),
            new CodeInstruction(OpCodes.Ldstr, "PawnEditor.CharacterEditor"),
            CodeInstruction.Call(typeof(Translator), nameof(Translator.Translate), new[] { typeof(string) }),
            CodeInstruction.Call(typeof(TaggedString), "op_Implicit", new[] { typeof(TaggedString) }),
            new CodeInstruction(OpCodes.Ldloca, 0), new CodeInstruction(OpCodes.Initobj, typeof(Color?)),
            new CodeInstruction(OpCodes.Ldloc_0), new CodeInstruction(OpCodes.Ldloca, 0),
            new CodeInstruction(OpCodes.Initobj, typeof(Color?)), new CodeInstruction(OpCodes.Ldloc_0),
            new CodeInstruction(OpCodes.Ldloca, 0), new CodeInstruction(OpCodes.Initobj, typeof(Color?)),
            new CodeInstruction(OpCodes.Ldloc_0), new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Ldc_R4, -1f),
            new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(WidgetRow), nameof(WidgetRow.ButtonIcon))),
            new CodeInstruction(OpCodes.Brfalse, label1),
            new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Find), nameof(Find.WindowStack))),
            new CodeInstruction(OpCodes.Newobj, AccessTools.Constructor(typeof(Dialog_PawnEditor_InGame))),
            CodeInstruction.Call(typeof(WindowStack), nameof(WindowStack.Add))
        });
        return codes;
    }
    
    public static void Notify_ConfigurePawns()
    {
        StartingThingsManager.ProcessScenario();
        PawnEditor.ResetPoints();
        PawnEditor.CheckChangeTabGroup();
    }
}