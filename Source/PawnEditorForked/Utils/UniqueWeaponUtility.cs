using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Odyssey unique weapons: their traits, color and generated name. A unique weapon rolls all three when it
/// is made (CompUniqueWeapon.PostPostMake), so every path that rebuilds a weapon from its def (blueprints,
/// duplicating, the Gear editor) used to hand back a different weapon with the same base.
/// Reported by ExoticButts (Steam, 30 Oct).
///
/// Verified against RimWorld 1.6 and 1.7 (CompUniqueWeapon): private traits, color and name, saved as
/// "traits", "color" and "name"; Setup(fromSave) applies trait abilities; CompArt.Title mirrors the name.
/// </summary>
public static class UniqueWeaponUtility
{
    private static readonly FieldInfo ColorField = AccessTools.Field(typeof(CompUniqueWeapon), "color");
    private static readonly FieldInfo NameField = AccessTools.Field(typeof(CompUniqueWeapon), "name");
    private static readonly FieldInfo IgnoreMalusesField = AccessTools.Field(typeof(CompUniqueWeapon), "ignoreAccuracyMaluses");

    public sealed class Data
    {
        public List<WeaponTraitDef> Traits = new();
        public ColorDef Color;
        public string Name;
    }

    /// <summary>The unique part of a weapon, or null for an ordinary thing.</summary>
    public static Data Capture(Thing thing)
    {
        if (thing?.TryGetComp<CompUniqueWeapon>() is not { } comp) return null;
        return new Data
        {
            Traits = comp.TraitsListForReading.ToList(),
            Color = ColorField?.GetValue(comp) as ColorDef,
            Name = NameField?.GetValue(comp) as string
        };
    }

    /// <summary>Makes <paramref name="thing"/> carry exactly these traits, color and name.</summary>
    public static void Apply(Thing thing, Data data)
    {
        if (data == null || thing?.TryGetComp<CompUniqueWeapon>() is not { } comp) return;
        try
        {
            var traits = comp.TraitsListForReading;
            traits.Clear();
            traits.AddRange(data.Traits.Where(trait => trait != null));
            if (data.Color != null) ColorField?.SetValue(comp, data.Color);
            if (!data.Name.NullOrEmpty())
            {
                NameField?.SetValue(comp, data.Name);
                if (thing.TryGetComp<CompArt>() is { } art) art.Title = data.Name;
            }
            IgnoreMalusesField?.SetValue(comp, null); // cached from the old traits
            comp.Setup(fromSave: false);              // trait abilities (e.g. a reloadable ability)
            thing.Notify_ColorChanged();
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Could not restore unique weapon {thing.LabelShort}: {ex.Message}");
        }
    }

    public static void CopyTo(Thing source, Thing target) => Apply(target, Capture(source));

    /// <summary>Writes &lt;uniqueWeapon name=".." color=".."&gt;&lt;trait&gt;..&lt;/trait&gt;&lt;/uniqueWeapon&gt; inside the current element.</summary>
    public static void Write(XmlWriter w, Thing thing)
    {
        var data = Capture(thing);
        if (data == null) return;
        w.WriteStartElement("uniqueWeapon");
        if (!data.Name.NullOrEmpty()) w.WriteAttributeString("name", data.Name);
        if (data.Color != null) w.WriteAttributeString("color", data.Color.defName);
        foreach (var trait in data.Traits.Where(trait => trait != null))
            w.WriteElementString("trait", trait.defName);
        w.WriteEndElement();
    }

    /// <summary>Reads what Write wrote. Traits from mods no longer loaded are skipped with a warning.</summary>
    public static void Read(XmlNode parent, Thing thing)
    {
        var node = parent?.SelectSingleNode("uniqueWeapon");
        if (node == null) return;
        var data = new Data
        {
            Name = node.Attributes?["name"]?.Value,
            Color = DefDatabase<ColorDef>.GetNamedSilentFail(node.Attributes?["color"]?.Value ?? "")
        };
        foreach (XmlNode traitNode in node.SelectNodes("trait"))
        {
            var trait = DefDatabase<WeaponTraitDef>.GetNamedSilentFail(traitNode.InnerText);
            if (trait != null) data.Traits.Add(trait);
            else Log.Warning($"[Pawn Editor] Unique weapon trait '{traitNode.InnerText}' is not loaded; {thing.LabelShort} keeps the others.");
        }
        Apply(thing, data);
    }
}
