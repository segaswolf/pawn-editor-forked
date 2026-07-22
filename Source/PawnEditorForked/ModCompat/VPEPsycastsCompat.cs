using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using Verse;

namespace PawnEditor;

/// <summary>
/// Blueprint persistence for Vanilla Psycasts Expanded.
///
/// The blueprint already stores the VANILLA psylink level, but VPE keeps everything that actually
/// matters on its own hediff (VanillaPsycastsExpanded.Hediff_PsycastAbilities): unlocked paths,
/// meditation foci, points, stat points, experience. The psycast abilities the pawn LEARNED live on
/// the VEF CompAbilities comp. None of that survived a save/load round trip, so a reloaded psycaster
/// came back with the RANDOM psycasts that ChangePsylinkLevel hands out.
///
/// All reflection, no assembly reference: if VPE isn't installed, every method here is a no-op.
/// See <see cref="PawnDuplicationUtility" /> for the duplicate-time equivalent (same VPE API).
/// </summary>
public static class VPEPsycastsCompat
{
    private const string HediffTypeName = "VanillaPsycastsExpanded.Hediff_PsycastAbilities";

    private static Type HediffType => AccessTools.TypeByName(HediffTypeName);

    private static object FindHediff(Pawn pawn, Type type)
    {
        if (pawn?.health?.hediffSet?.hediffs == null || type == null) return null;
        foreach (var hediff in pawn.health.hediffSet.hediffs)
            if (type.IsInstanceOfType(hediff))
                return hediff;
        return null;
    }

    private static ThingComp GetCompByTypeName(Pawn pawn, string simpleName)
    {
        if (pawn?.AllComps == null) return null;
        foreach (var comp in pawn.AllComps)
            if (comp.GetType().Name == simpleName)
                return comp;
        return null;
    }

    private static IEnumerable<string> DefNamesOf(object hediff, string fieldName)
    {
        if (AccessTools.Field(hediff.GetType(), fieldName)?.GetValue(hediff) is not IEnumerable entries) yield break;
        foreach (var entry in entries)
            if (entry is Def def && !def.defName.NullOrEmpty())
                yield return def.defName;
    }

    /// <summary>DefDatabase&lt;T&gt;.GetNamedSilentFail for a type we only know at runtime.</summary>
    private static Def GetDefNamed(Type defType, string defName)
    {
        if (defType == null || defName.NullOrEmpty()) return null;
        var method = typeof(DefDatabase<>).MakeGenericType(defType)
            .GetMethod("GetNamedSilentFail", BindingFlags.Public | BindingFlags.Static);
        return method?.Invoke(null, new object[] { defName }) as Def;
    }

    /// <summary>The element type of a List&lt;SomeDef&gt; field, so we can look defs up by name.</summary>
    private static Type ElementTypeOf(Type owner, string fieldName)
    {
        var field = AccessTools.Field(owner, fieldName);
        var type = field?.FieldType;
        return type is { IsGenericType: true } ? type.GetGenericArguments().FirstOrDefault() : null;
    }

