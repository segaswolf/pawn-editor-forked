using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PawnEditor;

[HotSwappable]
public static partial class SaveLoadUtility
{
    private static bool currentlyWorking;
    private static ILoadReferenceable currentItem;
    private static Pawn currentPawn;
    private static readonly HashSet<ILoadReferenceable> savedItems = new();
    private static readonly FieldInfo needJoyTolerancesField = AccessTools.Field(typeof(Need_Joy), "tolerances");
    private static readonly Type joyToleranceSetType = AccessTools.TypeByName("RimWorld.JoyToleranceSet");
    private static readonly HashSet<string> fallbackWarningsLogged = new();
    public static bool UseRandomFactionOnSave = false;

    public static MethodInfo ReferenceLook = AccessTools.FirstMethod(typeof(Scribe_References),
        mi => mi.Name == "Look" && mi.GetParameters().All(p => !p.ParameterType.Name.Contains("WeakReference")));

    public static string BaseSaveFolder => GenFilePaths.FolderUnderSaveData("PawnEditor");

    public static DirectoryInfo SaveFolderForItemType(string type)
    {
        var dir = new DirectoryInfo(Path.Combine(BaseSaveFolder, type.CapitalizeFirst()));
        var parent = dir.Parent;
        while (parent is { Exists: false })
        {
            parent.Create();
            parent = parent.Parent;
        }

        if (!dir.Exists) dir.Create();
        return dir;
    }

    public static string FilePathFor(string type, string name) => Path.Combine(BaseSaveFolder, type.CapitalizeFirst(), name + ".xml");

    public static int CountWithName(string type, string name) =>
        SaveFolderForItemType(type).GetFiles().Count(f => f.Extension == ".xml" && f.Name.StartsWith(name));

