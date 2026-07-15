using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Small dialog to pick which pawn categories a colony save (or load) should include:
/// Humanlike, Animals, Mechs. All checked by default. Confirms with the chosen flags.
/// </summary>
[HotSwappable]
public class Dialog_ColonyCategories : Window
{
    private bool humans;
    private bool animals;
    private bool mechs;

    private readonly string title;
    private readonly string confirmLabel;
    private readonly string note;
    private readonly Action<bool, bool, bool> onConfirm;

    public override Vector2 InitialSize => new Vector2(360f, note.NullOrEmpty() ? 210f : 300f);

    public Dialog_ColonyCategories(string title, string confirmLabel, Action<bool, bool, bool> onConfirm,
        bool humans = true, bool animals = true, bool mechs = true, string note = null)
    {
        this.title = title;
        this.confirmLabel = confirmLabel;
        this.onConfirm = onConfirm;
        this.note = note;
        this.humans = humans;
        this.animals = animals;
        this.mechs = mechs;

        forcePause = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = true;
    }

    public override void DoWindowContents(Rect inRect)
    {
        var buttonRow = inRect.BottomPartPixels(36f);
        var listRect = inRect.TopPartPixels(inRect.height - 44f);

        var listing = new Listing_Standard();
        listing.Begin(listRect);
        using (new TextBlock(GameFont.Medium)) listing.Label(title);
        listing.Gap(8f);
        listing.CheckboxLabeled(PawnCategory.Humans.LabelCapPlural(),  ref humans);
        listing.CheckboxLabeled(PawnCategory.Animals.LabelCapPlural(), ref animals);
        listing.CheckboxLabeled(PawnCategory.Mechs.LabelCapPlural(),   ref mechs);
        if (!note.NullOrEmpty())
        {
            listing.Gap(10f);
            using (new TextBlock(GameFont.Tiny))
                listing.Label(note.Colorize(ColoredText.SubtleGrayColor));
        }
        listing.End();

        if (Widgets.ButtonText(buttonRow.RightHalf().ContractedBy(2f, 0f), confirmLabel))
        {
            if (!humans && !animals && !mechs)
            {
                Messages.Message("PawnEditor.PickAtLeastOneCategory".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Close();
            onConfirm?.Invoke(humans, animals, mechs);
        }

        if (Widgets.ButtonText(buttonRow.LeftHalf().ContractedBy(2f, 0f), "CancelButton".Translate()))
            Close();
    }
}
