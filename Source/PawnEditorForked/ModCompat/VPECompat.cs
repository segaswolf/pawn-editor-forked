using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[ModCompat("vanillaexpanded.vpsycastse", "VanillaExpanded.VPsycastsE")]
public static class VPECompat
{
    public static bool Active;
    public static string Name = "Vanilla Psycasts Expanded";

    private static Type hediffPsycastAbilitiesType;
    private static Type psycasterPathDefType;

    private static MethodInfo recacheCurStageMethod;
    private static MethodInfo unlockMeditationFocusMethod;
    private static MethodInfo unlockPathMethod;
    private static FieldInfo unlockedMeditationFociField;
    private static FieldInfo unlockedPathsField;

    [UsedImplicitly]
    public static void Activate()
    {
        hediffPsycastAbilitiesType = AccessTools.TypeByName("VanillaPsycastsExpanded.Hediff_PsycastAbilities");
        psycasterPathDefType = AccessTools.TypeByName("VanillaPsycastsExpanded.PsycasterPathDef");
        unlockedMeditationFociField = AccessTools.Field(hediffPsycastAbilitiesType, "unlockedMeditationFoci");
        unlockedPathsField = AccessTools.Field(hediffPsycastAbilitiesType, "unlockedPaths");
        recacheCurStageMethod = AccessTools.Method(hediffPsycastAbilitiesType, "RecacheCurStage");
        unlockMeditationFocusMethod = AccessTools.Method(hediffPsycastAbilitiesType, "UnlockMeditationFocus", new[] { typeof(MeditationFocusDef) });
        unlockPathMethod = AccessTools.Method(hediffPsycastAbilitiesType, "UnlockPath", new[] { psycasterPathDefType });
    }

    public static bool HasPsycasts(Pawn pawn) => FindPsycastHediff(pawn) != null;

    public static int GetLevel(Pawn pawn)
    {
        if (FindPsycastHediff(pawn) is Hediff_Level levelHediff)
            return levelHediff.level;

        return pawn?.GetPsylinkLevel() ?? 0;
    }

    public static void SetLevel(Pawn pawn, int value)
    {
        if (FindPsycastHediff(pawn) == null && value > 0)
            TabWorker_Royalty.SetPsylinkLevel(pawn, value);

        if (FindPsycastHediff(pawn) is Hediff_Level levelHediff)
        {
            levelHediff.level = Mathf.Max(0, value);
            levelHediff.Severity = levelHediff.level;
            SetPsylinkLevel(pawn, levelHediff.level);
            Recache(levelHediff);
        }
    }

    public static List<Def> GetUnlockedMeditationFoci(Pawn pawn) => GetDefList(FindPsycastHediff(pawn), unlockedMeditationFociField);
    public static List<Def> GetUnlockedPaths(Pawn pawn) => GetDefList(FindPsycastHediff(pawn), unlockedPathsField);
    public static List<Def> AllMeditationFocusDefs() => DefDatabase<MeditationFocusDef>.AllDefs
        .OrderByDescending(def => def.modContentPack.IsOfficialMod)
        .ThenBy(def => def.label ?? def.defName)
        .Cast<Def>()
        .ToList();

    public static List<Def> AllPathDefs() => GetAllDefs(psycasterPathDefType)
        .OrderBy(def => def.label ?? def.defName)
        .ToList();

    public static void AddUnlockedMeditationFocus(Pawn pawn, Def focus)
    {
        var hediff = FindPsycastHediff(pawn);
        var list = EnsureList(hediff, unlockedMeditationFociField);
        if (list == null || focus is not MeditationFocusDef meditationFocus || ContainsDef(list, focus))
            return;

        if (unlockMeditationFocusMethod != null)
            unlockMeditationFocusMethod.Invoke(hediff, new object[] { meditationFocus });
        else
        {
            list.Add(meditationFocus);
            MeditationFocusTypeAvailabilityCache.ClearFor(pawn);
        }
    }

    public static void RemoveUnlockedMeditationFocus(Pawn pawn, Def focus)
    {
        var list = EnsureList(FindPsycastHediff(pawn), unlockedMeditationFociField);
        RemoveDef(list, focus);
        MeditationFocusTypeAvailabilityCache.ClearFor(pawn);
    }

    public static void AddUnlockedPath(Pawn pawn, Def path)
    {
        var hediff = FindPsycastHediff(pawn);
        var list = EnsureList(hediff, unlockedPathsField);
        if (list == null || path == null || ContainsDef(list, path))
            return;

        if (unlockPathMethod != null)
            unlockPathMethod.Invoke(hediff, new object[] { path });
        else
            list.Add(path);
    }

    public static void RemoveUnlockedPath(Pawn pawn, Def path)
    {
        var list = EnsureList(FindPsycastHediff(pawn), unlockedPathsField);
        RemoveDef(list, path);
    }

    private static object FindPsycastHediff(Pawn pawn)
    {
        if (pawn?.health?.hediffSet?.hediffs == null || hediffPsycastAbilitiesType == null)
            return null;

        return pawn.health.hediffSet.hediffs.FirstOrDefault(hediff => hediffPsycastAbilitiesType.IsInstanceOfType(hediff));
    }