    public static void SaveItem<T>(T item, Action<T> callback = null, Pawn parentPawn = null, Action<T> prepare = null, string typePostfix = null)
        where T : IExposable
    {
        var type = typeof(T).Name;
        Find.WindowStack.Add(new Dialog_PawnEditorFiles_Save(typePostfix.NullOrEmpty() ? type : Path.Combine(type, typePostfix!), path =>
        {
            currentlyWorking = true;
            currentItem = item as ILoadReferenceable;
            currentPawn = parentPawn;
            savedItems.Clear();
            prepare?.Invoke(item);
            ApplyPatches();

            var tempFile = Path.GetTempFileName();
            Scribe.saver.InitSaving(tempFile, typePostfix.NullOrEmpty() ? type : type + "." + typePostfix);
            item.ExposeData();
            Scribe.saver.FinalizeSaving();
            File.Delete(tempFile);

            Scribe.saver.InitSaving(path, typePostfix.NullOrEmpty() ? type : type + "." + typePostfix);
            ScribeMetaHeaderUtility.WriteMetaHeader();
            item.ExposeData();
            Scribe.saver.FinalizeSaving();

            savedItems.Clear();
            currentItem = null;
            currentlyWorking = false;
            currentPawn = null;
            UnApplyPatches();

            if (item is Pawn pawn) PawnEditor.SavePawnTex(pawn, Path.ChangeExtension(path, ".png"), Rot4.South);

            //Overwrite saved faction with "Random" if setting is active
            if (item is Pawn)
            {
                if (UseRandomFactionOnSave)
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(path);
                    XmlNode factionNode = doc.DocumentElement["faction"];
                    factionNode.InnerText = "Random";
                    doc.Save(path);
                }
               
            }

            callback?.Invoke(item);
        },
        item switch
        {
            Pawn pawn => pawn.LabelShort,
            Map => "Colony",
            StartingThingsManager.StartingPreset => "Colony",
            Faction faction => faction.Name,
            Pawn_AbilityTracker abilities => abilities.pawn.LabelShort,
            Pawn_EquipmentTracker equipment => equipment.pawn.LabelShort,
            Pawn_ApparelTracker apparel => apparel.pawn.LabelShort,
            Pawn_InventoryTracker inventory => inventory.pawn.LabelShort,
            HediffSet hediffs => hediffs.pawn.LabelShort,
            ISaveable saveable => saveable.DefaultFileName(),
            _ => type
        }));
    }

    public static void LoadItem<T>(T item, Action<T> callback = null, Pawn parentPawn = null, Action<T> prepare = null, string typePostfix = null)
        where T : IExposable
    {
        var type = typeof(T).Name;
        Find.WindowStack.Add(new Dialog_PawnEditorFiles_Load(typePostfix.NullOrEmpty() ? type : Path.Combine(type, typePostfix!), path =>
        {
            //Setup loading with random faction
            string beforeSave = "";
            if (item is Pawn)
            {
                if (UseRandomFactionOnSave)
                {

                    XmlDocument doc = new XmlDocument();
                    doc.Load(path);
                    XmlNode factionNode = doc.DocumentElement["faction"];
                    beforeSave = factionNode.InnerText;
                    factionNode.InnerText = "Random";
                    doc.Save(path);
                }
               
            }


            currentlyWorking = true;
            currentItem = item as ILoadReferenceable;
            currentPawn = parentPawn;
            savedItems.Clear();
            loadInfo.Clear();
            fallbackWarningsLogged.Clear();
            var playing = false;
            if (Current.ProgramState == ProgramState.Playing)
            {
                Current.ProgramState = ProgramState.MapInitializing;
                playing = true;
            }

            prepare?.Invoke(item);
            ApplyPatches();
            Scribe.loader.InitLoading(path);
            ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.None, true);
            Scribe.loader.curParent = item;
            item.ExposeData();
            if (item is IExposable exposable)
            {
                Scribe.loader.crossRefs.crossReferencingExposables.Add(exposable);
                Scribe.loader.initer.saveablesToPostLoad.Add(exposable);
            }

            Scribe.loader.FinalizeLoading();

            if (item is Pawn loadedPawn)
                SanitizeLoadedPawn(loadedPawn);

            savedItems.Clear();
            loadInfo.Clear();
            currentlyWorking = false;
            currentItem = null;
            currentPawn = null;
            UnApplyPatches();
            callback?.Invoke(item);
            if (playing)
                Current.ProgramState = ProgramState.Playing;

            try
            {
                PawnEditor.Notify_PointsUsed();
            }
            catch (Exception ex)
            {
                Log.Error($"[Pawn Editor] Failed to refresh points after loading item '{type}'. {ex}");
            }

            //cleanup loading with random faction
            if (item is Pawn)
            {

                if (UseRandomFactionOnSave)
                {

                    XmlDocument doc = new XmlDocument();
                    doc.Load(path);
                    XmlNode factionNode = doc.DocumentElement["faction"];
                    factionNode.InnerText = beforeSave;
                    doc.Save(path);
                }

            }
        }));
    }

    private static void SanitizeLoadedPawn(Pawn pawn)
    {
        Pawn templatePawn = null;
        try
        {
            templatePawn = CreateFallbackTemplatePawn(pawn);
            SanitizeIdentityFromTemplate(pawn, templatePawn);
            SanitizeStoryFromTemplate(pawn, templatePawn);
            SanitizeSkillsFromTemplate(pawn, templatePawn);
            SanitizeTraits(pawn, templatePawn);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed base pawn fallback build for {pawn?.LabelCap ?? "<null>"}: {ex.Message}");
        }

        try
        {
            var needs = pawn?.needs?.AllNeeds;
            if (needs != null)
            {
                needs.RemoveAll(n => n == null);
                foreach (var need in needs)
                {
                    if (need is not Need_Joy joyNeed) continue;
                    if (needJoyTolerancesField == null || joyToleranceSetType == null) continue;
                    if (needJoyTolerancesField.GetValue(joyNeed) != null) continue;

                    object tolerances = null;
                    try
                    {
                        tolerances = Activator.CreateInstance(joyToleranceSetType, pawn);
                    }
                    catch
                    {
                        tolerances = Activator.CreateInstance(joyToleranceSetType);
                    }

                    if (tolerances != null)
                        needJoyTolerancesField.SetValue(joyNeed, tolerances);
                }

                pawn.needs.AddOrRemoveNeedsAsAppropriate();
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to sanitize loaded pawn needs for {pawn?.LabelCap ?? "<null>"}: {ex.Message}");
        }

        try
        {
            var relations = pawn?.relations?.DirectRelations;
            if (relations != null)
                relations.RemoveAll(r => r.otherPawn == null);
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] Failed to sanitize loaded pawn relations for {pawn?.LabelCap ?? "<null>"}: {ex.Message}");
        }

        try
        {
            pawn?.Notify_DisabledWorkTypesChanged();
            pawn?.drawer?.renderer?.SetAllGraphicsDirty();
        }
        catch
        {
        }
    }

    private static Pawn CreateFallbackTemplatePawn(Pawn pawn)
    {
        if (pawn == null) return null;
        var kind = pawn.kindDef ?? PawnKindDefOf.Colonist;
        var faction = pawn.Faction;
        try
        {
            var request = new PawnGenerationRequest(kind, faction)
            {
                ForceGenerateNewPawn = true,
                CanGeneratePawnRelations = false,
                ForceNoGear = true
            };
            return PawnGenerator.GeneratePawn(request);
        }
        catch
        {
            try
            {
                return PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, faction));
            }
            catch
            {
                return null;
            }
        }
    }

    private static void SanitizeIdentityFromTemplate(Pawn pawn, Pawn template)
    {
        if (pawn == null) return;
        if (pawn.kindDef == null)
        {
            pawn.kindDef = template?.kindDef ?? PawnKindDefOf.Colonist;
            LogFallback(pawn, "Missing pawn kind; assigned fallback kind");
        }

        if (pawn.Name == null)
        {
            pawn.Name = template?.Name ?? NameTriple.FromString($"{pawn.kindDef?.label ?? "Pawn"} {Rand.Range(100, 999)}");
            LogFallback(pawn, "Missing name; assigned fallback name");
        }
    }

    private static void SanitizeStoryFromTemplate(Pawn pawn, Pawn template)
    {
        if (pawn?.story == null || template?.story == null) return;

        if (pawn.story.Childhood == null && template.story.Childhood != null)
        {
            pawn.story.Childhood = template.story.Childhood;
            LogFallback(pawn, "Missing childhood backstory; assigned fallback");
        }

        if (pawn.DevelopmentalStage.Adult() && pawn.story.Adulthood == null && template.story.Adulthood != null)
        {
            pawn.story.Adulthood = template.story.Adulthood;
            LogFallback(pawn, "Missing adulthood backstory; assigned fallback");
        }
    }

    private static void SanitizeSkillsFromTemplate(Pawn pawn, Pawn template)
    {
        if (pawn?.skills == null) return;

        if (pawn.skills.skills == null)
            pawn.skills.skills = new List<SkillRecord>();

        if (pawn.skills.skills.Count == 0 && template?.skills?.skills != null)
        {
            foreach (var skill in template.skills.skills)
            {
                pawn.skills.skills.Add(new SkillRecord(pawn, skill.def)
                {
                    levelInt = skill.levelInt,
                    passion = skill.passion,
                    xpSinceLastLevel = skill.xpSinceLastLevel,
                    xpSinceMidnight = skill.xpSinceMidnight
                });
            }

            LogFallback(pawn, "Missing skill set; assigned fallback skills");
            return;
        }

        foreach (var skill in pawn.skills.skills)
        {
            if (skill == null) continue;
            if (skill.levelInt < 0) skill.levelInt = 0;
            else if (skill.levelInt > 20) skill.levelInt = 20;

            if (!Enum.IsDefined(typeof(Passion), skill.passion))
            {
                skill.passion = Passion.None;
                LogFallback(pawn, $"Invalid passion on skill {skill.def?.defName ?? "unknown"}; reset to None");
            }
        }
    }

    private static void SanitizeTraits(Pawn pawn, Pawn template)
    {
        if (pawn?.story?.traits?.allTraits == null) return;

        pawn.story.traits.allTraits.RemoveAll(t => t == null || t.def == null);
        if (pawn.story.traits.allTraits.Count == 0 && template?.story?.traits?.allTraits != null)
        {
            foreach (var trait in template.story.traits.allTraits)
            {
                if (trait?.def == null) continue;
                pawn.story.traits.GainTrait(new Trait(trait.def, trait.Degree, trait.ScenForced));
                break;
            }

            LogFallback(pawn, "Missing traits; assigned fallback trait");
        }
    }

    private static void LogFallback(Pawn pawn, string message)
    {
        var key = $"{pawn?.ThingID ?? "no-id"}:{message}";
        if (!fallbackWarningsLogged.Add(key)) return;
        Log.Warning($"[Pawn Editor] {message} for {pawn?.LabelCap ?? "<null>"}.");
    }

    private static void ApplyPatches()
    {
        var myType = typeof(SaveLoadUtility);
        PawnEditorMod.Harm.Patch(ReferenceLook.MakeGenericMethod(typeof(ILoadReferenceable)),
            new(myType, nameof(InterceptReferences)));
        PawnEditorMod.Harm.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.ExposeData)),
            transpiler: new(myType, nameof(FixFactionWeirdness)));
        PawnEditorMod.Harm.Patch(AccessTools.Method(typeof(DebugLoadIDsSavingErrorsChecker), nameof(DebugLoadIDsSavingErrorsChecker.RegisterDeepSaved)),
            postfix: new(myType, nameof(Notify_DeepSaved)));
        PawnEditorMod.Harm.Patch(AccessTools.Method(typeof(PostLoadIniter), nameof(PostLoadIniter.RegisterForPostLoadInit)),
            postfix: new(myType, nameof(Notify_DeepSaved)));
        PawnEditorMod.Harm.Patch(AccessTools.Method(typeof(Scribe_Values), nameof(Scribe_Values.Look), generics: new[] { typeof(int) }),
            new(myType, nameof(ReassignLoadID)));
        PawnEditorMod.Harm.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.ExposeData)),
            new(myType, nameof(AssignCurrentPawn)), new(myType, nameof(ClearCurrentPawn)));
        PawnEditorMod.Harm.Patch(AccessTools.Method(typeof(LoadIDsWantedBank), nameof(LoadIDsWantedBank.RegisterLoadIDListReadFromXml),
            new[] { typeof(List<string>), typeof(string), typeof(IExposable) }), new(myType, nameof(InterceptIDList)));
    }

    private static void UnApplyPatches()
    {
        var myType = typeof(SaveLoadUtility);
        PawnEditorMod.Harm.Unpatch(ReferenceLook.MakeGenericMethod(typeof(ILoadReferenceable)),
            AccessTools.Method(myType, nameof(InterceptReferences)));
        PawnEditorMod.Harm.Unpatch(AccessTools.Method(typeof(Thing), nameof(Thing.ExposeData)),
            AccessTools.Method(myType, nameof(FixFactionWeirdness)));
        PawnEditorMod.Harm.Unpatch(AccessTools.Method(typeof(DebugLoadIDsSavingErrorsChecker), nameof(DebugLoadIDsSavingErrorsChecker.RegisterDeepSaved)),
            AccessTools.Method(myType, nameof(Notify_DeepSaved)));
        PawnEditorMod.Harm.Unpatch(AccessTools.Method(typeof(Scribe_Values), nameof(Scribe_Values.Look), generics: new[] { typeof(int) }),
            AccessTools.Method(myType, nameof(ReassignLoadID)));
        PawnEditorMod.Harm.Unpatch(AccessTools.Method(typeof(Pawn), nameof(Pawn.ExposeData)), HarmonyPatchType.All, PawnEditorMod.Harm.Id);
        PawnEditorMod.Harm.Unpatch(AccessTools.Method(typeof(LoadIDsWantedBank), nameof(LoadIDsWantedBank.RegisterLoadIDListReadFromXml),
            new[] { typeof(List<string>), typeof(string), typeof(IExposable) }), AccessTools.Method(myType, nameof(InterceptIDList)));
    }
}

public interface ISaveable
{
    string DefaultFileName();
}