    public static void Write(XmlWriter w, Pawn pawn)
    {
        var type = HediffType;
        if (type == null) return;

        try
        {
            var hediff = FindHediff(pawn, type);
            if (hediff == null) return;

            w.WriteStartElement("vpePsycasts");

            WriteDefList(w, "unlockedPaths", "path", DefNamesOf(hediff, "unlockedPaths"));
            WriteDefList(w, "unlockedMeditationFoci", "focus", DefNamesOf(hediff, "unlockedMeditationFoci"));

            WriteFieldIfPresent(w, hediff, type, "points");
            WriteFieldIfPresent(w, hediff, type, "statPoints");
            WriteFieldIfPresent(w, hediff, type, "experience");

            // The learned psycasts themselves live on VEF's CompAbilities, not on the hediff.
            var comp = GetCompByTypeName(pawn, "CompAbilities");
            var learned = comp == null
                ? null
                : AccessTools.Property(comp.GetType(), "LearnedAbilities")?.GetValue(comp)
                  ?? AccessTools.Field(comp.GetType(), "LearnedAbilities")?.GetValue(comp);

            if (learned is IEnumerable abilities)
            {
                w.WriteStartElement("abilities");
                foreach (var ability in abilities)
                {
                    if (ability == null) continue;
                    if (AccessTools.Field(ability.GetType(), "def")?.GetValue(ability) is Def def && !def.defName.NullOrEmpty())
                        w.WriteElementString("ability", def.defName);
                }
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }
        catch (Exception ex)
        {
            // Never eat it silently: a half-saved psycaster is worth a line in the log.
            Log.Warning($"[Pawn Editor] Could not save Vanilla Psycasts Expanded data: {ex.Message}");
        }
    }

    private static void WriteDefList(XmlWriter w, string listName, string entryName, IEnumerable<string> defNames)
    {
        w.WriteStartElement(listName);
        foreach (var defName in defNames) w.WriteElementString(entryName, defName);
        w.WriteEndElement();
    }

    private static void WriteFieldIfPresent(XmlWriter w, object hediff, Type type, string fieldName)
    {
        var value = AccessTools.Field(type, fieldName)?.GetValue(hediff);
        if (value != null) w.WriteElementString(fieldName, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Restores the saved psycast state. Must run AFTER the psylink level has been applied, because
    /// that is what creates the VPE hediff in the first place.
    /// </summary>
    public static void Read(XmlNode root, Pawn pawn)
    {
        var type = HediffType;
        if (type == null || root == null) return;

        try
        {
            var node = root.SelectSingleNode("vpePsycasts");
            if (node == null) return;

            var hediff = FindHediff(pawn, type);
            if (hediff == null)
            {
                Log.Warning($"[Pawn Editor] '{pawn?.LabelShortCap}' has saved Vanilla Psycasts Expanded data, but no "
                            + "psycast hediff to restore it onto (no psylink level?). Psycasts were not restored.");
                return;
            }

            // Wipe whatever random psycasts the pawn was generated with. Reset keeps the psylink level.
            AccessTools.Method(type, "Reset")?.Invoke(hediff, null);

            ApplyDefList(node, "unlockedPaths/path", ElementTypeOf(type, "unlockedPaths"),
                AccessTools.Method(type, "UnlockPath"), hediff);
            ApplyDefList(node, "unlockedMeditationFoci/focus", ElementTypeOf(type, "unlockedMeditationFoci"),
                AccessTools.Method(type, "UnlockMeditationFocus"), hediff);

            // After the unlocks, because UnlockPath spends points.
            SetNumericField(node, hediff, type, "points");
            SetNumericField(node, hediff, type, "statPoints");
            SetNumericField(node, hediff, type, "experience");

            RestoreAbilities(node, pawn);

            AccessTools.Method(type, "RecacheCurStage")?.Invoke(hediff, null);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Could not load Vanilla Psycasts Expanded data: {ex.Message}");
        }
    }

    private static void ApplyDefList(XmlNode node, string xpath, Type defType, MethodInfo unlock, object hediff)
    {
        if (defType == null || unlock == null) return;
        var nodes = node.SelectNodes(xpath);
        if (nodes == null) return;

        foreach (XmlNode entry in nodes)
        {
            var def = GetDefNamed(defType, entry.InnerText?.Trim());
            // A def can legitimately be gone (the mod that added that path was removed). Say so once
            // per entry rather than aborting the whole restore.
            if (def == null)
            {
                Log.Warning($"[Pawn Editor] Psycast entry '{entry.InnerText}' no longer exists, skipping it.");
                continue;
            }
            unlock.Invoke(hediff, new object[] { def });
        }
    }

    private static void SetNumericField(XmlNode node, object hediff, Type type, string fieldName)
    {
        var text = node.SelectSingleNode(fieldName)?.InnerText;
        if (text.NullOrEmpty()) return;

        var field = AccessTools.Field(type, fieldName);
        if (field == null) return;

        try
        {
            field.SetValue(hediff, Convert.ChangeType(text, field.FieldType, System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Could not restore psycast field '{fieldName}' (value '{text}'): {ex.Message}");
        }
    }

    private static void RestoreAbilities(XmlNode node, Pawn pawn)
    {
        var nodes = node.SelectNodes("abilities/ability");
        if (nodes == null || nodes.Count == 0) return;

        var comp = GetCompByTypeName(pawn, "CompAbilities");
        var give = comp == null ? null : AccessTools.Method(comp.GetType(), "GiveAbility");
        var parameters = give?.GetParameters();
        if (give == null || parameters == null || parameters.Length != 1) return;

        var paramType = parameters[0].ParameterType;
        // GiveAbility takes the ability DEF; work out which def type from the parameter itself so we
        // don't hardcode VEF's AbilityDef vs vanilla's.
        var defType = typeof(Def).IsAssignableFrom(paramType) ? paramType : null;
        if (defType == null) return;

        foreach (XmlNode entry in nodes)
        {
            var def = GetDefNamed(defType, entry.InnerText?.Trim());
            if (def == null)
            {
                Log.Warning($"[Pawn Editor] Psycast ability '{entry.InnerText}' no longer exists, skipping it.");
                continue;
            }
            give.Invoke(comp, new object[] { def });
        }
    }
}
