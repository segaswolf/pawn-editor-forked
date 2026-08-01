using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[ModCompat("ferny.characterdevelopment")]
public static class CharacterDevelopmentCompat
{
    public static bool Active;
    public static string Name = "Character Development";

    private static Type pawnWantsDataType;
    private static Type activeWantType;
    private static Type activeWantWithTargetType;
    private static Type activeWantWithPawnTargetType;
    private static Type wantDefType;
    private static Type wantWorkerType;
    private static Type quirkType;
    private static Type grantedGeneLinkType;
    private static Type rewardDefType;
    private static Type rewardWorkerType;
    private static MethodInfo getWantsDataMethod;
    private static MethodInfo tryGetWantsDataMethod;
    private static MethodInfo canHaveWantsMethod;
    private static MethodInfo initializePawnWantsMethod;
    private static MethodInfo addWantMethod;
    private static MethodInfo completeWantMethod;
    private static MethodInfo rerollWantMethod;
    private static MethodInfo addQuirkMethod;
    private static FieldInfo activeWantsField;
    private static FieldInfo quirksField;
    private static FieldInfo grantedGenesField;
    private static FieldInfo nextWantTickField;
    private static FieldInfo activeWantDefField;
    private static FieldInfo activeWantAssignedTickField;
    private static FieldInfo activeWantRerollCountField;
    private static FieldInfo activeWantTargetDefField;
    private static FieldInfo activeWantTargetPawnField;
    private static PropertyInfo activeWantLabelProperty;
    private static PropertyInfo activeWantDescriptionProperty;
    private static PropertyInfo activeWantIconProperty;
    private static FieldInfo wantDefMentalBreakField;
    private static PropertyInfo wantDefWorkerProperty;
    private static PropertyInfo wantDefIconProperty;
    private static MethodInfo wantCanHaveMethod;
    private static MethodInfo wantCanGenerateMethod;
    private static FieldInfo quirkDefField;
    private static FieldInfo quirkItemField;
    private static FieldInfo quirkPawnTargetField;
    private static ConstructorInfo quirkConstructor;
    private static ConstructorInfo grantedGeneLinkConstructor;
    private static FieldInfo grantedGeneLinkGeneField;
    private static FieldInfo grantedGeneLinkQuirkField;
    private static PropertyInfo quirkLabelProperty;
    private static PropertyInfo quirkDescriptionProperty;
    private static FieldInfo rewardDefIsQuirkField;
    private static FieldInfo rewardDefRequiresItemField;
    private static FieldInfo rewardDefRequiresPawnField;
    private static PropertyInfo rewardDefWorkerProperty;
    private static PropertyInfo rewardDefIconProperty;
    private static MethodInfo rewardCanGenerateMethod;
    private static MethodInfo rewardCanBestowMethod;
    private static MethodInfo rewardGetValidItemsMethod;
    private static MethodInfo rewardGetValidPawnsMethod;
    private static MethodInfo rewardOnRemovedMethod;
    private static FieldInfo settingsField;
    private static FieldInfo rerollsPerWantField;
    private static FieldInfo maxActiveWantsField;
    private static bool ready;

    public static bool Available => Active && ready;

