using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

public abstract class Dialog_PawnEditor : Window, IMinWindowSize
{
    /// <summary>
    /// Floor for the main editor window, enforced by the resizer itself (see IMinWindowSize).
    /// Without it the window could be shrunk past the point where the tab row and the bottom buttons
    /// fit, and everything collapsed into a squashed strip on the left. There is NO maximum on
    /// purpose: growing the window is always fine, the layout just spreads out.
    /// </summary>
    public Vector2 MinWindowSize => new(900f, 600f);

    protected Dialog_PawnEditor()
    {
        forcePause = PawnEditorMod.Settings?.PauseWhileEditorOpen ?? true;
        absorbInputAroundWindow = false;
        closeOnAccept = false;
        closeOnCancel = true;
        forceCatchAcceptAndCancelEventEvenIfUnfocused = true;
        closeOnClickedOutside = true;
    }

    protected abstract bool Pregame { get; }

    /// <summary>
    /// Slightly roomier than the vanilla page size: the editor now hosts extra tabs and optional mod
    /// sections (Trauma &amp; Integrity, Development, Royalty), which felt cramped at the old default.
    /// Capped against the actual screen so it still opens sensibly on small displays, and the user can
    /// resize from the corner as always.
    /// </summary>
    public override Vector2 InitialSize => new(
        Mathf.Min(1120f, UI.screenWidth * 0.92f),
        Mathf.Min(740f, UI.screenHeight * 0.92f));

    public override void PreOpen()
    {
        base.PreOpen();

        // Dim whatever is behind us (most noticeably vanilla's own character page during pregame).
        // Opened HERE on purpose: WindowStack.Add runs PreOpen before inserting this window, so the
        // backdrop lands in the stack first and therefore draws behind the editor.
        // Guarded: the dim is decoration. If it can't open, the editor still must.
        try { Find.WindowStack.Add(new Window_EditorBackdrop()); }
        catch (Exception ex) { Log.Warning($"[Pawn Editor] Could not open the editor backdrop: {ex.Message}"); }

        PawnEditor.Pregame = Pregame;
        PawnEditor.RecachePawnList();
        PawnEditor.CheckChangeTabGroup();
        TabWorker<Pawn>.Notify_OpenedDialog();
        TabWorker<Faction>.Notify_OpenedDialog();
        PawnEditor.ResetPoints();
    }

    public override void PostClose()
    {
        base.PostClose();
        EditUtility.CurrentWindow?.Close();
        // Take the dim down with us, so it can never linger over the game.
        Find.WindowStack.TryRemove(typeof(Window_EditorBackdrop), false);
    }

    public override void OnCancelKeyPressed()
    {
        if (EditUtility.CurrentWindow != null && Find.WindowStack.IsOpen(EditUtility.CurrentWindow))
            EditUtility.CurrentWindow.OnCancelKeyPressed();
        else base.OnCancelKeyPressed();
    }
}

public class Dialog_PawnEditor_Pregame : Dialog_PawnEditor
{
    private readonly Action doNext;

    public Dialog_PawnEditor_Pregame(Action doNext) => this.doNext = doNext;

    protected override bool Pregame => true;

    public override void DoWindowContents(Rect inRect)
    {
        PawnEditor.DoUI(inRect, () => Close(), doNext);
    }
}

public class Dialog_PawnEditor_InGame : Dialog_PawnEditor
{
    protected override bool Pregame => false;

    public override void PreOpen()
    {
        ColonyInventory.RecacheItems();
        base.PreOpen();
        if (Find.Selector.SingleSelectedThing is Pawn pawn)
            PawnEditor.Select(pawn);
    }

    public override void OnCancelKeyPressed()
    {
        if (PawnEditor.CanExit())
            base.OnCancelKeyPressed();
    }

    public override void PostClose()
    {
        base.PostClose();
        if (PawnEditorMod.Settings.UseSilver) PawnEditor.ApplyPoints();
    }

    public override void DoWindowContents(Rect inRect)
    {
        PawnEditor.DoUI(inRect, () => Close(), null);
    }
}
