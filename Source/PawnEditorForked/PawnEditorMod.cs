using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
// All services use flat namespace PawnEditor — no sub-namespace imports needed

namespace PawnEditor;

[HotSwappable]
public class PawnEditorMod : Mod
{
    public static Harmony Harm { get; private set; }
    public static PawnEditorSettings Settings { get; private set; }
    public static PawnEditorMod Instance { get; private set; }

    private readonly PawnEditorHarmony _harmonyBootstrap;
    private readonly ModCompatibilityService _compatibilityService;
    private readonly PawnEditorHotkeyService _hotkeyService;
    private readonly PawnEditorSettingsDrawer _settingsDrawer;

    public PawnEditorMod(ModContentPack content) : base(content)
    {
        Instance = this;
        Harm = new Harmony("segaswolf.pawneditor.fork");
        Settings = GetSettings<PawnEditorSettings>();

        _hotkeyService = new PawnEditorHotkeyService();
        _settingsDrawer = new PawnEditorSettingsDrawer(_hotkeyService);
        _harmonyBootstrap = new PawnEditorHarmony(Harm);
        _compatibilityService = new ModCompatibilityService(content, Harm);

        _harmonyBootstrap.ApplyCorePatches();
        _compatibilityService.Initialize();
    }

    public override string SettingsCategory()
    {
        return "PawnEditor".Translate();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        _settingsDrawer.Draw(inRect, Settings);
    }

    public override void WriteSettings()
    {
        base.WriteSettings();
        _harmonyBootstrap.ApplySettingsPatches(Settings);
    }

    public static bool OverrideVanilla(Rect rect, Page_ConfigureStartingPawns __instance)
    {
        PawnEditor.Pregame = true;
        PawnEditor.DoUI(rect, __instance.DoBack, __instance.DoNext);
        return false;
    }

    public static void Keybind()
    {
        if (Instance == null || Settings == null)
        {
            return;
        }

        Instance._hotkeyService.HandleOpenEditorHotkey(Settings);
    }

    public static bool AddEditorButton(Rect rect, Page_ConfigureStartingPawns __instance)
    {
        float x;
        float y;

        if (ModsConfig.BiotechActive)
        {
            Text.Font = GameFont.Small;
            x = rect.x + rect.width / 2 + 2f;
            y = rect.y + rect.height - 38f;

            if (Widgets.ButtonText(new Rect(x, y, Page.BottomButSize.x, Page.BottomButSize.y), "XenotypeEditor".Translate()))
            {
                Find.WindowStack.Add(new Dialog_CreateXenotype(__instance.curPawnIndex, delegate
                {
                    CharacterCardUtility.cachedCustomXenotypes = null;
                    StartingPawnUtility.RandomizePawn(__instance.curPawnIndex);
                }));
            }

            x = rect.x + rect.width / 2 - 2f - Page.BottomButSize.x;
            y = rect.y + rect.height - 38f;
        }
        else
        {
            x = (rect.width - Page.BottomButSize.x) / 2f;
            y = rect.y + rect.height - 38f;
        }

        if (Widgets.ButtonText(new Rect(x, y, Page.BottomButSize.x, Page.BottomButSize.y), "PawnEditor.CharacterEditor".Translate()))
        {
            Find.WindowStack.Add(new Dialog_PawnEditor_Pregame(__instance.DoNext));
        }

        return false;
    }

    public static IEnumerable<CodeInstruction> AddDevButton(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var codes = new List<CodeInstruction>(instructions);

        var toggleGodModeMethod = AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.ToggleGodMode));
        var toggleGodModeIndex = codes.FindIndex(ins => ins.Calls(toggleGodModeMethod));
        var branchIndex = codes.FindLastIndex(toggleGodModeIndex, ins => ins.opcode == OpCodes.Brfalse_S);

        if (toggleGodModeIndex < 0 || branchIndex < 0)
        {
            Log.Warning($"[Pawn Editor] Could not inject dev button transpiler safely. ToggleGodMode={toggleGodModeIndex}, Branch={branchIndex}. Dev toolbar button will not appear.");
            return codes;
        }

        var originalLabel = (Label)codes[branchIndex].operand;
        var continueLabel = generator.DefineLabel();

        codes[toggleGodModeIndex + 1].labels.Remove(originalLabel);
        codes[toggleGodModeIndex + 1].labels.Add(continueLabel);

        codes.InsertRange(toggleGodModeIndex + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0).WithLabels(originalLabel),
            CodeInstruction.LoadField(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.widgetRow)),
            CodeInstruction.LoadField(typeof(TexPawnEditor), nameof(TexPawnEditor.OpenPawnEditor)),
            new CodeInstruction(OpCodes.Ldstr, "PawnEditor.CharacterEditor"),
            CodeInstruction.Call(typeof(Translator), nameof(Translator.Translate), new[] { typeof(string) }),
            CodeInstruction.Call(typeof(TaggedString), "op_Implicit", new[] { typeof(TaggedString) }),
            new CodeInstruction(OpCodes.Ldloca, 0),
            new CodeInstruction(OpCodes.Initobj, typeof(Color?)),
            new CodeInstruction(OpCodes.Ldloc_0),
            new CodeInstruction(OpCodes.Ldloca, 0),
            new CodeInstruction(OpCodes.Initobj, typeof(Color?)),
            new CodeInstruction(OpCodes.Ldloc_0),
            new CodeInstruction(OpCodes.Ldloca, 0),
            new CodeInstruction(OpCodes.Initobj, typeof(Color?)),
            new CodeInstruction(OpCodes.Ldloc_0),
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Ldc_R4, -1f),
            new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(WidgetRow), nameof(WidgetRow.ButtonIcon))),
            new CodeInstruction(OpCodes.Brfalse, continueLabel),
            new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Find), nameof(Find.WindowStack))),
            new CodeInstruction(OpCodes.Newobj, AccessTools.Constructor(typeof(Dialog_PawnEditor_InGame))),
            CodeInstruction.Call(typeof(WindowStack), nameof(WindowStack.Add))
        });

        return codes;
    }

    public static IEnumerable<Gizmo> AddEditButton(IEnumerable<Gizmo> gizmos, Pawn __instance)
    {
        foreach (var gizmo in gizmos)
        {
            yield return gizmo;
        }

        if (!Prefs.DevMode)
        {
            yield break;
        }

        yield return new Command_Action
        {
            defaultLabel = "PawnEditor.Edit".Translate(),
            defaultDesc = "PawnEditor.Edit.Desc".Translate(),
            action = delegate
            {
                Find.WindowStack.Add(new Dialog_PawnEditor_InGame());
                PawnEditor.Select(__instance);
            }
        };
    }

    public static void Notify_ConfigurePawns()
    {
        StartingThingsManager.ProcessScenario();
        PawnEditor.ResetPoints();
        PawnEditor.CheckChangeTabGroup();
    }
}