    public static void Activate()
    {
        var utilityType = AccessTools.TypeByName("WantsAndQuirks.WantsAndQuirksUtility");
        pawnWantsDataType = AccessTools.TypeByName("WantsAndQuirks.PawnWantsData");
        activeWantType = AccessTools.TypeByName("WantsAndQuirks.ActiveWant");
        activeWantWithTargetType = AccessTools.TypeByName("WantsAndQuirks.ActiveWantWithTarget");
        activeWantWithPawnTargetType = AccessTools.TypeByName("WantsAndQuirks.ActiveWantWithPawnTarget");
        wantDefType = AccessTools.TypeByName("WantsAndQuirks.WantDef");
        wantWorkerType = AccessTools.TypeByName("WantsAndQuirks.WantWorker");
        quirkType = AccessTools.TypeByName("WantsAndQuirks.Quirk");
        grantedGeneLinkType = AccessTools.TypeByName("WantsAndQuirks.GrantedGeneLink");
        rewardDefType = AccessTools.TypeByName("WantsAndQuirks.RewardDef");
        rewardWorkerType = AccessTools.TypeByName("WantsAndQuirks.RewardWorker");
        var modType = AccessTools.TypeByName("WantsAndQuirks.WantsAndQuirksMod");
        var settingsType = AccessTools.TypeByName("WantsAndQuirks.WantsAndQuirksSettings");

        if (new[]
            {
                utilityType,
                pawnWantsDataType,
                activeWantType,
                activeWantWithTargetType,
                activeWantWithPawnTargetType,
                wantDefType,
                wantWorkerType,
                quirkType,
                grantedGeneLinkType,
                rewardDefType,
                rewardWorkerType,
                modType,
                settingsType
            }.Any(type => type == null))
        {
            ready = false;
            Log.Error("[Pawn Editor] Character Development compatibility could not resolve its required API.");
            return;
        }

        getWantsDataMethod = AccessTools.Method(utilityType, "GetWantsData", new[] { typeof(Pawn) });
        tryGetWantsDataMethod = AccessTools.Method(utilityType, "TryGetWantsData",
            new[] { typeof(Pawn), pawnWantsDataType.MakeByRefType() });
        canHaveWantsMethod = AccessTools.Method(utilityType, "CanHaveWants", new[] { typeof(Pawn) });
        initializePawnWantsMethod = AccessTools.Method(utilityType, "InitializePawnWants",
            new[] { typeof(Pawn), pawnWantsDataType });
        addWantMethod = AccessTools.Method(utilityType, "AddWant",
            new[] { typeof(Pawn), pawnWantsDataType, wantDefType, typeof(Def), typeof(bool) });
        completeWantMethod = AccessTools.Method(utilityType, "CompleteWant",
            new[] { typeof(Pawn), pawnWantsDataType, activeWantType });
        rerollWantMethod = AccessTools.Method(utilityType, "RerollWant",
            new[] { typeof(Pawn), pawnWantsDataType, activeWantType });
        addQuirkMethod = AccessTools.Method(utilityType, "AddQuirk",
            new[] { typeof(Pawn), rewardDefType, typeof(ThingDef), typeof(Pawn) });

        activeWantsField = AccessTools.Field(pawnWantsDataType, "activeWants");
        quirksField = AccessTools.Field(pawnWantsDataType, "quirks");
        grantedGenesField = AccessTools.Field(pawnWantsDataType, "grantedGenes");
        nextWantTickField = AccessTools.Field(pawnWantsDataType, "nextWantTick");
        activeWantDefField = AccessTools.Field(activeWantType, "def");
        activeWantAssignedTickField = AccessTools.Field(activeWantType, "assignedTick");
        activeWantRerollCountField = AccessTools.Field(activeWantType, "rerollCount");
        activeWantTargetDefField = AccessTools.Field(activeWantWithTargetType, "targetDef");
        activeWantTargetPawnField = AccessTools.Field(activeWantWithPawnTargetType, "targetPawn");
        activeWantLabelProperty = AccessTools.Property(activeWantType, "LabelCap");
        activeWantDescriptionProperty = AccessTools.Property(activeWantType, "Description");
        activeWantIconProperty = AccessTools.Property(activeWantType, "Icon");
        wantDefMentalBreakField = AccessTools.Field(wantDefType, "isMentalBreakWant");
        wantDefWorkerProperty = AccessTools.Property(wantDefType, "Worker");
        wantDefIconProperty = AccessTools.Property(wantDefType, "Icon");
        wantCanHaveMethod = AccessTools.Method(wantWorkerType, "CanHaveWant", new[] { typeof(Pawn) });
        wantCanGenerateMethod = AccessTools.Method(wantWorkerType, "CanGenerate", new[] { typeof(Pawn) });

        quirkDefField = AccessTools.Field(quirkType, "def");
        quirkItemField = AccessTools.Field(quirkType, "item");
        quirkPawnTargetField = AccessTools.Field(quirkType, "pawnTarget");
        quirkConstructor = AccessTools.Constructor(
            quirkType,
            new[] { rewardDefType, typeof(ThingDef), typeof(Pawn) });
        grantedGeneLinkConstructor = AccessTools.Constructor(
            grantedGeneLinkType,
            new[] { typeof(Gene), quirkType });
        grantedGeneLinkGeneField = AccessTools.Field(grantedGeneLinkType, "gene");
        grantedGeneLinkQuirkField = AccessTools.Field(grantedGeneLinkType, "quirk");
        quirkLabelProperty = AccessTools.Property(quirkType, "LabelCap");
        quirkDescriptionProperty = AccessTools.Property(quirkType, "Description");
        rewardDefIsQuirkField = AccessTools.Field(rewardDefType, "isQuirk");
        rewardDefRequiresItemField = AccessTools.Field(rewardDefType, "requiresItem");
        rewardDefRequiresPawnField = AccessTools.Field(rewardDefType, "requiresPawn");
        rewardDefWorkerProperty = AccessTools.Property(rewardDefType, "Worker");
        rewardDefIconProperty = AccessTools.Property(rewardDefType, "Icon");
        rewardCanGenerateMethod = AccessTools.Method(rewardWorkerType, "CanGenerate", Type.EmptyTypes);
        rewardCanBestowMethod = AccessTools.Method(rewardWorkerType, "CanBestowOn",
            new[] { typeof(Pawn), typeof(ThingDef), typeof(Pawn) });
        rewardGetValidItemsMethod = AccessTools.Method(rewardWorkerType, "GetValidItems", new[] { typeof(Map) });
        rewardGetValidPawnsMethod = AccessTools.Method(rewardWorkerType, "GetValidPawns", new[] { typeof(Map) });
        rewardOnRemovedMethod = AccessTools.Method(rewardWorkerType, "OnRemoved", new[] { typeof(Pawn), quirkType });
        settingsField = AccessTools.Field(modType, "settings");
        rerollsPerWantField = AccessTools.Field(settingsType, "rerollsPerWant");
        maxActiveWantsField = AccessTools.Field(settingsType, "maxActiveWants");

        ready = utilityType != null
                && pawnWantsDataType != null
                && activeWantType != null
                && activeWantWithTargetType != null
                && activeWantWithPawnTargetType != null
                && wantDefType != null
                && wantWorkerType != null
                && quirkType != null
                && grantedGeneLinkType != null
                && rewardDefType != null
                && rewardWorkerType != null
                && getWantsDataMethod != null
                && tryGetWantsDataMethod != null
                && canHaveWantsMethod != null
                && initializePawnWantsMethod != null
                && addWantMethod != null
                && completeWantMethod != null
                && rerollWantMethod != null
                && addQuirkMethod != null
                && activeWantsField != null
                && quirksField != null
                && grantedGenesField != null
                && nextWantTickField != null
                && activeWantDefField != null
                && activeWantAssignedTickField != null
                && activeWantRerollCountField != null
                && activeWantTargetDefField != null
                && activeWantTargetPawnField != null
                && activeWantLabelProperty != null
                && activeWantDescriptionProperty != null
                && activeWantIconProperty != null
                && wantDefMentalBreakField != null
                && wantDefWorkerProperty != null
                && wantDefIconProperty != null
                && wantCanHaveMethod != null
                && wantCanGenerateMethod != null
                && quirkDefField != null
                && quirkItemField != null
                && quirkPawnTargetField != null
                && quirkConstructor != null
                && grantedGeneLinkConstructor != null
                && grantedGeneLinkGeneField != null
                && grantedGeneLinkQuirkField != null
                && quirkLabelProperty != null
                && quirkDescriptionProperty != null
                && rewardDefIsQuirkField != null
                && rewardDefRequiresItemField != null
                && rewardDefRequiresPawnField != null
                && rewardDefWorkerProperty != null
                && rewardDefIconProperty != null
                && rewardCanGenerateMethod != null
                && rewardCanBestowMethod != null
                && rewardGetValidItemsMethod != null
                && rewardGetValidPawnsMethod != null
                && rewardOnRemovedMethod != null
                && settingsField != null
                && rerollsPerWantField != null
                && maxActiveWantsField != null;

        if (!ready)
            Log.Error("[Pawn Editor] Character Development compatibility could not resolve its required API.");
    }

