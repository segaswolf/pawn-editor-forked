using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

public static partial class PawnEditor
{
    private static Pawn clipboard;

    public static bool CanPaste => clipboard != null;

    public static void Copy(Pawn pawn)
    {
        clipboard = pawn;
    }

    public static void ClearClipboard()
    {
        clipboard = null;
    }

    public static void Paste(Pawn into)
    {
        if (into.apparel != null && clipboard?.apparel != null)
        {
            into.apparel.DestroyAll();
            foreach (var apparel in clipboard.apparel.WornApparel) into.apparel.Wear(apparel.Clone(), false, clipboard.apparel.IsLocked(apparel));
        }

        if (into.equipment != null && clipboard?.equipment != null)
        {
            into.equipment.DestroyAllEquipment();
            foreach (var eq in clipboard.equipment.AllEquipmentListForReading) into.equipment.AddEquipment(eq.Clone());
        }

        if (into.inventory != null && clipboard?.inventory != null)
        {
            into.inventory.DestroyAll();
            // FIX #008: Snapshot list to avoid collection-modified errors.
            var items = clipboard.inventory.innerContainer.ToList();
            foreach (var thing in items) into.inventory.innerContainer.TryAdd(thing.Clone(), false);
        }
    }

    private static T Clone<T>(this T thing) where T : Thing
    {
        var clone = (T)ThingMaker.MakeThing(thing.def, thing.Stuff);
        clone.HitPoints = thing.HitPoints;

        // FIX #008: Copy stackCount, quality, and color.
        clone.stackCount = thing.stackCount;

        if (thing.TryGetComp<CompQuality>() is { } srcQ && clone.TryGetComp<CompQuality>() is { } dstQ)
            dstQ.SetQuality(srcQ.Quality, ArtGenerationContext.Outsider);

        if (thing.TryGetComp<CompColorable>() is { Active: true } srcC && clone.TryGetComp<CompColorable>() is { } dstC)
            dstC.SetColor(srcC.Color);

        if (thing is ThingWithComps twc)
            foreach (var comp in twc.AllComps)
                comp.PostSplitOff(clone);

        return clone;
    }
}
