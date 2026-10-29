using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Equips a pawn and makes sure the item ends up somewhere the player can see it.
/// </summary>
/// <remarks>
/// <para>
/// Other mods can veto equipping. Progression: Education, for one, postfixes
/// <c>Pawn_EquipmentTracker.AddEquipment</c>: when the pawn lacks the proficiency it removes the item
/// again, drops it on the ground if the pawn is on a map, and otherwise leaves it nowhere. From the
/// editor that looked like the weapon vanishing (on the character creation screen it really was lost).
/// </para>
/// <para>
/// Their rule is respected: a refused item is never forced into the hands. It goes into the pawn's
/// possessions instead, and the player is told. Every place the editor equips something goes through
/// here, so the next mod with a rule like this is handled everywhere at once.
/// </para>
/// </remarks>
public static class EquipmentPlacement
{
    public enum Outcome
    {
        Equipped,
        KeptAsPossession,
        Lost
    }

    public static Outcome Equip(Pawn pawn, ThingWithComps item)
    {
        if (pawn?.equipment == null || item == null) return Outcome.Lost;

        pawn.equipment.AddEquipment(item);
        if (pawn.equipment.Contains(item)) return Outcome.Equipped;

        // Refused by another mod. Take it back from wherever it was put and keep it with the pawn.
        if (item.Spawned) item.DeSpawn();
        if (!item.Destroyed && item.holdingOwner == null && pawn.inventory != null
            && pawn.inventory.innerContainer.TryAdd(item, canMergeWithExistingStacks: false))
        {
            Messages.Message("PawnEditor.EquipRefusedKeptAsPossession".Translate(pawn.LabelShortCap, item.LabelCap),
                pawn, MessageTypeDefOf.CautionInput, false);
            return Outcome.KeptAsPossession;
        }

        Log.Warning($"[Pawn Editor] {pawn.LabelShortCap} could not equip {item.LabelCap} (another mod refused it) "
                    + "and it could not be kept as a possession either.");
        return Outcome.Lost;
    }
}