    public static bool CanEdit(Pawn pawn) =>
        Available && pawn != null && canHaveWantsMethod.Invoke(null, new object[] { pawn }) is true;

    public static bool HasQuirkTargetMap(Pawn pawn) => GetMap(pawn) != null;

    public static List<object> GetActiveWants(Pawn pawn)
    {
        var data = GetData(pawn);
        return data == null ? new List<object>() : ToObjectList(activeWantsField.GetValue(data));
    }

    public static List<object> GetQuirks(Pawn pawn)
    {
        var data = GetData(pawn);
        return data == null ? new List<object>() : ToObjectList(quirksField.GetValue(data));
    }

    public static List<Def> GetAvailableWantDefs(Pawn pawn)
    {
        if (!CanEdit(pawn))
            return new List<Def>();

        var activeWants = GetActiveWants(pawn);
        if (activeWants.Count >= GetMaximumActiveWants())
            return new List<Def>();

        var assigned = activeWants.Select(GetDef).Where(def => def != null).ToHashSet();
        return GetDefs(wantDefType)
            .Where(def => !assigned.Contains(def))
            .Where(def => !IsMentalBreakDef(def))
            .Where(def => WantCanBeAdded(pawn, def))
            .OrderBy(def => def.label ?? def.defName)
            .ToList();
    }

