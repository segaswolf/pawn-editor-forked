using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace PawnEditor;

/// <summary>
/// Compatibility layer for Mechanoid Upgrades (gogatio.mechanoidupgrades, mod namespace "MU").
///
/// Mech upgrades are NOT hediffs — the mod keeps them in a custom comp on the mech:
///   MU.CompUpgradableMechanoid.upgrades : List&lt;MU.MechUpgrade&gt;
/// and each MU.MechUpgrade carries a MechUpgradeDef plus an optional ability charge count.
/// Because of that, the blueprint's hediff section never captured them. This wrapper reads and
/// re-installs them via reflection, so there is no hard dependency on the mod assembly (the mod
/// may be absent when a blueprint is loaded).
///
/// Scope: which upgrades are installed (defName) + ability charges. The fine reload state of
/// battery/ammo style upgrades (MechUpgradeWithComps -> UpgradeCompReloadable) is intentionally
/// out of scope for now.
/// </summary>
[ModCompat("gogatio.mechanoidupgrades")]
public static class MechUpgradesCompat
{
    public static bool Active;
    public static string Name = "Mechanoid Upgrades";

    private static Type compType;              // MU.CompUpgradableMechanoid
    private static FieldInfo upgradesField;    // CompUpgradableMechanoid.upgrades (List<MechUpgrade>)
    private static FieldInfo upgradeDefField;  // MechUpgrade.def (MechUpgradeDef)
    private static FieldInfo upgradeChargesField; // MechUpgrade.charges (int?)
    private static Type upgradeDefType;        // MU.MechUpgradeDef
    private static MethodInfo addUpgradeMethod; // CompUpgradableMechanoid.AddUpgrade(MechUpgradeDef)

    /// <summary>Resolve all reflection handles. Called once by the compat bootstrap when the mod is active.</summary>
    public static void Activate()
    {
        compType     = AccessTools.TypeByName("MU.CompUpgradableMechanoid");
        upgradesField = AccessTools.Field(compType, "upgrades");

        var upgradeType = AccessTools.TypeByName("MU.MechUpgrade");
        upgradeDefField     = AccessTools.Field(upgradeType, "def");
        upgradeChargesField = AccessTools.Field(upgradeType, "charges");

        upgradeDefType = AccessTools.TypeByName("MU.MechUpgradeDef");

        // AddUpgrade is overloaded (MechUpgradeDef / MechUpgrade); pick the def-based one.
        if (upgradeDefType != null)
            addUpgradeMethod = AccessTools.Method(compType, "AddUpgrade", new[] { upgradeDefType });
    }

    // ── Read (save side) ──

    /// <summary>Snapshot of one installed upgrade for serialization.</summary>
    public class UpgradeSnapshot
    {
        public string DefName;
        public string PackageId; // source mod, for MayRequire portability
        public int? Charges;     // ability charges, if the upgrade grants a reloadable ability
    }

    /// <summary>
    /// Read the upgrades installed on a mech: defName, source mod, and ability charges. Returns an
    /// empty list for a pawn without the comp (any non-mech, or a mech with no upgrades).
    /// </summary>
    public static List<UpgradeSnapshot> GetInstalledUpgrades(Pawn mech)
    {
        var result = new List<UpgradeSnapshot>();
        var comp = GetComp(mech);
        if (comp == null) return result;
        try
        {
            if (!(upgradesField?.GetValue(comp) is IList list)) return result;
            foreach (var upgrade in list)
            {
                if (upgrade == null) continue;
                if (!(upgradeDefField?.GetValue(upgrade) is Def def)) continue;
                var charges = upgradeChargesField?.GetValue(upgrade) as int?;
                result.Add(new UpgradeSnapshot
                {
                    DefName   = def.defName,
                    PackageId = def.modContentPack?.PackageId,
                    Charges   = charges
                });
            }
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] MU GetInstalledUpgrades: {ex.Message}"); }
        return result;
    }

    // ── Write (load side — used by CAPA 2) ──

    /// <summary>
    /// Install an upgrade on a mech by defName, restoring its ability charges if given. Resolves the
    /// MechUpgradeDef by name; skips silently (with a warning) if the def isn't loaded.
    /// </summary>
    public static bool AddUpgrade(Pawn mech, string defName, int? charges)
    {
        var comp = GetComp(mech);
        if (comp == null || addUpgradeMethod == null || upgradeDefType == null) return false;
        try
        {
            var def = GenDefDatabase.GetDefSilentFail(upgradeDefType, defName, false);
            if (def == null)
            {
                Log.Warning($"[Pawn Editor] MU upgrade '{defName}' not found, skipping");
                return false;
            }

            var upgrade = addUpgradeMethod.Invoke(comp, new[] { def });
            if (upgrade != null && charges.HasValue)
                upgradeChargesField?.SetValue(upgrade, charges);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[Pawn Editor] MU AddUpgrade '{defName}': {ex.Message}");
            return false;
        }
    }

    // ── Shared ──

    /// <summary>Find the MU.CompUpgradableMechanoid on a pawn, or null.</summary>
    private static object GetComp(Pawn mech)
    {
        if (!Active || compType == null || mech?.AllComps == null) return null;
        try
        {
            foreach (var comp in mech.AllComps)
                if (compType.IsInstanceOfType(comp))
                    return comp;
        }
        catch { }
        return null;
    }
}
