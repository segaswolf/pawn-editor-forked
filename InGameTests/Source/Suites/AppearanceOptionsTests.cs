using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using AppearanceEditor = global::PawnEditor.Dialog_AppearanceEditor;

namespace PawnEditorInGameTests;

/// <summary>
/// What the appearance editor's style pickers offer: body, head, hair, beard, body and face tattoos.
/// </summary>
/// <remarks>
/// <para>
/// Safety net for refactor step 4. Written right after the queries were extracted verbatim and before
/// anything else moved, so its first green run records today's behaviour. From then on it guards that
/// the cut does not change what the player is offered.
/// </para>
/// <para>
/// It checks the queries, not the drawing. IMGUI can't run outside OnGUI, but the option lists are
/// where the behaviour lives and where mod content gets filtered in or out.
/// </para>
/// </remarks>
public static class AppearanceOptionsTests
{
    [InGameTest]
    public static void Dialog_OpensForAColonist_WithoutThrowing()
    {
        var pawn = TestPawns.Colonist();
        Check.DoesNotThrow(() => new AppearanceEditor(pawn), "creating the appearance editor");
    }

    [InGameTest]
    public static void EveryPicker_OffersSomething_ForAColonist()
    {
        var editor = new AppearanceEditor(TestPawns.Colonist());

        NotEmpty(editor.BodyTypeOptions(), "body types");
        NotEmpty(editor.HeadTypeOptions(), "head types");
        NotEmpty(editor.HairOptions(), "hairs");
        NotEmpty(editor.BeardOptions(), "beards");
        NotEmpty(editor.TattooOptions(TattooType.Body), "body tattoos");
        NotEmpty(editor.TattooOptions(TattooType.Face), "face tattoos");
    }

    /// <summary>
    /// Property, not a single case: whatever age the generator gave the colonist, adults never get the
    /// baby or child body, and a child only gets the child body. Babies and children are generated too
    /// when Biotech is active, since those are the stages the filter exists for.
    /// </summary>
    [InGameTest]
    public static void BodyTypes_MatchTheDevelopmentalStage()
    {
        foreach (var pawn in PawnsOfEveryStage())
        {
            var offered = new AppearanceEditor(pawn).BodyTypeOptions().ToList();
            var stage = pawn.DevelopmentalStage;
            var who = $"{stage} pawn aged {pawn.ageTracker.AgeBiologicalYears}";

            NotEmpty(offered, $"body types for a {who}");
            if (stage == DevelopmentalStage.Adult)
                Check.That(!offered.Contains(BodyTypeDefOf.Baby) && !offered.Contains(BodyTypeDefOf.Child),
                    $"an adult was offered the baby or child body: {Names(offered)}");
            else if (stage == DevelopmentalStage.Child)
                Check.That(offered.All(body => body == BodyTypeDefOf.Child),
                    $"a child was offered bodies other than Child: {Names(offered)}");
            else if (stage is DevelopmentalStage.Baby or DevelopmentalStage.Newborn)
                Check.That(offered.All(body => body == BodyTypeDefOf.Baby),
                    $"a {stage} was offered bodies other than Baby: {Names(offered)}");
        }
    }

    [InGameTest]
    public static void TattooPickers_OnlyOfferTheirOwnType()
    {
        var editor = new AppearanceEditor(TestPawns.Colonist());

        var wrongBody = editor.TattooOptions(TattooType.Body).Where(tattoo => tattoo.tattooType != TattooType.Body).ToList();
        var wrongFace = editor.TattooOptions(TattooType.Face).Where(tattoo => tattoo.tattooType != TattooType.Face).ToList();

        Check.That(wrongBody.Count == 0, $"the body tattoo picker offered face tattoos: {Names(wrongBody)}");
        Check.That(wrongFace.Count == 0, $"the face tattoo picker offered body tattoos: {Names(wrongFace)}");
    }

