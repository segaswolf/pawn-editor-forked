using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnEditor;

public sealed class PawnEditorHarmony
{
    private readonly Harmony _harmony;

    public PawnEditorHarmony(Harmony harmony)
    {
        _harmony = harmony;
    }

    public void ApplyCorePatches()
    {
        _harmony.Patch(
            AccessTools.Method(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.PreOpen)),
            prefix: new HarmonyMethod(typeof(PawnEditorMod), nameof(PawnEditorMod.Notify_ConfigurePawns)));

        _harmony.Patch(
            AccessTools.Method(typeof(Page_SelectScenario), nameof(Page_SelectScenario.PreOpen)),
            prefix: new HarmonyMethod(typeof(StartingThingsManager), nameof(StartingThingsManager.RestoreScenario)));

        _harmony.Patch(
            AccessTools.Method(typeof(Game), nameof(Game.InitNewGame)),
            postfix: new HarmonyMethod(typeof(StartingThingsManager), nameof(StartingThingsManager.RestoreScenario)));

        // FIX: Starting items disappear — restore scenario when going to main menu.
        // Without this, if the user opens the editor and then goes to main menu
        // (skipping Page_SelectScenario), the scenario parts stay removed.
        _harmony.Patch(
            AccessTools.Method(typeof(GenScene), nameof(GenScene.GoToMainMenu)),
            prefix: new HarmonyMethod(typeof(StartingThingsManager), nameof(StartingThingsManager.RestoreScenario)));

        _harmony.Patch(
            AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DevToolStarterOnGUI)),
            prefix: new HarmonyMethod(typeof(PawnEditorMod), nameof(PawnEditorMod.Keybind)));

        _harmony.Patch(
            AccessTools.Method(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.CompatibilityWith)),
            transpiler: new HarmonyMethod(typeof(SaveLoadUtility), nameof(SaveLoadUtility.UseCompatibilitySeedInCompatibilityWith)));

        ApplySettingsPatches(PawnEditorMod.Settings);
    }

    public void ApplySettingsPatches(PawnEditorSettings settings)
    {
        if (settings == null)
        {
            Log.Warning("[Pawn Editor] Settings were null while applying settings patches.");
            return;
        }

        UnpatchSettingsControlledMethods();

        if (settings.OverrideVanilla)
        {
            _harmony.Patch(
                AccessTools.Method(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DoWindowContents)),
                prefix: new HarmonyMethod(typeof(PawnEditorMod), nameof(PawnEditorMod.OverrideVanilla)));
        }
        else
        {
            _harmony.Patch(
                AccessTools.Method(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DrawXenotypeEditorButton)),
                prefix: new HarmonyMethod(typeof(PawnEditorMod), nameof(PawnEditorMod.AddEditorButton)));
        }

        if (settings.ShowOpenButton)
        {
            _harmony.Patch(
                AccessTools.Method(typeof(Pawn), nameof(Pawn.GetGizmos)),
                postfix: new HarmonyMethod(typeof(PawnEditorMod), nameof(PawnEditorMod.AddEditButton)));
        }

        if (settings.InGameDevButton)
        {
            _harmony.Patch(
                AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DrawButtons)),
                transpiler: new HarmonyMethod(typeof(PawnEditorMod), nameof(PawnEditorMod.AddDevButton)));
        }
    }

    private void UnpatchSettingsControlledMethods()
    {
        _harmony.Unpatch(
            AccessTools.Method(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DoWindowContents)),
            HarmonyPatchType.Prefix,
            _harmony.Id);

        _harmony.Unpatch(
            AccessTools.Method(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DrawXenotypeEditorButton)),
            HarmonyPatchType.Prefix,
            _harmony.Id);

        _harmony.Unpatch(
            AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DrawButtons)),
            HarmonyPatchType.Transpiler,
            _harmony.Id);

        _harmony.Unpatch(
            AccessTools.Method(typeof(Pawn), nameof(Pawn.GetGizmos)),
            HarmonyPatchType.Postfix,
            _harmony.Id);
    }
}