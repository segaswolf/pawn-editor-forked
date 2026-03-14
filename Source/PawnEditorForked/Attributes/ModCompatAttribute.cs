using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace PawnEditor;

[AttributeUsage(AttributeTargets.Class)]
public class ModCompatAttribute : Attribute
{
    private readonly List<string> _mods;

    public ModCompatAttribute(params string[] mods)
    {
        _mods = mods.ToList();
    }

    public bool ShouldActivate()
    {
        return _mods.Any(mod => ModLister.GetActiveModWithIdentifier(mod, true) != null);
    }
}