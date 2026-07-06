using System;
using System.Linq;
using System.Xml;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Save: control / handling data. This is NOT part of a pawn's own body or story; it
/// describes how the colony manages the pawn, which is exactly what was missing from animal and
/// mech blueprints:
///
///   - Training     (animals): learned trainables, partial steps, and "wanted" toggles.
///   - Master       (animals): the assigned handler (playerSettings.Master).
///   - Mech control (mechs)  : overseer mechanitor + control group index and work mode.
///
/// All references to OTHER pawns are stored by ThingID + name so the loader can resolve them in a
/// second pass, the same way relations are, once every colony pawn exists. Every method is guarded
/// so a pawn without the relevant tracker (a normal colonist, say) simply writes nothing.
///
/// NOTE: modded mech upgrades (e.g. battery/power upgrade components from a mod) are NOT covered
/// here yet — that needs the specific mod's comp inspected first. Tracked separately.
/// </summary>
public static partial class PawnBlueprintSaveLoad
{
    // ── Save: Animal training ──

    private static void WriteTraining(XmlWriter w, Pawn pawn)
    {
        if (pawn.training == null) return;
        try
        {
            var trainables = DefDatabase<TrainableDef>.AllDefsListForReading;
            if (trainables == null || trainables.Count == 0) return;

            // Skip untouched animals: only emit if something was learned, partially trained, or wanted.
            if (!trainables.Any(td =>
                    pawn.training.HasLearned(td) || pawn.training.GetSteps(td) > 0 || pawn.training.GetWanted(td)))
                return;

            w.WriteStartElement("training");
            foreach (var td in trainables)
            {
                var learned = pawn.training.HasLearned(td);
                var steps   = pawn.training.GetSteps(td);
                var wanted  = pawn.training.GetWanted(td);
                if (!learned && steps <= 0 && !wanted) continue;

                w.WriteStartElement("li");
                WriteDefWithSource(w, "def", td);
                if (learned)   w.WriteElementString("learned", "true");
                if (steps > 0) w.WriteElementString("steps",   steps.ToString());
                if (wanted)    w.WriteElementString("wanted",  "true");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteTraining: {ex.Message}"); }
    }

    // ── Save: Assigned master / handler (animals) ──

    private static void WriteMaster(XmlWriter w, Pawn pawn)
    {
        try
        {
            var master = pawn.playerSettings?.Master;
            if (master == null || master == pawn) return;

            w.WriteStartElement("master");
            WritePawnRef(w, master);
            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteMaster: {ex.Message}"); }
    }

    // ── Save: Mech overseer + control group (Biotech) ──

    private static void WriteMechControl(XmlWriter w, Pawn pawn)
    {
        if (!ModsConfig.BiotechActive) return;
        if (pawn.RaceProps == null || !pawn.RaceProps.IsMechanoid) return;
        try
        {
            var overseer = pawn.GetOverseer();
            if (overseer == null) return;

            w.WriteStartElement("mechControl");
            WritePawnRef(w, overseer, "overseerID", "overseerFirst", "overseerNick", "overseerLast", "overseerName");

            var group = pawn.GetMechControlGroup();
            if (group != null)
            {
                w.WriteElementString("controlGroupIndex", group.Index.ToString());
                if (group.WorkMode != null)
                    WriteDefWithSource(w, "workMode", group.WorkMode);
            }
            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteMechControl: {ex.Message}"); }
    }

    // ── Save: Mech upgrades (Mechanoid Upgrades mod — custom comp, not hediffs) ──

    private static void WriteMechUpgrades(XmlWriter w, Pawn pawn)
    {
        if (!MechUpgradesCompat.Active) return;
        try
        {
            var upgrades = MechUpgradesCompat.GetInstalledUpgrades(pawn);
            if (upgrades.Count == 0) return;

            w.WriteStartElement("mechUpgrades");
            foreach (var up in upgrades)
            {
                if (up == null || up.DefName.NullOrEmpty()) continue;
                w.WriteStartElement("li");
                w.WriteAttributeString("defName", up.DefName);
                // Upgrade defs always come from a mod (never core), so tag the source for portability.
                if (!up.PackageId.NullOrEmpty())
                    w.WriteAttributeString("MayRequire", up.PackageId.ToLower());
                if (up.Charges.HasValue)
                    w.WriteAttributeString("charges", up.Charges.Value.ToString());
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteMechUpgrades: {ex.Message}"); }
    }

    // ── Save: Mechanitor status (Biotech) ──

    /// <summary>
    /// A mechanitor's capability comes from the mechlink implant hediff, which is hidden and marked
    /// duplicationAllowed=false — so WriteHediffs deliberately drops it, and a saved mechanitor would
    /// load as a normal colonist. Persist a simple mechanism-agnostic flag instead (via the vanilla
    /// IsMechanitor check); the load side re-grants the mechlink so the pawn stays a mechanitor.
    /// </summary>
    private static void WriteMechanitor(XmlWriter w, Pawn pawn)
    {
        if (!ModsConfig.BiotechActive) return;
        try
        {
            if (MechanitorUtility.IsMechanitor(pawn))
                w.WriteElementString("mechanitor", "true");
        }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] WriteMechanitor: {ex.Message}"); }
    }

    // ── Shared: reference to another pawn (ThingID + name) for second-pass resolution ──

    /// <summary>
    /// Write a reference to another pawn — ThingID plus name parts — under the given element names,
    /// so the loader can resolve it in the second pass the same way it resolves relations.
    /// </summary>
    private static void WritePawnRef(XmlWriter w, Pawn other,
        string idElem = "otherPawnID", string firstElem = "otherPawnFirst",
        string nickElem = "otherPawnNick", string lastElem = "otherPawnLast", string fullElem = "otherPawnName")
    {
        w.WriteElementString(idElem, other.ThingID ?? "");
        if (other.Name is NameTriple nt)
        {
            w.WriteElementString(firstElem, nt.First ?? "");
            w.WriteElementString(nickElem,  nt.Nick  ?? "");
            w.WriteElementString(lastElem,  nt.Last  ?? "");
        }
        else if (other.Name != null)
            w.WriteElementString(fullElem, other.Name.ToStringFull ?? "");
    }
}
