using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[HotSwappable]
public class PawnEditorMod : Mod
{
    public static Harmony Harm;
    public static PawnEditorSettings Settings;
    private static bool _waitingForHotkey;
    public static PawnEditorMod Instance;

    public PawnEditorMod(ModContentPack content) : base(content)
    {
        Harm = new("segaswolf.pawneditor.fork");
        Instance = this;
        Settings = GetSettings<PawnEditorSettings>();

        // Aplicar parches básicos
        ApplyPatches(content);

        LongEventHandler.ExecuteWhenFinished(() =>
        {
            ApplySettings();
            InitializeModCompatibility(content);
        });
    }

    private void ApplyPatches(ModContentPack content)
    {
        // Parches que siempre están activos
        Harm.Patch(AccessTools.Method(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.PreOpen)),
            new(GetType(), nameof(PawnEditorPatches.Notify_ConfigurePawns)));
        Harm.Patch(AccessTools.Method(typeof(Page_SelectScenario), nameof(Page_SelectScenario.PreOpen)),
            new(typeof(StartingThingsManager), nameof(StartingThingsManager.RestoreScenario)));
        Harm.Patch(AccessTools.Method(typeof(Game), nameof(Game.InitNewGame)),
            postfix: new(typeof(StartingThingsManager), nameof(StartingThingsManager.RestoreScenario)));
        Harm.Patch(AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DevToolStarterOnGUI)),
            new(GetType(), nameof(Keybind)));
        Harm.Patch(AccessTools.Method(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.CompatibilityWith)),
            transpiler: new(typeof(SaveLoadUtility), nameof(SaveLoadUtility.UseCompatibilitySeedInCompatibilityWith)));
    }

    private void InitializeModCompatibility(ModContentPack content)
    {
        foreach (var assembly in content.assemblies.loadedAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.TryGetAttribute<ModCompatAttribute>(out var modCompat) && modCompat.ShouldActivate())
                {
                    ActivateCompatibilityFeature(type);
                }
            }
        }
    }

    private void ActivateCompatibilityFeature(Type type)
    {
        try 
        {
            // Intentar invocar Activate(Harmony)
            var method = AccessTools.Method(type, "Activate", new[] { typeof(Harmony) });
            method?.Invoke(null, new object[] { Harm });

            // Intentar invocar Activate()
            method = AccessTools.Method(type, "Activate", Type.EmptyTypes);
            method?.Invoke(null, Array.Empty<object>());

            // Setear campo Active
            var field = AccessTools.Field(type, "Active");
            field?.SetValue(null, true);

            if (Prefs.DevMode)
            {
                // Intentar obtener nombre para log
                var nameMethod = AccessTools.Method(type, "GetName") ?? AccessTools.Method(type, "get_Name");
                var nameField = AccessTools.Field(type, "Name");
                string name = (string)nameMethod?.Invoke(null, Array.Empty<object>()) ?? (string)nameField?.GetValue(null);
                
                if (name != null) Log.Message($"[Pawn Editor] {name} compatibility active.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[PawnEditor] Failed to activate compatibility for {type.FullName}: {ex}");
        }
    }

    public override string SettingsCategory() => "PawnEditor".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        base.DoSettingsWindowContents(inRect);
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        
        // General Settings
        listing.CheckboxLabeled("PawnEdtior.OverrideVanilla".Translate(), ref Settings.OverrideVanilla, "PawnEditor.OverrideVanilla.Desc".Translate());
        listing.CheckboxLabeled("PawnEditor.InGameDevButton".Translate(), ref Settings.InGameDevButton, "PawnEditor.InGameDevButton.Desc".Translate());
        
        // Points & Economy
        listing.Label("PawnEditor.PointLimit".Translate() + ": " + Settings.PointLimit.ToStringMoney());
        Settings.PointLimit = listing.Slider(Settings.PointLimit, 100, 10000000);
        listing.CheckboxLabeled("PawnEditor.UseSilver".Translate(), ref Settings.UseSilver, "PawnEditor.UseSilver.Desc".Translate());
        listing.CheckboxLabeled("PawnEditor.CountNPCs".Translate(), ref Settings.CountNPCs, "PawnEditor.CountNPCs.Desc".Translate());
        
        // UI Options
        listing.CheckboxLabeled("PawnEditor.ShowEditButton".Translate(), ref Settings.ShowOpenButton, "PawnEditor.ShowEditButton.Desc".Translate());
        DrawHediffLocationSelector(listing);
        
        if (listing.ButtonText("PawnEditor.ResetConfirmation".Translate())) 
            Settings.DontShowAgain.Clear();

        listing.CheckboxLabeled("PawnEditor.EnforceHARRestrictions".Translate(), ref HARCompat.EnforceRestrictions, "PawnEditor.EnforceHARRestrictions.Desc".Translate());
        listing.CheckboxLabeled("PawnEditor.HideRandomFactions".Translate(), ref Settings.HideFactions, "PawnEditor.HideRandomFactions.Desc".Translate());

        DrawHotkeyPicker(listing);
        
        listing.End();
    }

    private void DrawHediffLocationSelector(Listing_Standard listing)
    {
        if (listing.ButtonTextLabeled("PawnEditor.HediffLocation".Translate(), $"PawnEditor.HediffLocation.{Settings.HediffLocationLimit}".Translate()))
        {
            Find.WindowStack.Add(new FloatMenu(Enum.GetValues(typeof(PawnEditorSettings.HediffLocation))
                .Cast<PawnEditorSettings.HediffLocation>()
                .Select(loc => new FloatMenuOption($"PawnEditor.HediffLocation.{loc}".Translate(), () => Settings.HediffLocationLimit = loc))
                .ToList()));
        }
    }

    private void DrawHotkeyPicker(Listing_Standard listing)
    {
        var hotkeyRect = listing.GetRect(30f);
        Widgets.Label(hotkeyRect.LeftHalf(), "PawnEditor.EditorHotkey".Translate());
        var hotkeyLabel = _waitingForHotkey ? "PawnEditor.PressAnyKey".Translate().ToString() : Settings.EditorHotkey.ToString();
        
        if (Widgets.ButtonText(hotkeyRect.RightHalf(), hotkeyLabel))
            _waitingForHotkey = true;
            
        if (_waitingForHotkey && Event.current.type == EventType.KeyDown && Event.current.keyCode != KeyCode.None)
        {
            Settings.EditorHotkey = Event.current.keyCode;
            _waitingForHotkey = false;
            Event.current.Use();
        }
    }

    // Lógica de Aplicación de Settings
    private void ApplySettings()
    {
        // Desaplicar parches existentes primero para evitar duplicados
        Harm.Unpatch(AccessTools.Method(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DoWindowContents)), HarmonyPatchType.Prefix, Harm.Id);
        Harm.Unpatch(AccessTools.Method(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DrawXenotypeEditorButton)), HarmonyPatchType.Prefix, Harm.Id);
        Harm.Unpatch(AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DrawButtons)), HarmonyPatchType.Transpiler, Harm.Id);
        Harm.Unpatch(AccessTools.Method(typeof(Pawn), nameof(Pawn.GetGizmos)), HarmonyPatchType.Postfix, Harm.Id);

        if (Settings.OverrideVanilla)
            Harm.Patch(AccessTools.Method(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DoWindowContents)),
                new(GetType(), nameof(PawnEditorPatches.OverrideVanilla)));
        else
            Harm.Patch(AccessTools.Method(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DrawXenotypeEditorButton)),
                new(GetType(), nameof(PawnEditorPatches.AddEditorButton)));

        if (Settings.ShowOpenButton)
            Harm.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.GetGizmos)), postfix: new(GetType(), nameof(PawnEditorPatches.AddEditButton)));

        if (Settings.InGameDevButton)
            Harm.Patch(AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DrawButtons)),
                transpiler: new(GetType(), nameof(PawnEditorPatches.AddDevButton)));
    }
    
    public static void Keybind()
    {
        bool triggered = false;
        try { triggered = KeyBindingDefOf.PawnEditor_OpenEditor.KeyDownEvent; } catch { }
        
        if (!triggered && Event.current.type == EventType.KeyDown && Event.current.keyCode == Settings.EditorHotkey)
        {
            triggered = true;
            Event.current.Use();
        }
        
        if (triggered && !PawnEditor.Pregame)
        {
            if (Find.WindowStack.IsOpen<Dialog_PawnEditor_InGame>()) 
                Find.WindowStack.TryRemove(typeof(Dialog_PawnEditor_InGame));
            else 
                Find.WindowStack.Add(new Dialog_PawnEditor_InGame());
        }
    }

    public override void WriteSettings()
    {
        base.WriteSettings();
        ApplySettings();
    }
}

// Attribute definitions (keep in a separate file or at bottom)
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class HotSwappableAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public class ModCompatAttribute : Attribute
{
    private readonly List<string> mods;
    public ModCompatAttribute(params string[] mods) => this.mods = mods.ToList();
    public bool ShouldActivate() => mods.Any(mod => ModLister.GetActiveModWithIdentifier(mod, true) != null);
}