    /// <summary>
    /// Without HAR and with no source filter, the hair and beard pickers must offer EVERY loaded def.
    /// This is what makes other mods' styles show up at all; a filter that silently drops some is the
    /// kind of bug that only surfaces as "your editor doesn't show my mod's hair".
    /// </summary>
    [InGameTest]
    public static void HairAndBeards_WithoutHar_OfferEveryLoadedDef()
    {
        if (global::PawnEditor.HARCompat.Active) return; // HAR restricts on purpose; covered by its own profile.

        var editor = new AppearanceEditor(TestPawns.Colonist());

        Check.Equal(DefDatabase<HairDef>.DefCount, editor.HairOptions().Count(), "hairs offered vs loaded");
        Check.Equal(DefDatabase<BeardDef>.DefCount, editor.BeardOptions().Count(), "beards offered vs loaded");
    }

    /// <summary>
    /// Each style tab shows the right sub-tabs, in order, and each sub-tab's candidates are the right
    /// kind of def. The Source menu reads these candidates, so this is what keeps "Beard" from listing
    /// hair mods, which it did while one shared enum meant different things in each tab.
    /// </summary>
    [InGameTest]
    public static void StyleTabs_EachSubTab_OffersItsOwnKindOfDef()
    {
        var editor = new AppearanceEditor(TestPawns.Colonist());

        SubTabsOffer(editor, AppearanceEditor.MainTab.Shape, typeof(BodyTypeDef), typeof(HeadTypeDef));
        SubTabsOffer(editor, AppearanceEditor.MainTab.Hair, typeof(HairDef), typeof(BeardDef));
        SubTabsOffer(editor, AppearanceEditor.MainTab.Tattoos, typeof(TattooDef), typeof(TattooDef));

        var tattoos = editor.StylePickersFor(AppearanceEditor.MainTab.Tattoos);
        Check.That(tattoos[0].Candidates().Cast<TattooDef>().All(t => t.tattooType == TattooType.Body),
            "the first tattoo sub-tab offered face tattoos");
        Check.That(tattoos[1].Candidates().Cast<TattooDef>().All(t => t.tattooType == TattooType.Face),
            "the second tattoo sub-tab offered body tattoos");

        Check.That(editor.StylePickersFor(AppearanceEditor.MainTab.Xenotype) == null,
            "the cosmetic genes tab is drawn by itself and should have no style pickers");
    }

    private static void SubTabsOffer(AppearanceEditor editor, AppearanceEditor.MainTab tab, params System.Type[] kinds)
    {
        var pickers = editor.StylePickersFor(tab);
        Check.NotNull(pickers, $"{tab} pickers");
        Check.Equal(kinds.Length, pickers.Count, $"{tab} sub-tab count");

        for (var i = 0; i < kinds.Length; i++)
        {
            var candidates = pickers[i].Candidates().ToList();
            var wrong = candidates.Where(def => !kinds[i].IsInstanceOfType(def)).ToList();
            NotEmpty(candidates, $"{tab} sub-tab '{pickers[i].Label}' candidates");
            Check.That(wrong.Count == 0,
                $"{tab} sub-tab '{pickers[i].Label}' should offer {kinds[i].Name} but offered: {Names(wrong)}");
        }
    }

    private static IEnumerable<Pawn> PawnsOfEveryStage()
    {
        yield return TestPawns.Colonist();
        if (!ModsConfig.BiotechActive) yield break;

        foreach (var age in new[] { 1f, 8f })
            yield return PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, fixedBiologicalAge: age, fixedChronologicalAge: age,
                developmentalStages: DevelopmentalStage.Baby | DevelopmentalStage.Child | DevelopmentalStage.Adult));
    }

    private static void NotEmpty<T>(IEnumerable<T> options, string what) =>
        Check.That(options.Any(), $"the picker offered no {what}");

    private static string Names<T>(IEnumerable<T> defs) where T : Def =>
        string.Join(", ", defs.Select(def => def.defName));
}