    public static List<Def> GetAvailableQuirkDefs(Pawn pawn)
    {
        if (!CanEdit(pawn))
            return new List<Def>();

        return GetDefs(rewardDefType)
            .Where(IsQuirkDef)
            .Where(CanGenerateQuirk)
            .Where(def => HasValidQuirkTarget(pawn, def))
            .OrderBy(def => PickerLabel(def))
            .ToList();
    }

    public static bool AddWant(Pawn pawn, Def wantDef)
    {
        var data = GetData(pawn);
        if (data == null
            || wantDef == null
            || IsMentalBreakDef(wantDef)
            || !WantCanBeAdded(pawn, wantDef)
            || activeWantsField.GetValue(data) is not IList list
            || list.Count >= GetMaximumActiveWants()
            || list.Cast<object>().Any(want => GetDef(want) == wantDef))
            return false;

        return addWantMethod.Invoke(null, new object[] { pawn, data, wantDef, null, false }) != null;
    }

    public static bool RemoveWant(Pawn pawn, object want)
    {
        var data = GetData(pawn);
        if (data == null || activeWantsField.GetValue(data) is not IList list || !list.Contains(want))
            return false;

        list.Remove(want);
        return true;
    }

    public static bool CompleteWant(Pawn pawn, object want)
    {
        var data = GetData(pawn);
        if (data == null
            || !TargetsAreValid(want)
            || activeWantsField.GetValue(data) is not IList list
            || !list.Contains(want))
            return false;

        completeWantMethod.Invoke(null, new[] { pawn, data, want });
        return true;
    }

    public static bool RerollWant(Pawn pawn, object want)
    {
        var data = GetData(pawn);
        if (data == null
            || rerollWantMethod == null
            || activeWantsField.GetValue(data) is not IList list
            || !list.Contains(want)
            || !CanRerollWant(want))
            return false;

        var index = list.IndexOf(want);
        rerollWantMethod.Invoke(null, new[] { pawn, data, want });
        return index >= 0 && index < list.Count && !ReferenceEquals(list[index], want);
    }

    public static bool CanRerollWant(object want)
    {
        if (want == null || IsMentalBreakWant(want) || activeWantRerollCountField == null)
            return false;

        var maximum = GetMaximumRerolls();
        return maximum > 0 && (int)activeWantRerollCountField.GetValue(want) < maximum;
    }

    public static int GetRerollsRemaining(object want)
    {
        if (want == null || activeWantRerollCountField == null)
            return 0;

        return Mathf.Max(0, GetMaximumRerolls() - (int)activeWantRerollCountField.GetValue(want));
    }

    public static bool AddQuirk(Pawn pawn, Def rewardDef, ThingDef item = null, Pawn targetPawn = null)
    {
        var data = GetData(pawn);
        if (data == null
            || rewardDef == null
            || !IsQuirkDef(rewardDef)
            || !CanGenerateQuirk(rewardDef)
            || RequiresItem(rewardDef) != (item != null)
            || RequiresPawn(rewardDef) != (targetPawn != null)
            || item != null && !GetWorkerItems(pawn, rewardDef).Contains(item)
            || targetPawn != null && !GetWorkerPawns(pawn, rewardDef).Contains(targetPawn)
            || !CanBestowQuirk(pawn, rewardDef, item, targetPawn))
            return false;

        addQuirkMethod.Invoke(null, new object[] { pawn, rewardDef, item, targetPawn });
        return true;
    }