    private static void SetPsylinkLevel(Pawn pawn, int level)
    {
        var psylink = pawn?.health?.hediffSet?.hediffs?.OfType<Hediff_Psylink>().FirstOrDefault();
        if (psylink != null)
        {
            psylink.level = level;
            psylink.Severity = level;
        }
    }

    private static void Recache(object hediff) => recacheCurStageMethod?.Invoke(hediff, Array.Empty<object>());

    private static List<Def> GetDefList(object owner, FieldInfo field)
    {
        if (field?.GetValue(owner) is not IEnumerable list)
            return new List<Def>();

        return list.OfType<Def>().ToList();
    }

    private static IList EnsureList(object owner, FieldInfo field)
    {
        if (owner == null || field == null)
            return null;

        if (field.GetValue(owner) is IList existing)
            return existing;

        if (!field.FieldType.IsGenericType)
            return null;

        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(field.FieldType.GetGenericArguments()[0]));
        field.SetValue(owner, list);
        return list;
    }

    private static bool ContainsDef(IList list, Def def) => list?.Cast<object>().OfType<Def>().Any(existing => existing == def) == true;

    private static void RemoveDef(IList list, Def def)
    {
        if (list == null || def == null)
            return;

        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == def)
                list.RemoveAt(i);
        }
    }

    private static List<Def> GetAllDefs(Type defType)
    {
        if (defType == null)
            return new List<Def>();

        return GenDefDatabase.GetAllDefsInDatabaseForDef(defType).OfType<Def>().ToList();
    }

}

internal sealed class VPEPsycastEditor
{
    private readonly Pawn pawn;
    private string levelBuffer;

    public VPEPsycastEditor(Pawn pawn)
    {
        this.pawn = pawn;
    }

    public void Draw(Listing_Standard listing)
    {
        DrawIntRow(listing, "PawnEditor.Royalty.PsylinkLevel".Translate(), VPECompat.GetLevel(pawn), ref levelBuffer, 0, 1000,
            value => VPECompat.SetLevel(pawn, value));

        if (!VPECompat.HasPsycasts(pawn))
        {
            listing.GapLine();
            listing.Label("PawnEditor.VPE.NoPsycasts".Translate());
            return;
        }

        listing.GapLine();
        DrawMeditationFoci(listing);
        listing.GapLine();
        DrawPaths(listing);
    }

    private void DrawMeditationFoci(Listing_Standard listing)
    {
        var unlocked = VPECompat.GetUnlockedMeditationFoci(pawn);
        DrawSectionHeader(listing, "PawnEditor.VPE.MeditationType".Translate(), "Add".Translate().CapitalizeFirst(), () =>
        {
            var options = VPECompat.AllMeditationFocusDefs()
                .Where(focus => !unlocked.Contains(focus))
                .Select(focus => new FloatMenuOption(focus.LabelCap, () => VPECompat.AddUnlockedMeditationFocus(pawn, focus)))
                .ToList();

            Find.WindowStack.Add(new FloatMenu(options));
        });

        foreach (var focus in unlocked.OrderBy(def => def.label ?? def.defName).ToList())
            DrawRemovableDefRow(listing, focus, () => VPECompat.RemoveUnlockedMeditationFocus(pawn, focus));
    }

    private void DrawPaths(Listing_Standard listing)
    {
        var unlocked = VPECompat.GetUnlockedPaths(pawn);
        DrawSectionHeader(listing, "PawnEditor.VPE.PsycastPath".Translate(), "Add".Translate().CapitalizeFirst(), () =>
        {
            var options = VPECompat.AllPathDefs()
                .Where(path => !unlocked.Contains(path))
                .Select(path => new FloatMenuOption(path.LabelCap, () => VPECompat.AddUnlockedPath(pawn, path)))
                .ToList();

            Find.WindowStack.Add(new FloatMenu(options));
        });

        foreach (var path in unlocked.OrderBy(def => def.label ?? def.defName).ToList())
            DrawRemovableDefRow(listing, path, () => VPECompat.RemoveUnlockedPath(pawn, path));
    }

    private static void DrawSectionHeader(Listing_Standard listing, string label, string buttonLabel, Action buttonAction)
    {
        var rect = listing.GetRect(32f);
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(rect.LeftPart(0.65f), label.Colorize(ColoredText.TipSectionTitleColor));

        if (Widgets.ButtonText(rect.RightPartPixels(110f), buttonLabel))
            buttonAction();
    }

    private static void DrawRemovableDefRow(Listing_Standard listing, Def def, Action removeAction)
    {
        var rect = listing.GetRect(28f);
        if (Mouse.IsOver(rect))
            Widgets.DrawHighlight(rect);

        Widgets.Label(rect.LeftPart(0.82f), def.LabelCap);
        if (Widgets.ButtonImage(rect.RightPartPixels(26f).ContractedBy(4f), TexButton.Delete))
            removeAction();
    }

    private static void DrawIntRow(Listing_Standard listing, string label, int currentValue, ref string buffer, int min, int max, Action<int> setValue)
    {
        var rect = listing.GetRect(30f);
        Widgets.Label(rect.LeftPart(0.45f), label);
        var value = currentValue;
        Widgets.TextFieldNumeric(rect.RightPart(0.35f), ref value, ref buffer, min, max);
        if (value != currentValue)
            setValue(value);
    }

}
