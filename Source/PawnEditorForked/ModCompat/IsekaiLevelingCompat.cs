using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using JetBrains.Annotations;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Isekai RPG Leveling: level, XP, the six stats and the constellation live in a ThingComp
/// (<c>IsekaiLeveling.IsekaiComponent</c>). Writes go through the mod's own forge API
/// (<c>IsekaiCharacterForge.Apply</c>), which keeps the rank trait and build titles in sync with the level.
/// </summary>
/// <remarks>
/// Checked against the Workshop source (3657580708, v1.1.8.3, 19 Oct 2026). Without this, duplicating
/// or loading a blueprint keeps the stats the generator rolled for the new pawn.
/// The forge's own Capture skips unspent points, so state is read from the component directly.
/// </remarks>
[ModCompat("JellyCreative.IsekaiLeveling")]
public static class IsekaiLevelingCompat
{
    public static bool Active;
    public static string Name = "Isekai RPG Leveling";

    private const string RankTraitPrefix = "Isekai_Rank_";
    private const string TitleTraitPrefix = "Isekai_Title_";
    private const string BlueprintNode = "isekai";
    private const int BaseStatValue = 5;
    private static readonly string[] StatNames = { "strength", "dexterity", "vitality", "intelligence", "wisdom", "charisma" };

    private static Type compType;
    private static Type specType;
    private static ConstructorInfo windowConstructor;
    private static MethodInfo applyMethod;

    private static FieldInfo levelField, xpField, lockedField, statsField, treeField;
    private static FieldInfo statPointsField, treeNameField, treePointsField, starFragmentsField, nodesField;
    private static FieldInfo specLevelField, specXpField, specTreeField, specNodesField, specSkipTitlesField;
    private static FieldInfo[] statFields;
    private static FieldInfo[] specStatFields;
    private static bool ready;

    public static bool Available => Active && ready;

    /// <summary>The Isekai layer of one pawn, as plain values.</summary>
    public sealed class State
    {
        public int Level = 1;
        public int Xp;
        public readonly int[] Stats = { BaseStatValue, BaseStatValue, BaseStatValue, BaseStatValue, BaseStatValue, BaseStatValue };
        public int StatPoints;
        public string Tree;
        public readonly List<string> Nodes = new();
        public int TreePoints;
        public int StarFragments;
        public bool Locked;
    }

    [UsedImplicitly]
    public static void Activate()
    {
        compType = AccessTools.TypeByName("IsekaiLeveling.IsekaiComponent");
        var statsType = AccessTools.TypeByName("IsekaiLeveling.IsekaiStatAllocation");
        var treeType = AccessTools.TypeByName("IsekaiLeveling.SkillTree.PassiveTreeTracker");
        var forgeType = AccessTools.TypeByName("IsekaiLeveling.CharacterForge.IsekaiCharacterForge");
        specType = AccessTools.TypeByName("IsekaiLeveling.CharacterForge.IsekaiCharacterSpec");
        var windowType = AccessTools.TypeByName("IsekaiLeveling.CharacterForge.Window_CharacterForge");

        if (compType == null || statsType == null || treeType == null || forgeType == null || specType == null)
        {
            Log.Error("[Pawn Editor] Isekai RPG Leveling compatibility could not resolve its types.");
            return;
        }

        applyMethod = AccessTools.Method(forgeType, "Apply", new[] { typeof(Pawn), specType, typeof(bool) });
        windowConstructor = windowType == null ? null : AccessTools.Constructor(windowType, new[] { typeof(Pawn) });

        levelField = AccessTools.Field(compType, "currentLevel");
        xpField = AccessTools.Field(compType, "currentXP");
        lockedField = AccessTools.Field(compType, "forgeLocked");
        statsField = AccessTools.Field(compType, "stats");
        treeField = AccessTools.Field(compType, "passiveTree");
        statPointsField = AccessTools.Field(statsType, "availableStatPoints");
        treeNameField = AccessTools.Field(treeType, "assignedTree");
        treePointsField = AccessTools.Field(treeType, "availablePoints");
        starFragmentsField = AccessTools.Field(treeType, "starFragmentsAbsorbed");
        nodesField = AccessTools.Field(treeType, "unlockedNodeIds"); // internal
        specLevelField = AccessTools.Field(specType, "level");
        specXpField = AccessTools.Field(specType, "xp");
        specTreeField = AccessTools.Field(specType, "constellation");
        specNodesField = AccessTools.Field(specType, "constellationNodes");
        specSkipTitlesField = AccessTools.Field(specType, "skipTitleRefresh");
        statFields = StatNames.Select(name => AccessTools.Field(statsType, name)).ToArray();
        specStatFields = StatNames.Select(name => AccessTools.Field(specType, name)).ToArray();

        var fields = new[]
        {
            levelField, xpField, lockedField, statsField, treeField, statPointsField, treeNameField, treePointsField,
            starFragmentsField, nodesField, specLevelField, specXpField, specTreeField, specNodesField, specSkipTitlesField
        };
        ready = applyMethod != null && fields.All(f => f != null)
                                    && statFields.All(f => f != null) && specStatFields.All(f => f != null);

        if (!ready)
            Log.Error("[Pawn Editor] Isekai RPG Leveling compatibility could not resolve its required API.");
    }

    /// <summary>Rank and build-title traits: the mod derives them from the level and stats.</summary>
    public static bool IsSystemTrait(TraitDef def) =>
        Active && def?.defName != null
               && (def.defName.StartsWith(RankTraitPrefix, StringComparison.Ordinal)
                   || def.defName.StartsWith(TitleTraitPrefix, StringComparison.Ordinal));

    public static bool CanEdit(Pawn pawn) => Available && windowConstructor != null && GetComp(pawn) != null;

