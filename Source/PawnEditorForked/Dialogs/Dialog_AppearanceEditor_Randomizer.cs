using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// The Randomize button and its "repeat last" arrow.
//
// Randomizing draws from the same lists the pickers show, so the dice can never land on something the
// player could not have clicked: it respects the Source filter, genes, developmental stage and HAR.
// It used to keep its own copies of those lists, and the tattoo one ignored the tattoo type, so a
// body tattoo could end up on the face.
public partial class Dialog_AppearanceEditor
{
    private const float RepeatButtonWidth = 20f;
    private const float RepeatButtonGap = 1f;

    private void DrawRandomizeButtons(Rect inRect)
    {
        var randomRect = new Rect(0, 0, 200, 40).CenteredOnXIn(inRect).CenteredOnYIn(inRect);

        if (lastRandomization != null)
        {
            var repeatRect = randomRect.TakeRightPart(RepeatButtonWidth);
            randomRect.xMax -= RepeatButtonGap;
            if (Widgets.ButtonImageWithBG(repeatRect, TexUI.RotRightTex, new Vector2(12, 12)))
                lastRandomization();
        }

        if (Widgets.ButtonText(randomRect, "Randomize".Translate()))
            Find.WindowStack.Add(new FloatMenu(RandomizeOptions()));
    }

    private List<FloatMenuOption> RandomizeOptions()
    {
        var choices = new List<(string label, Func<bool> randomize)>
        {
            ("PawnEditor.Shape".Translate(), RandomizeBodyType),
            ("PawnEditor.Head".Translate().CapitalizeFirst(), RandomizeHeadType)
        };
        if (ModsConfig.IdeologyActive) choices.Add(("Tattoos".Translate(), RandomizeTattoos));

        return choices.Select(choice => new FloatMenuOption("Randomize".Translate() + " " + choice.label, () =>
            {
                lastRandomization = () => RunRandomization(choice.randomize);
                lastRandomization();
            }))
            .ToList();
    }

    private void RunRandomization(Func<bool> randomize)
    {
        if (randomize())
            PawnEditor.RefreshPawnGraphics(pawn);
        else
            Messages.Message("PawnEditor.NothingToRandomize".Translate(), MessageTypeDefOf.RejectInput, false);
    }

    /// <returns>False when the current filters leave nothing to pick; the pawn is left untouched.</returns>
    internal bool RandomizeBodyType()
    {
        if (!BodyTypeOptions().TryRandomElement(out var bodyType)) return false;
        pawn.story.bodyType = bodyType;
        return true;
    }

    /// <returns>False when the current filters leave nothing to pick; the pawn is left untouched.</returns>
    internal bool RandomizeHeadType()
    {
        if (!HeadTypeOptions().TryRandomElement(out var headType)) return false;
        pawn.story.headType = headType;
        return true;
    }

    /// <summary>Each slot draws from its own tattoo type; a slot with nothing to offer keeps its tattoo.</summary>
    /// <returns>False when neither slot had anything to pick.</returns>
    internal bool RandomizeTattoos()
    {
        var changed = false;
        if (TattooOptions(TattooType.Face).TryRandomElement(out var face))
        {
            pawn.style.FaceTattoo = face;
            changed = true;
        }
        if (TattooOptions(TattooType.Body).TryRandomElement(out var body))
        {
            pawn.style.BodyTattoo = body;
            changed = true;
        }
        return changed;
    }
}
