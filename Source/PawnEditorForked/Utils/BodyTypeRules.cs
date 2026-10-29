using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Which body types a pawn can be given, shared by every body type picker in the editor.
/// </summary>
/// <remarks>
/// <para>
/// The Bio tab's Shape button, the appearance editor's Body grid and the randomizer each used to
/// filter body types their own way, so a mod's body type could show up in one and not another.
/// </para>
/// <para>
/// Some mods add body types for their own internal use: Big and Small's <c>BS_AnimalBodyType</c>
/// points at a blank texture on purpose, so picking it leaves the pawn invisible. These are still
/// offered (someone may want them), but listed last, flagged, and never rolled by Randomize.
/// "Special" is decided from the data, not a hand-written list: a body type is ordinary when normal
/// pawn generation can hand it out, meaning a backstory names it, it is the baby or child body, or
/// the pawn's HAR race allows it.
/// </para>
/// </remarks>
public static class BodyTypeRules
{
    private static HashSet<BodyTypeDef> namedByBackstories;

    /// <summary>
    /// Body types this pawn may have: matching its developmental stage and, with HAR, its race.
    /// </summary>
    /// <param name="ignoreRestrictions">The appearance editor's "ignore xenotype" override.</param>
    public static IEnumerable<BodyTypeDef> For(Pawn pawn, bool ignoreRestrictions = false)
    {
        IEnumerable<BodyTypeDef> q = DefDatabase<BodyTypeDef>.AllDefs
            .Where(bodyType => ignoreRestrictions || !ModsConfig.BiotechActive || !pawn.DevelopmentalStage.Juvenile()
                               || bodyType == BodyTypeDefOf.Baby || bodyType == BodyTypeDefOf.Child)
            .Where(bodyType => pawn.DevelopmentalStage switch
            {
                DevelopmentalStage.Baby or DevelopmentalStage.Newborn => bodyType == BodyTypeDefOf.Baby,
                DevelopmentalStage.Child => bodyType == BodyTypeDefOf.Child,
                DevelopmentalStage.Adult => bodyType != BodyTypeDefOf.Baby && bodyType != BodyTypeDefOf.Child,
                _ => true
            });

        if (HARCompat.Active)
        {
            var allowedBodyTypes = HARCompat.AllowedBodyTypes(pawn);
            if (!allowedBodyTypes.NullOrEmpty()) q = q.Intersect(allowedBodyTypes);
        }

        return q;
    }

    /// <summary>
    /// True for body types that normal generation never hands out, which mods use for their own
    /// purposes and which may draw wrong or not at all.
    /// </summary>
    public static bool IsSpecial(BodyTypeDef bodyType, Pawn pawn = null)
    {
        if (bodyType == null) return false;
        if (bodyType == BodyTypeDefOf.Baby || bodyType == BodyTypeDefOf.Child) return false;

        namedByBackstories ??= DefDatabase<BackstoryDef>.AllDefs
            .SelectMany(backstory => new[] { backstory.bodyTypeGlobal, backstory.bodyTypeFemale, backstory.bodyTypeMale })
            .Where(named => named != null)
            .ToHashSet();
        if (namedByBackstories.Contains(bodyType)) return false;

        return !(pawn != null && HARCompat.Active && HARCompat.AllowedBodyTypes(pawn)?.Contains(bodyType) == true);
    }

    /// <summary>The label to show: the def's label when it has one, otherwise its defName.</summary>
    public static string LabelFor(BodyTypeDef bodyType) =>
        bodyType.label.NullOrEmpty() ? bodyType.defName : bodyType.LabelCap.ToString();

    /// <summary>A warning for special body types, or null for ordinary ones.</summary>
    public static string WarningFor(BodyTypeDef bodyType, Pawn pawn) =>
        IsSpecial(bodyType, pawn) ? "PawnEditor.SpecialBodyTypeDesc".Translate().ToString() : null;
}