    public static bool RemoveQuirk(Pawn pawn, object quirk)
    {
        var data = GetData(pawn);
        if (data == null || quirksField.GetValue(data) is not IList list || !list.Contains(quirk))
            return false;

        var rewardDef = GetDef(quirk);
        if (rewardDef == null)
            return false;
        var worker = rewardDefWorkerProperty.GetValue(rewardDef);
        rewardOnRemovedMethod.Invoke(worker, new[] { pawn, quirk });
        list.Remove(quirk);
        return true;
    }

    public static bool RequiresItem(Def rewardDef) =>
        rewardDef != null && rewardDefRequiresItemField.GetValue(rewardDef) is true;

    public static bool RequiresPawn(Def rewardDef) =>
        rewardDef != null && rewardDefRequiresPawnField.GetValue(rewardDef) is true;

    public static List<ThingDef> GetValidItems(Pawn pawn, Def rewardDef)
    {
        return GetWorkerItems(pawn, rewardDef)
            .Where(item => RequiresPawn(rewardDef) || CanBestowQuirk(pawn, rewardDef, item, null))
            .OrderBy(item => item.label ?? item.defName)
            .ToList();
    }

    public static List<Pawn> GetValidPawns(Pawn pawn, Def rewardDef, ThingDef item = null)
    {
        return GetWorkerPawns(pawn, rewardDef)
            .Where(target => CanBestowQuirk(pawn, rewardDef, item, target))
            .OrderBy(target => target.LabelShort)
            .ToList();
    }

    public static string GetLabel(object entry)
    {
        if (entry == null)
            return string.Empty;
        if (!TargetsAreValid(entry))
            return GetDef(entry)?.LabelCap.ToString() ?? string.Empty;
        if (activeWantType.IsInstanceOfType(entry))
            return activeWantLabelProperty?.GetValue(entry)?.ToString() ?? string.Empty;
        if (quirkType.IsInstanceOfType(entry))
            return quirkLabelProperty?.GetValue(entry)?.ToString() ?? string.Empty;
        return entry.ToString();
    }

    public static string GetDescription(object entry)
    {
        if (entry == null)
            return string.Empty;
        if (!TargetsAreValid(entry))
            return GetDef(entry)?.description ?? string.Empty;
        if (activeWantType.IsInstanceOfType(entry))
            return activeWantDescriptionProperty?.GetValue(entry)?.ToString() ?? string.Empty;
        if (quirkType.IsInstanceOfType(entry))
            return quirkDescriptionProperty?.GetValue(entry)?.ToString() ?? string.Empty;
        return string.Empty;
    }

    public static Texture GetIcon(object entry)
    {
        if (entry == null)
            return null;
        if (activeWantType.IsInstanceOfType(entry) && TargetsAreValid(entry))
            return activeWantIconProperty?.GetValue(entry) as Texture;

        var def = GetDef(entry);
        if (def == null)
            return null;
        if (quirkType.IsInstanceOfType(entry)
            && RequiresItem(def)
            && quirkItemField.GetValue(entry) is ThingDef item)
            return item.uiIcon;
        if (wantDefType.IsInstanceOfType(def))
            return wantDefIconProperty?.GetValue(def) as Texture;
        if (rewardDefType.IsInstanceOfType(def))
            return rewardDefIconProperty?.GetValue(def) as Texture;
        return null;
    }

    public static Texture GetDefIcon(Def def)
    {
        if (def == null)
            return null;
        if (wantDefType.IsInstanceOfType(def))
            return wantDefIconProperty?.GetValue(def) as Texture;
        if (rewardDefType.IsInstanceOfType(def))
            return rewardDefIconProperty?.GetValue(def) as Texture;
        return null;
    }

    public static bool IsMentalBreakWant(object want) =>
        want != null && IsMentalBreakDef(GetDef(want));

    public static string PickerLabel(Def def) =>
        (def?.LabelCap.ToString() ?? string.Empty).Replace("{0}", "...").CapitalizeFirst();

