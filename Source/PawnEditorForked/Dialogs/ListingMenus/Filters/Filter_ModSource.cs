using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnEditor;

public class Filter_ModSource<T> : Filter_Dropdown<T> where T : Def
{
    public Filter_ModSource(bool enabledByDefault = false) : base(
        "Source".Translate(), BuildSourceOptions(), enabledByDefault, "PawnEditor.SourceDesc".Translate()) { }

    /// <summary>
    /// Two loaded mods CAN share a display name, and ToDictionary throws on a duplicate key. This filter
    /// is attached to every picker in the mod, so that single throw would take down whichever listing
    /// the player happened to open, over a cosmetic dropdown. Built defensively instead: blank names are
    /// skipped and the first mod wins for a given name.
    ///
    /// Merging same-named mods costs nothing here: the predicate already matched by NAME, not by mod
    /// instance, so both entries would have filtered identically anyway.
    /// </summary>
    private static Dictionary<string, Func<T, bool>> BuildSourceOptions()
    {
        var result = new Dictionary<string, Func<T, bool>>();

        foreach (var mod in LoadedModManager.runningMods)
        {
            if (mod == null || mod.Name.NullOrEmpty() || result.ContainsKey(mod.Name)) continue;
            if (!mod.AllDefs.OfType<T>().Any()) continue;

            var name = mod.Name;
            result[name] = d => d.modContentPack?.Name == name;
        }

        return result;
    }
}