    /// <summary>Opens the mod's own editor. It stages edits and applies them on its Apply button.</summary>
    public static void OpenEditor(Pawn pawn)
    {
        if (!CanEdit(pawn)) return;
        Diagnostics.Run("Opening the Isekai editor", pawn,
            () => Find.WindowStack.Add((Window)windowConstructor.Invoke(new object[] { pawn })));
    }

    public static State Capture(Pawn pawn)
    {
        var comp = Available ? GetComp(pawn) : null;
        if (comp == null) return null;

        var stats = statsField.GetValue(comp);
        var tree = treeField.GetValue(comp);
        if (stats == null || tree == null) return null;

        var state = new State
        {
            Level = (int)levelField.GetValue(comp),
            Xp = (int)xpField.GetValue(comp),
            Locked = (bool)lockedField.GetValue(comp),
            StatPoints = (int)statPointsField.GetValue(stats),
            Tree = treeNameField.GetValue(tree) as string,
            TreePoints = (int)treePointsField.GetValue(tree),
            StarFragments = (int)starFragmentsField.GetValue(tree)
        };
        for (var i = 0; i < StatNames.Length; i++)
            state.Stats[i] = (int)statFields[i].GetValue(stats);
        if (nodesField.GetValue(tree) is List<string> nodes)
            state.Nodes.AddRange(nodes);
        return state;
    }

    public static void Apply(Pawn pawn, State state)
    {
        var comp = Available && state != null ? GetComp(pawn) : null;
        if (comp == null) return;

        var spec = Activator.CreateInstance(specType);
        for (var i = 0; i < StatNames.Length; i++)
            specStatFields[i].SetValue(spec, state.Stats[i]);
        specLevelField.SetValue(spec, state.Level);
        specXpField.SetValue(spec, state.Xp);

        // "" clears the tree. With a tree, an exact node list (even empty) stops the forge auto-spending points.
        var hasTree = !state.Tree.NullOrEmpty();
        specTreeField.SetValue(spec, hasTree ? state.Tree : "");
        if (hasTree) specNodesField.SetValue(spec, new List<string>(state.Nodes));
        // A copy, not a re-derivation: title traits travel with the other traits. Isekai refreshes them
        // on its own tick; refreshing here gave a loaded pawn a title its source didn't have yet.
        specSkipTitlesField.SetValue(spec, true);

        applyMethod.Invoke(null, new[] { pawn, spec, (object)state.Locked });

        // Not covered by the spec: unspent points, and unlocking a pawn that was locked.
        statPointsField.SetValue(statsField.GetValue(comp), state.StatPoints);
        var tree = treeField.GetValue(comp);
        if (tree != null)
        {
            treePointsField.SetValue(tree, state.TreePoints);
            starFragmentsField.SetValue(tree, state.StarFragments);
        }
        lockedField.SetValue(comp, state.Locked);
    }

    public static void CopyData(Pawn source, Pawn destination)
    {
        if (!Available || source == null || destination == null) return;
        Diagnostics.Run("Copying Isekai stats", destination, () => Apply(destination, Capture(source)));
    }

    public static void WriteBlueprint(XmlWriter writer, Pawn pawn)
    {
        var state = Diagnostics.Run("Saving Isekai stats", pawn, () => Capture(pawn));
        if (state == null) return;

        writer.WriteStartElement(BlueprintNode);
        WriteInt(writer, "level", state.Level);
        WriteInt(writer, "xp", state.Xp);
        for (var i = 0; i < StatNames.Length; i++)
            WriteInt(writer, StatNames[i], state.Stats[i]);
        WriteInt(writer, "statPoints", state.StatPoints);
        if (!state.Tree.NullOrEmpty()) writer.WriteAttributeString("tree", state.Tree);
        WriteInt(writer, "treePoints", state.TreePoints);
        WriteInt(writer, "starFragments", state.StarFragments);
        if (state.Locked) writer.WriteAttributeString("locked", "true");
        foreach (var node in state.Nodes)
            writer.WriteElementString("node", node);
        writer.WriteEndElement();
    }

    /// <summary>Blueprints saved without the mod have no node; the pawn keeps what the generator rolled.</summary>
    public static void LoadBlueprint(Pawn pawn, XmlNode root)
    {
        if (!Available) return;
        var node = root?.SelectSingleNode(BlueprintNode);
        if (node == null) return;

        var state = new State
        {
            Level = ReadInt(node, "level", 1),
            Xp = ReadInt(node, "xp", 0),
            StatPoints = ReadInt(node, "statPoints", 0),
            Tree = node.Attributes?["tree"]?.Value,
            TreePoints = ReadInt(node, "treePoints", 0),
            StarFragments = ReadInt(node, "starFragments", 0),
            Locked = node.Attributes?["locked"]?.Value == "true"
        };
        for (var i = 0; i < StatNames.Length; i++)
            state.Stats[i] = ReadInt(node, StatNames[i], BaseStatValue);
        foreach (XmlNode child in node.SelectNodes("node"))
        {
            var id = child.InnerText?.Trim();
            if (!id.NullOrEmpty()) state.Nodes.Add(id);
        }

        Diagnostics.Run("Loading Isekai stats", pawn, () => Apply(pawn, state));
    }

    private static void WriteInt(XmlWriter writer, string name, int value) =>
        writer.WriteAttributeString(name, value.ToString(CultureInfo.InvariantCulture));

    private static int ReadInt(XmlNode node, string name, int fallback) =>
        int.TryParse(node.Attributes?[name]?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static ThingComp GetComp(Pawn pawn)
    {
        if (pawn?.AllComps == null || compType == null) return null;
        foreach (var comp in pawn.AllComps)
        {
            if (compType.IsInstanceOfType(comp))
                return comp;
        }
        return null;
    }
}