    public static void CopyData(Pawn source, Pawn destination)
    {
        if (!Available || source == null || destination == null)
            return;

        var sourceData = TryGetExistingData(source);
        if (sourceData == null)
            return;

        var copiedWants = CreateTypedList(activeWantType);
        foreach (var want in ToObjectList(activeWantsField.GetValue(sourceData)))
        {
            var copiedWant = CopyWant(want, source, destination);
            if (copiedWant != null)
                copiedWants.Add(copiedWant);
        }

        var copiedQuirks = CreateTypedList(quirkType);
        var copiedQuirksBySource = new Dictionary<object, object>();
        foreach (var quirk in ToObjectList(quirksField.GetValue(sourceData)))
        {
            var copiedQuirk = CopyQuirk(quirk, source, destination);
            if (copiedQuirk == null)
                continue;

            copiedQuirks.Add(copiedQuirk);
            copiedQuirksBySource[quirk] = copiedQuirk;
        }

        var copiedLinks = CreateTypedList(grantedGeneLinkType);
        foreach (var link in ToObjectList(grantedGenesField.GetValue(sourceData)))
        {
            var copiedLink = CopyGrantedGeneLink(link, source, destination, copiedQuirksBySource);
            if (copiedLink != null)
                copiedLinks.Add(copiedLink);
        }

        var destinationData = GetOrCreateData(destination);
        if (destinationData == null)
            return;

        activeWantsField.SetValue(destinationData, copiedWants);
        quirksField.SetValue(destinationData, copiedQuirks);
        grantedGenesField.SetValue(destinationData, copiedLinks);
        nextWantTickField.SetValue(destinationData, nextWantTickField.GetValue(sourceData));
    }

    private static object CopyWant(object sourceWant, Pawn source, Pawn destination)
    {
        var wantDef = activeWantDefField.GetValue(sourceWant) as Def;
        if (wantDef == null)
        {
            Log.Warning("[Pawn Editor] Character Development duplication skipped a want with a missing definition.");
            return null;
        }

        Type runtimeType;
        Def targetDef = null;
        Pawn targetPawn = null;
        if (activeWantWithPawnTargetType.IsInstanceOfType(sourceWant))
        {
            runtimeType = activeWantWithPawnTargetType;
            targetPawn = activeWantTargetPawnField.GetValue(sourceWant) as Pawn;
            if (targetPawn == null)
            {
                Log.Warning($"[Pawn Editor] Character Development duplication skipped pawn-targeted want '{wantDef.defName}' because its target was missing.");
                return null;
            }
            if (ReferenceEquals(targetPawn, source))
                targetPawn = destination;
        }
        else if (activeWantWithTargetType.IsInstanceOfType(sourceWant))
        {
            runtimeType = activeWantWithTargetType;
            targetDef = activeWantTargetDefField.GetValue(sourceWant) as Def;
            if (targetDef == null)
            {
                Log.Warning($"[Pawn Editor] Character Development duplication skipped def-targeted want '{wantDef.defName}' because its target was missing.");
                return null;
            }
        }
        else if (sourceWant.GetType() == activeWantType)
        {
            runtimeType = activeWantType;
        }
        else
        {
            Log.Warning($"[Pawn Editor] Character Development duplication skipped want '{wantDef.defName}' because its runtime type was unsupported.");
            return null;
        }

        var copiedWant = Activator.CreateInstance(runtimeType);
        activeWantDefField.SetValue(copiedWant, wantDef);
        activeWantAssignedTickField.SetValue(copiedWant, activeWantAssignedTickField.GetValue(sourceWant));
        activeWantRerollCountField.SetValue(copiedWant, activeWantRerollCountField.GetValue(sourceWant));
        if (targetDef != null)
            activeWantTargetDefField.SetValue(copiedWant, targetDef);
        if (targetPawn != null)
            activeWantTargetPawnField.SetValue(copiedWant, targetPawn);
        return copiedWant;
    }

    private static object CopyQuirk(object sourceQuirk, Pawn source, Pawn destination)
    {
        var rewardDef = quirkDefField.GetValue(sourceQuirk) as Def;
        if (rewardDef == null || !IsQuirkDef(rewardDef))
        {
            Log.Warning("[Pawn Editor] Character Development duplication skipped a quirk with a missing or invalid definition.");
            return null;
        }

        var item = quirkItemField.GetValue(sourceQuirk) as ThingDef;
        var targetPawn = quirkPawnTargetField.GetValue(sourceQuirk) as Pawn;
        if (RequiresItem(rewardDef) && item == null)
        {
            Log.Warning($"[Pawn Editor] Character Development duplication skipped quirk '{rewardDef.defName}' because its item target was missing.");
            return null;
        }
        if (RequiresPawn(rewardDef) && targetPawn == null)
        {
            Log.Warning($"[Pawn Editor] Character Development duplication skipped quirk '{rewardDef.defName}' because its pawn target was missing.");
            return null;
        }
        if (ReferenceEquals(targetPawn, source))
            targetPawn = destination;

