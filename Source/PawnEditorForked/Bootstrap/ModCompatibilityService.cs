using System;
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
        foreach (var assembly in _content.assemblies.loadedAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!type.TryGetAttribute<ModCompatAttribute>(out var modCompat) || !modCompat.ShouldActivate())
                {
                    continue;
                }

                TryInvoke(type, "Activate", Type.EmptyTypes, null);
                TryInvoke(type, "Activate", new[] { typeof(Harmony) }, new object[] { _harmony });

                var activeField = AccessTools.Field(type, "Active");
                activeField?.SetValue(null, true);

                var name = ResolveCompatName(type);
                if (!string.IsNullOrEmpty(name) && Prefs.DevMode)
                {
                    Log.Message("[Pawn Editor] " + name + " compatibility active.");
                }
            }
        }
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