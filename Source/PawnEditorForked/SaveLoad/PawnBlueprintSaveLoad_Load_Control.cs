using System;
using System.Linq;
using System.Xml;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Load: control / handling data (mirror of _Save_Control.cs).
///   LoadMechanitor   — re-grant the mechlink if the pawn was a mechanitor.
///   LoadTraining     — restore learned trainables + wanted toggles (animals).
///   LoadMaster       — restore the assigned handler (animals).
///   LoadMechControl  — restore the mech's overseer link (resolve only for now, see note).
///   LoadMechUpgrades — reinstall Mechanoid Upgrades upgrades.
///
/// SAFEGUARD (agreed design): every pawn reference (master, overseer, ...) is resolved against the
/// pawns that currently exist. If the referenced pawn isn't present — the pawn was saved alone, or
/// its partner lives on another map — the link is skipped with a warning and the pawn still loads
/// cleanly. Never a dangling pointer. A mech whose overseer can't be resolved loads as a valid,
/// unowned player mech (same end state as a mech whose mechanitor died), not a broken binding.
/// </summary>
public static partial class PawnBlueprintSaveLoad
{
    // ── Load: Mechanitor status ──

    private static void LoadMechanitor(Pawn pawn, XmlNode root)
    {
        if (!ModsConfig.BiotechActive) return;
        if (!ParseBool(GetText(root, "mechanitor"), false)) return;
        try
        {
            if (MechanitorUtility.IsMechanitor(pawn)) return; // already a mechanitor

            // The mechlink implant is what IsMechanitor checks for; re-grant it so the pawn keeps
            // its mechanitor capability. (Save drops it via the duplicationAllowed filter — see
            // WriteMechanitor / the <mechanitor> flag.)
            var mechlink = DefDatabase<HediffDef>.GetNamedSilentFail("MechlinkImplant");
            if (mechlink == null) { Warn("Mechanitor: MechlinkImplant hediff not found"); return; }

            var brain = pawn.health?.hediffSet?.GetBrain();
            pawn.health.AddHediff(mechlink, brain);
        }
        catch (Exception ex) { Warn($"Mechanitor: {ex.Message}"); }
    }

    // ── Load: Animal training ──

    private static void LoadTraining(Pawn pawn, XmlNode root)
    {
        if (pawn.training == null) return;
        var node = root.SelectSingleNode("training");
        if (node == null) return;
        try
        {
            foreach (XmlNode li in node.SelectNodes("li"))
            {
                var td = ResolveDef<TrainableDef>(li, "def");
                if (td == null) continue;

                var learned = ParseBool(GetText(li, "learned"), false);
                var wanted  = ParseBool(GetText(li, "wanted"),  false);
                try
                {
                    if (wanted) pawn.training.SetWantedRecursive(td, true);
                    if (learned && !pawn.training.HasLearned(td))
                        pawn.training.Train(td, null, complete: true);
                }
                catch (Exception ex) { Warn($"Training '{td.defName}': {ex.Message}"); }
            }
        }
        catch (Exception ex) { Warn($"Training: {ex.Message}"); }
    }

    // ── Load: Assigned master / handler (animals) ──

    private static void LoadMaster(Pawn pawn, XmlNode root)
    {
        if (pawn.playerSettings == null) return;
        var node = root.SelectSingleNode("master");
        if (node == null) return;
        try
        {
            var master = ResolveSavedPawn(node, pawn);
            if (master == null) { Warn("Master: assigned handler not present, left unassigned"); return; }
            pawn.playerSettings.Master = master;
        }
        catch (Exception ex) { Warn($"Master: {ex.Message}"); }
    }

    // ── Load: Mech overseer + control group ──

    private static void LoadMechControl(Pawn pawn, XmlNode root)
    {
        if (!ModsConfig.BiotechActive) return;
        if (pawn.RaceProps == null || !pawn.RaceProps.IsMechanoid) return;
        var node = root.SelectSingleNode("mechControl");
        if (node == null) return;
        try
        {
            var overseer = ResolveSavedPawn(node, pawn,
                "overseerID", "overseerFirst", "overseerNick", "overseerLast", "overseerName");
            if (overseer == null || overseer.mechanitor == null)
            {
                Warn("MechControl: overseer not present, mech left unowned");
                return;
            }

            // The overseer link is the vanilla Overseer DirectRelation (mechanitor -> mech), plus a
            // control-group assignment. Establish both, matching how the game binds a mech.
            if (!overseer.relations.DirectRelationExists(PawnRelationDefOf.Overseer, pawn))
                overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, pawn);

            var workMode = ResolveDef<MechWorkModeDef>(node, "workMode");
            overseer.mechanitor.AssignPawnControlGroup(pawn, workMode);
        }
        catch (Exception ex) { Warn($"MechControl: {ex.Message}"); }
    }

    // ── Load: Mech upgrades (Mechanoid Upgrades) ──

    private static void LoadMechUpgrades(Pawn pawn, XmlNode root)
    {
        if (!MechUpgradesCompat.Active) return;
        var node = root.SelectSingleNode("mechUpgrades");
        if (node == null) return;
        try
        {
            foreach (XmlNode li in node.SelectNodes("li"))
            {
                if (!IsAvailable(li)) continue;
                var defName = li.Attributes?["defName"]?.Value;
                if (defName.NullOrEmpty()) continue;

                int? charges = null;
                var chStr = li.Attributes?["charges"]?.Value;
                if (!chStr.NullOrEmpty() && int.TryParse(chStr, out var c)) charges = c;

                MechUpgradesCompat.AddUpgrade(pawn, defName, charges);
            }
        }
        catch (Exception ex) { Warn($"MechUpgrades: {ex.Message}"); }
    }

    // ── Shared: resolve a saved pawn reference against existing pawns ──

    /// <summary>
    /// Find the pawn a saved reference points to (ThingID first, then NameTriple, then full name),
    /// among all reachable pawns. Returns null if not present — callers must handle that as "skip
    /// the link, load the pawn anyway" (the safeguard). Mirrors LoadRelations' resolution.
    /// </summary>
    private static Pawn ResolveSavedPawn(XmlNode node, Pawn self,
        string idElem = "otherPawnID", string firstElem = "otherPawnFirst",
        string nickElem = "otherPawnNick", string lastElem = "otherPawnLast", string fullElem = "otherPawnName")
    {
        return ResolvePawnRef(GetAllReachablePawns(), self,
            GetText(node, idElem), GetText(node, firstElem), GetText(node, lastElem), GetText(node, fullElem));
    }
}