        return quirkConstructor.Invoke(new object[] { rewardDef, item, targetPawn });
    }

    private static object CopyGrantedGeneLink(
        object sourceLink,
        Pawn source,
        Pawn destination,
        Dictionary<object, object> copiedQuirksBySource)
    {
        var sourceGene = grantedGeneLinkGeneField.GetValue(sourceLink) as Gene;
        var sourceQuirk = grantedGeneLinkQuirkField.GetValue(sourceLink);
        if (sourceGene?.def == null
            || sourceQuirk == null
            || !copiedQuirksBySource.TryGetValue(sourceQuirk, out var copiedQuirk))
        {
            Log.Warning("[Pawn Editor] Character Development duplication skipped an incomplete granted-gene link.");
            return null;
        }

        if (!TryGetGeneLocation(source, sourceGene, out var xenogene, out var ordinal))
        {
            Log.Warning($"[Pawn Editor] Character Development duplication skipped granted gene '{sourceGene.def.defName}' because it was not present on the source pawn.");
            return null;
        }

        var copiedGene = FindGene(destination, sourceGene.def, xenogene, ordinal);
        if (copiedGene == null)
        {
            Log.Warning($"[Pawn Editor] Character Development duplication skipped granted gene '{sourceGene.def.defName}' because the matching destination gene was missing.");
            return null;
        }

        return grantedGeneLinkConstructor.Invoke(new object[] { copiedGene, copiedQuirk });
    }

    private static object GetData(Pawn pawn)
    {
        if (!CanEdit(pawn))
            return null;

        var data = getWantsDataMethod.Invoke(null, new object[] { pawn });
        if (data != null && nextWantTickField.GetValue(data) is -1)
        {
            if (Current.Game == null || Find.TickManager == null)
                return null;
            initializePawnWantsMethod.Invoke(null, new[] { pawn, data });
        }
        return data;
    }

    private static object TryGetExistingData(Pawn pawn)
    {
        if (!Available || pawn == null)
            return null;

        var arguments = new object[] { pawn, null };
        return tryGetWantsDataMethod.Invoke(null, arguments) is true ? arguments[1] : null;
    }

    private static object GetOrCreateData(Pawn pawn) =>
        Available && pawn != null
            ? getWantsDataMethod.Invoke(null, new object[] { pawn })
            : null;

    private static Def GetDef(object entry)
    {
        if (entry == null)
            return null;
        if (activeWantType.IsInstanceOfType(entry))
            return activeWantDefField.GetValue(entry) as Def;
        if (quirkType.IsInstanceOfType(entry))
            return quirkDefField.GetValue(entry) as Def;
        return entry as Def;
    }

    private static bool WantCanBeAdded(Pawn pawn, Def wantDef)
    {
        if (wantDef == null || !wantDefType.IsInstanceOfType(wantDef))
            return false;

        var worker = wantDefWorkerProperty.GetValue(wantDef);
        return worker != null
               && wantCanHaveMethod.Invoke(worker, new object[] { pawn }) is true
               && wantCanGenerateMethod.Invoke(worker, new object[] { pawn }) is true;
    }

    private static bool IsMentalBreakDef(Def def) =>
        def != null && wantDefType.IsInstanceOfType(def) && wantDefMentalBreakField.GetValue(def) is true;

    private static bool IsQuirkDef(Def def) =>
        def != null && rewardDefType.IsInstanceOfType(def) && rewardDefIsQuirkField.GetValue(def) is true;

    private static bool CanGenerateQuirk(Def rewardDef)
    {
        var worker = rewardDef == null ? null : rewardDefWorkerProperty.GetValue(rewardDef);
        return worker != null && rewardCanGenerateMethod.Invoke(worker, Array.Empty<object>()) is true;
    }

    private static bool CanBestowQuirk(Pawn pawn, Def rewardDef, ThingDef item, Pawn targetPawn)
    {
        var worker = rewardDef == null ? null : rewardDefWorkerProperty.GetValue(rewardDef);
        return worker != null
               && rewardCanBestowMethod.Invoke(worker, new object[] { pawn, item, targetPawn }) is true;
    }

    private static bool HasValidQuirkTarget(Pawn pawn, Def rewardDef)
    {
        if (RequiresItem(rewardDef))
            return GetValidItems(pawn, rewardDef).Count > 0;
        if (RequiresPawn(rewardDef))
            return GetValidPawns(pawn, rewardDef).Count > 0;
        return CanBestowQuirk(pawn, rewardDef, null, null);
    }

