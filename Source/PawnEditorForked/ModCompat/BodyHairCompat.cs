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
/// Optional support for Body Hair Framework by Lucius (workshop 3823805934, lucius.bodyhairframework), and
/// so for the mods that use it, such as Morton's Body Hair (3825963278). Requested by Lucius (Steam, 25 Oct).
///
/// Verified against its decompiled assembly (Dev Notes/BodyHair_decompiled, version of 27 Oct 2026):
///   BodyHair.BHF_BodyHairComp      CurrentFor(slot) returns BHF_None when unset; SetStyle(slot, style)
///                                  (null means BHF_None; throws for a style of another slot).
///   BodyHair.BHF_BodyHairDef       : StyleItemDef. IsForSlot(slot) (BHF_None fits every slot) and
///                                  CanUse(pawn): humanlike, no BHF_NoBodyHair or VREA_SyntheticSkin gene,
///                                  allowed body type, required gene or mutant, and a texture for the body type.
///   BodyHair.BHF_BodyHairSlotDef   order (render layer); BHF_Torso, BHF_LowerBody and BHF_Back by default.
/// The comp is added to the Human ThingDef only, so other races have no body hair tab.
/// All reflection by type name: with the mod absent this class is inert.
/// </summary>
public static class BodyHairCompat
{
    private static readonly Type CompType = AccessTools.TypeByName("BodyHair.BHF_BodyHairComp");
    private static readonly Type StyleType = AccessTools.TypeByName("BodyHair.BHF_BodyHairDef");
    private static readonly Type SlotType = AccessTools.TypeByName("BodyHair.BHF_BodyHairSlotDef");

    private static readonly MethodInfo CurrentForMethod = Method(CompType, "CurrentFor", SlotType);
    private static readonly MethodInfo SetStyleMethod = Method(CompType, "SetStyle", SlotType, StyleType);
    private static readonly MethodInfo IsForSlotMethod = Method(StyleType, "IsForSlot", SlotType);
    private static readonly MethodInfo CanUseMethod = Method(StyleType, "CanUse", typeof(Pawn));
    private static readonly FieldInfo OrderField = SlotType == null ? null : AccessTools.Field(SlotType, "order");
    private static readonly FieldInfo SpawnAutomaticallyField = StyleType == null ? null : AccessTools.Field(StyleType, "spawnAutomatically");
    private static readonly FieldInfo AllowedGendersField = StyleType == null ? null : AccessTools.Field(StyleType, "allowedGenders");

    private static List<Def> slots;
    private static List<StyleItemDef> styles;
    private static StyleItemDef none;
    private static bool warned;

    public static bool Active => CurrentForMethod != null && SetStyleMethod != null && IsForSlotMethod != null && CanUseMethod != null;

    /// <summary>The body hair slots, top layer first (torso, lower body, back by default).</summary>
    public static List<Def> Slots => slots ??= !Active ? new List<Def>()
        : GenDefDatabase.GetAllDefsInDatabaseForDef(SlotType).OrderByDescending(slot => OrderField?.GetValue(slot) as int? ?? 0).ToList();

    private static List<StyleItemDef> Styles => styles ??= !Active ? new List<StyleItemDef>()
        : GenDefDatabase.GetAllDefsInDatabaseForDef(StyleType).OfType<StyleItemDef>().ToList();

    /// <summary>The "no body hair" style, which fits every slot.</summary>
    public static StyleItemDef None => none ??= Active ? GenDefDatabase.GetDefSilentFail(StyleType, "BHF_None", false) as StyleItemDef : null;

    /// <summary>The pawn has body hair data (only races the framework patches, Human by default).</summary>
    public static bool HasBodyHair(Pawn pawn) => Comp(pawn) != null;

    /// <summary>Every style of that slot the pawn can wear, "none" first. The framework's own CanUse decides.</summary>
    public static IEnumerable<StyleItemDef> StylesFor(Def slot, Pawn pawn)
    {
        if (None != null) yield return None;
        foreach (var style in Styles)
            if (style != None && FitsSlot(style, slot) && CanUse(style, pawn))
                yield return style;
    }

    /// <summary>The pawn can wear at least one real style in that slot (a gene, an android body or a missing texture can rule them all out).</summary>
    public static bool AnyUsable(Def slot, Pawn pawn) => StylesFor(slot, pawn).Any(style => style != None);

    public static StyleItemDef Current(Pawn pawn, Def slot) =>
        Call(() => CurrentForMethod.Invoke(Comp(pawn), new object[] { slot }) as StyleItemDef, "read", pawn);

    public static void Set(Pawn pawn, Def slot, StyleItemDef style)
    {
        var comp = Comp(pawn);
        if (comp == null || slot == null) return;
        Call<object>(() => SetStyleMethod.Invoke(comp, new object[] { slot, style }), $"set {style?.defName} on {slot.defName}", pawn);
    }

