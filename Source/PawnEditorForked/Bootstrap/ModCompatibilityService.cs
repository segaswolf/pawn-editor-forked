using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace PawnEditor;

public sealed class ModCompatibilityService
{
    private readonly ModContentPack _content;
    private readonly Harmony _harmony;

    public ModCompatibilityService(ModContentPack content, Harmony harmony)
    {
        _content = content;
        _harmony = harmony;
    }

    public void Initialize()
    {
        LongEventHandler.ExecuteWhenFinished(delegate
        {
            ActivateCompatClasses();
            PawnEditorMod.Instance?.WriteSettings();
        });
    }

    private void ActivateCompatClasses()
    {
        var activated = new List<(string Name, Type Type)>();

        foreach (var assembly in _content.assemblies.loadedAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!type.TryGetAttribute<ModCompatAttribute>(out var modCompat) || !modCompat.ShouldActivate())
                {
                    continue;
                }

                var name = ResolveCompatName(type);
                var label = string.IsNullOrEmpty(name) ? type.Name : name;

                // Una frontera por compat. Sin esto, un Activate que reviente se lleva por delante a
                // todos los compat que vinieran después en el bucle, y el usuario pierde soporte para
                // mods que no tienen nada que ver con el que falló.
                Diagnostics.Run($"Activating {label} compatibility", null, () =>
                {
                    TryInvoke(type, "Activate", Type.EmptyTypes, null);
                    TryInvoke(type, "Activate", new[] { typeof(Harmony) }, new object[] { _harmony });
                });

                var activeField = AccessTools.Field(type, "Active");
                activeField?.SetValue(null, true);

                if (string.IsNullOrEmpty(name)) continue;

                activated.Add((name, type));
                if (Prefs.DevMode)
                {
                    Log.Message("[Pawn Editor] " + name + " compatibility active.");
                }
            }
        }

        // One consolidated line, so a mod that updated and broke our reflection shows up on the first
        // launch instead of arriving weeks later as a player report. See ModCompatReport.
        ModCompatReport.Emit(activated);
    }

    private static void TryInvoke(Type type, string methodName, Type[] parameters, object[] args)
    {
        var method = AccessTools.Method(type, methodName, parameters);
        method?.Invoke(null, args ?? Array.Empty<object>());
    }

    private static string ResolveCompatName(Type type)
    {
        var method = AccessTools.Method(type, "GetName");
        var name = method?.Invoke(null, Array.Empty<object>()) as string;

        if (string.IsNullOrEmpty(name))
        {
            method = AccessTools.Method(type, "get_Name");
            name = method?.Invoke(null, Array.Empty<object>()) as string;
        }

        if (string.IsNullOrEmpty(name))
        {
            var field = AccessTools.Field(type, "Name");
            name = field?.GetValue(null) as string;
        }

        return name;
    }
}