    private static bool TargetsAreValid(object entry)
    {
        if (entry == null)
            return false;
        if ((activeWantType.IsInstanceOfType(entry) || quirkType.IsInstanceOfType(entry)) && GetDef(entry) == null)
            return false;
        if (activeWantWithTargetType.IsInstanceOfType(entry) && activeWantTargetDefField.GetValue(entry) == null)
            return false;
        if (activeWantWithPawnTargetType.IsInstanceOfType(entry) && activeWantTargetPawnField.GetValue(entry) == null)
            return false;
        if (!quirkType.IsInstanceOfType(entry))
            return true;

        var rewardDef = GetDef(entry);
        if (RequiresItem(rewardDef) && quirkItemField.GetValue(entry) == null)
            return false;
        return !RequiresPawn(rewardDef) || quirkPawnTargetField.GetValue(entry) != null;
    }

    private static int GetMaximumRerolls()
    {
        var settings = settingsField?.GetValue(null);
        return settings != null && rerollsPerWantField?.GetValue(settings) is int value ? value : 0;
    }

    private static int GetMaximumActiveWants()
    {
        var settings = settingsField?.GetValue(null);
        return settings != null && maxActiveWantsField?.GetValue(settings) is int value ? value : 0;
    }

    private static List<ThingDef> GetWorkerItems(Pawn pawn, Def rewardDef)
    {
        var map = GetMap(pawn);
        var worker = rewardDef == null ? null : rewardDefWorkerProperty.GetValue(rewardDef);
        if (map == null || worker == null)
            return new List<ThingDef>();

        return ToObjectList(rewardGetValidItemsMethod.Invoke(worker, new object[] { map }))
            .OfType<ThingDef>()
            .Distinct()
            .ToList();
    }

    private static List<Pawn> GetWorkerPawns(Pawn pawn, Def rewardDef)
    {
        var map = GetMap(pawn);
        var worker = rewardDef == null ? null : rewardDefWorkerProperty.GetValue(rewardDef);
        if (map == null || worker == null)
            return new List<Pawn>();

        return ToObjectList(rewardGetValidPawnsMethod.Invoke(worker, new object[] { map }))
            .OfType<Pawn>()
            .Distinct()
            .ToList();
    }

    private static Map GetMap(Pawn pawn)
    {
        if (pawn?.MapHeld != null)
            return pawn.MapHeld;
        return Current.Game == null ? null : Find.AnyPlayerHomeMap ?? Find.CurrentMap;
    }

    private static List<Def> GetDefs(Type defType) =>
        defType == null
            ? new List<Def>()
            : GenDefDatabase.GetAllDefsInDatabaseForDef(defType).OfType<Def>().ToList();

    private static bool TryGetGeneLocation(
        Pawn pawn,
        Gene gene,
        out bool xenogene,
        out int ordinal)
    {
        xenogene = true;
        if (TryGetGeneOrdinal(pawn?.genes?.Xenogenes, gene, out ordinal))
            return true;

        xenogene = false;
        return TryGetGeneOrdinal(pawn?.genes?.Endogenes, gene, out ordinal);
    }

    private static bool TryGetGeneOrdinal(
        IEnumerable<Gene> genes,
        Gene target,
        out int ordinal)
    {
        ordinal = 0;
        if (genes == null || target?.def == null)
            return false;

        foreach (var gene in genes)
        {
            if (ReferenceEquals(gene, target))
                return true;
            if (gene?.def == target.def)
                ordinal++;
        }

        ordinal = -1;
        return false;
    }

    private static Gene FindGene(Pawn pawn, GeneDef geneDef, bool xenogene, int ordinal)
    {
        if (pawn?.genes == null || geneDef == null || ordinal < 0)
            return null;

        var genes = xenogene ? pawn.genes.Xenogenes : pawn.genes.Endogenes;
        return genes.Where(gene => gene?.def == geneDef).Skip(ordinal).FirstOrDefault();
    }

    private static IList CreateTypedList(Type elementType) =>
        (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));

    private static List<object> ToObjectList(object value) =>
        value is IEnumerable enumerable ? enumerable.Cast<object>().Where(item => item != null).ToList() : new List<object>();
}