    /// <summary>Slot to style, for Cancel and duplicating. Empty when the pawn has no body hair data.</summary>
    public static Dictionary<Def, StyleItemDef> Capture(Pawn pawn)
    {
        var result = new Dictionary<Def, StyleItemDef>();
        if (!HasBodyHair(pawn)) return result;
        foreach (var slot in Slots)
            if (Current(pawn, slot) is { } style) result[slot] = style;
        return result;
    }

    public static void Apply(Pawn pawn, Dictionary<Def, StyleItemDef> captured)
    {
        if (captured == null || !HasBodyHair(pawn)) return;
        foreach (var pair in captured) Set(pawn, pair.Key, pair.Value);
    }

    /// <summary>
    /// A random style per slot, drawn like the framework does for new pawns: "none" or a style marked to spawn
    /// automatically, that fits the pawn's gender. Returns false when there was nothing to change.
    /// </summary>
    public static bool Randomize(Pawn pawn)
    {
        if (!HasBodyHair(pawn)) return false;
        var changed = false;
        foreach (var slot in Slots)
        {
            var options = StylesFor(slot, pawn).Where(style => style == None || SpawnsFor(style, pawn)).ToList();
            if (!options.TryRandomElement(out var style)) continue;
            Set(pawn, slot, style);
            changed = true;
        }
        return changed;
    }

    // ── Blueprints ──

    /// <summary>Writes &lt;bodyHair&gt;&lt;slot def="BHF_Torso" style="..."/&gt;...&lt;/bodyHair&gt;. Nothing without the mod or the data.</summary>
    public static void WriteBlueprint(XmlWriter w, Pawn pawn)
    {
        var captured = Capture(pawn);
        if (captured.Count == 0) return;
        w.WriteStartElement("bodyHair");
        foreach (var pair in captured)
        {
            w.WriteStartElement("slot");
            w.WriteAttributeString("def", pair.Key.defName);
            w.WriteAttributeString("style", pair.Value.defName);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    /// <summary>Restores what it can: a slot or style from a mod that is no longer loaded is skipped with a warning.</summary>
    public static void LoadBlueprint(XmlNode node, Pawn pawn)
    {
        if (node == null || !Active || !HasBodyHair(pawn)) return;
        foreach (XmlNode entry in node.SelectNodes("slot"))
        {
            var slotName = entry.Attributes?["def"]?.Value;
            var styleName = entry.Attributes?["style"]?.Value;
            var slot = GenDefDatabase.GetDefSilentFail(SlotType, slotName, false);
            var style = GenDefDatabase.GetDefSilentFail(StyleType, styleName, false) as StyleItemDef;
            if (slot == null || style == null)
            {
                Log.Warning($"[Pawn Editor] Blueprint body hair {styleName} on {slotName} is not loaded; that slot keeps its current style.");
                continue;
            }
            if (FitsSlot(style, slot)) Set(pawn, slot, style);
        }
    }

    // ── Helpers ──

    private static bool FitsSlot(StyleItemDef style, Def slot) =>
        Call(() => (bool)IsForSlotMethod.Invoke(style, new object[] { slot }), "check the slot of " + style.defName, null);

    private static bool CanUse(StyleItemDef style, Pawn pawn) =>
        Call(() => (bool)CanUseMethod.Invoke(style, new object[] { pawn }), "check " + style.defName, pawn);

    private static bool SpawnsFor(StyleItemDef style, Pawn pawn)
    {
        if (SpawnAutomaticallyField?.GetValue(style) is not true) return false;
        return AllowedGendersField?.GetValue(style) is not List<Gender> genders || genders.Contains(pawn.gender);
    }

    private static ThingComp Comp(Pawn pawn)
    {
        if (!Active || pawn?.AllComps == null) return null;
        foreach (var comp in pawn.AllComps)
            if (CompType.IsInstanceOfType(comp)) return comp;
        return null;
    }

    private static MethodInfo Method(Type type, string name, params Type[] args) =>
        type == null || args.Any(arg => arg == null) ? null : AccessTools.Method(type, name, args);

    /// <summary>Runs a call into the framework; a failure warns once and returns the default instead of breaking the editor.</summary>
    private static T Call<T>(Func<T> call, string what, Pawn pawn)
    {
        try { return call(); }
        catch (Exception ex)
        {
            if (!warned)
            {
                warned = true;
                Log.Warning($"[Pawn Editor] Body Hair Framework: could not {what}{(pawn == null ? "" : " for " + pawn.LabelShort)}: {ex.InnerException?.Message ?? ex.Message}");
            }
            return default;
        }
    }
}
