using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Draws the "Basic info" section of the Bio tab: name fields, age fields, and backstory buttons.
/// Part of <see cref="TabWorker_Bio_Humanlike"/> (partial class).
/// </summary>
public partial class TabWorker_Bio_Humanlike
{
    private string ageBiologicalBuffer;
    private string ageChronologicalBuffer;
    private Pawn bufferForPawn;

    /// <summary>Set by SetDevStage to force the age input buffers to re-read from the pawn.</summary>
    internal static bool resetAgeBuffers;

    private const int MinBioAge = 0;
    private const int MaxBioAge = 9999;
    private const int MaxChronoAge = 99999;
    private const long TicksPerYear = 3600000L;

    /// <summary>
    /// Draws name fields, biological/chronological age inputs, and backstory buttons.
    /// When the user changes the biological age across a developmental stage boundary
    /// (e.g., 18→13), the adult backstory is automatically removed or generated.
    /// </summary>
    private void DoBasics(Rect inRect, Pawn pawn)
    {
        inRect.xMax -= 10;
        Widgets.Label(inRect.TakeTopPart(Text.LineHeight), "PawnEditor.Basic".Translate().Colorize(ColoredText.TipSectionTitleColor));
        inRect.xMin += 5;

        var name = "PawnEditor.Name".Translate();
        var age = "PawnEditor.Age".Translate();
        var childhood = "Childhood".Translate();
        var adulthood = "Adulthood".Translate();
        var sexuality = "PawnEditor.RJW.Sexuality".Translate();
        var leftWidth = UIUtility.ColumnWidth(3, name, age, childhood, adulthood, sexuality) + 32f;

        DrawNameFields(inRect.TakeTopPart(30), pawn, leftWidth);

        inRect.yMin += 3;
        DrawAgeFields(inRect.TakeTopPart(50), pawn, leftWidth);

        DrawBackstoryButtons(ref inRect, pawn, leftWidth, childhood, adulthood);
        DrawSexuality(ref inRect, pawn, leftWidth);
    }

    /// <summary>
    /// Draws the first/nick/last name input fields, or a single name field for NameSingle pawns.
    /// </summary>
    private static void DrawNameFields(Rect nameRect, Pawn pawn, float leftWidth)
    {
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(nameRect.TakeLeftPart(leftWidth), "PawnEditor.Name".Translate());

        if (pawn.Name is NameTriple nameTriple)
        {
            var thirdWidth = nameRect.width * 0.333f;
            var firstRect = new Rect(nameRect) { width = thirdWidth };
            var nickRect = new Rect(nameRect) { width = thirdWidth, x = nameRect.x + thirdWidth };
            var lastRect = new Rect(nameRect) { width = thirdWidth, x = nameRect.x + thirdWidth * 2f };

            var first = nameTriple.First;
            var nick = nameTriple.Nick;
            var last = nameTriple.Last;

            CharacterCardUtility.DoNameInputRect(firstRect, ref first, 12);
            if (nameTriple.Nick == nameTriple.First || nameTriple.Nick == nameTriple.Last)
                GUI.color = new(1f, 1f, 1f, 0.5f);
            CharacterCardUtility.DoNameInputRect(nickRect, ref nick, 16);
            GUI.color = Color.white;
            CharacterCardUtility.DoNameInputRect(lastRect, ref last, 12);

            if (nameTriple.First != first || nameTriple.Nick != nick || nameTriple.Last != last)
                pawn.Name = new NameTriple(first, string.IsNullOrEmpty(nick) ? first : nick, last);

            TooltipHandler.TipRegionByKey(firstRect, "FirstNameDesc");
            TooltipHandler.TipRegionByKey(nickRect, "ShortIdentifierDesc");
            TooltipHandler.TipRegionByKey(lastRect, "LastNameDesc");
        }
        else if (pawn.Name is NameSingle nameSingle)
        {
            var nameSingleName = nameSingle.Name;
            CharacterCardUtility.DoNameInputRect(nameRect, ref nameSingleName, 16);
            if (nameSingleName != nameSingle.Name)
                pawn.Name = new NameSingle(nameSingleName);

            TooltipHandler.TipRegionByKey(nameRect, "ShortIdentifierDesc");
        }
        else
        {
            Widgets.Label(nameRect, pawn.NameFullColored);
        }
    }

