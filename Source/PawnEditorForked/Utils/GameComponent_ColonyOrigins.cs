using System.Collections.Generic;
using Verse;

namespace PawnEditor;

/// <summary>
/// Remembers which saved pawn (by its savedThingID) each loaded colony pawn came from.
///
/// Colony "Replace by ID" matches an existing pawn by its current ThingID against the blueprint's
/// savedThingID. That only finds the TRUE originals — a pawn that was itself loaded from a save has a
/// fresh ThingID, so a second Replace load couldn't recognize it and would ADD a duplicate instead of
/// replacing it. Stamping each loaded pawn with its origin lets Replace recognize those too, so
/// repeated loads are idempotent. Persisted with the game so it survives a save/reload.
/// </summary>
public class GameComponent_ColonyOrigins : GameComponent
{
    private Dictionary<int, string> originByPawnId = new();

    public GameComponent_ColonyOrigins(Game game) { }

    /// <summary>Remember that <paramref name="pawn"/> was loaded from the save entry <paramref name="savedId"/>.</summary>
    public void Record(Pawn pawn, string savedId)
    {
        if (pawn == null || savedId.NullOrEmpty()) return;
        originByPawnId[pawn.thingIDNumber] = savedId;
    }

    /// <summary>True if <paramref name="pawn"/> was previously loaded from the save entry <paramref name="savedId"/>.</summary>
    public bool CameFrom(Pawn pawn, string savedId) =>
        pawn != null && originByPawnId.TryGetValue(pawn.thingIDNumber, out var s) && s == savedId;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref originByPawnId, "pawnEditorColonyOrigins", LookMode.Value, LookMode.Value);
        originByPawnId ??= new Dictionary<int, string>();
    }
}