    /// <summary>
    /// Draws the biological and chronological age input fields.
    /// When biological age changes across a developmental stage boundary,
    /// handles backstory transitions (add/remove adult backstory).
    /// </summary>
    private void DrawAgeFields(Rect ageRect, Pawn pawn, float leftWidth)
    {
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(ageRect.TakeLeftPart(leftWidth), "PawnEditor.Age".Translate());

        // Reset input buffers when switching pawns or after dev stage change
        if (bufferForPawn == null || bufferForPawn != pawn || resetAgeBuffers)
        {
            ageBiologicalBuffer = null;
            ageChronologicalBuffer = null;
            bufferForPawn = pawn;
            resetAgeBuffers = false;
        }

        // Biological age — full range allowed so users can change dev stage via age
        var bio = ageRect.LeftPart(0.6f).LeftHalf();
        using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleCenter))
            Widgets.Label(bio.TakeTopPart(Text.LineHeight), "PawnEditor.Biological".Translate());

        var ageBio = pawn.ageTracker.AgeBiologicalYears;
        UIUtility.IntField(bio, ref ageBio, MinBioAge, MaxBioAge, ref ageBiologicalBuffer);

        if (ageBio != pawn.ageTracker.AgeBiologicalYears)
            ApplyBiologicalAge(pawn, ageBio);

        // Chronological age — minimum is biological age (can't be younger than bio)
        var chrono = ageRect.LeftPart(0.6f).RightHalf();
        using (new TextBlock(GameFont.Tiny, TextAnchor.MiddleCenter))
            Widgets.Label(chrono.TakeTopPart(Text.LineHeight), "PawnEditor.Chronological".Translate());

        var ageChrono = pawn.ageTracker.AgeChronologicalYears;
        var minChrono = pawn.ageTracker.AgeBiologicalYears;
        UIUtility.IntField(chrono, ref ageChrono, minChrono, MaxChronoAge, ref ageChronologicalBuffer);

        if (ageChrono != pawn.ageTracker.AgeChronologicalYears)
        {
            pawn.ageTracker.AgeChronologicalTicks = ageChrono * TicksPerYear;
            PawnEditor.Notify_PointsUsed();
        }
    }

    /// <summary>
    /// Applies a new biological age to a pawn and handles dev stage transitions.
    /// If the age change crosses a dev stage boundary (e.g., adult→child at 13),
    /// removes or generates the adult backstory and adjusts body type.
    /// </summary>
    private void ApplyBiologicalAge(Pawn pawn, int newAge)
    {
        var oldDevStage = pawn.DevelopmentalStage;
        pawn.ageTracker.AgeBiologicalTicks = newAge * TicksPerYear;
        var newDevStage = pawn.DevelopmentalStage;

        // Keep chrono age ≥ bio age
        if (newAge > pawn.ageTracker.AgeChronologicalYears)
        {
            pawn.ageTracker.AgeChronologicalTicks = newAge * TicksPerYear;
            ageChronologicalBuffer = null;
        }

        // Handle backstory transitions when dev stage changes
        if (oldDevStage != newDevStage)
            BackstoryUtility.HandleDevStageTransition(pawn, newDevStage);

        PawnEditor.Notify_PointsUsed();
    }

    /// <summary>
    /// Draws the childhood and adulthood backstory buttons with tooltips.
    /// Each button opens a ListingMenu_Backstories for that slot.
    /// </summary>
    private static void DrawBackstoryButtons(ref Rect inRect, Pawn pawn, float leftWidth, string childhoodLabel, string adulthoodLabel)
    {
        if (pawn.story.Childhood != null)
        {
            inRect.yMin += 3;
            var childRect = inRect.TakeTopPart(30);
            using (new TextBlock(TextAnchor.MiddleLeft))
                Widgets.Label(childRect.TakeLeftPart(leftWidth), childhoodLabel);
            if (Widgets.ButtonText(childRect.LeftPart(0.6f), pawn.story.Childhood.TitleCapFor(pawn.gender)))
                Find.WindowStack.Add(new ListingMenu_Backstories(pawn, BackstorySlot.Childhood));
            TooltipHandler.TipRegion(childRect.LeftPart(0.6f),
                (TipSignal)pawn.story.childhood.FullDescriptionFor(pawn).Resolve());
        }

        if (pawn.story.Adulthood != null)
        {
            inRect.yMin += 3;
            var adultRect = inRect.TakeTopPart(30);
            using (new TextBlock(TextAnchor.MiddleLeft))
                Widgets.Label(adultRect.TakeLeftPart(leftWidth), adulthoodLabel);
            if (Widgets.ButtonText(adultRect.LeftPart(0.6f), pawn.story.Adulthood.TitleCapFor(pawn.gender)))
                Find.WindowStack.Add(new ListingMenu_Backstories(pawn, BackstorySlot.Adulthood));
            TooltipHandler.TipRegion(adultRect.LeftPart(0.6f),
                (TipSignal)pawn.story.adulthood.FullDescriptionFor(pawn).Resolve());
        }
    }

    private static void DrawSexuality(ref Rect inRect, Pawn pawn, float leftWidth)
    {
        if (!RJWCompat.IsAvailableForPawn(pawn))
            return;

        inRect.yMin += 3f;
        var row = inRect.TakeTopPart(30f);
        using (new TextBlock(TextAnchor.MiddleLeft))
            Widgets.Label(row.TakeLeftPart(leftWidth), "PawnEditor.RJW.Sexuality".Translate());

        if (!Widgets.ButtonText(row.LeftPart(0.6f), RJWCompat.GetOrientation(pawn)))
            return;

        var options = RJWCompat.OrientationNames()
            .Select(name => new FloatMenuOption(name, () => RJWCompat.SetOrientation(pawn, name)))
            .ToList();
        if (options.Count == 0)
            options.Add(new FloatMenuOption("PawnEditor.NoOptions".Translate(), null));
        Find.WindowStack.Add(new FloatMenu(options));
    }
}
