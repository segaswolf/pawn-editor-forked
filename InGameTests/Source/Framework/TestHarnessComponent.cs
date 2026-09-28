using Verse;

namespace PawnEditorInGameTests;

/// <summary>
/// The trigger. RimWorld creates one of these for every game automatically; it stays inert unless
/// the game was launched with -pawneditortests.
///
/// FinalizeInit runs once the game (and, with -quicktest, its map) is fully set up. The tests are
/// deferred with ExecuteWhenFinished so they start after any pending long event, never halfway
/// through loading.
/// </summary>
public class TestHarnessComponent : GameComponent
{
    private static bool alreadyStarted;

    public TestHarnessComponent(Game game)
    {
    }

    public override void FinalizeInit()
    {
        if (alreadyStarted || !TestRunner.RequestedOnCommandLine) return;

        alreadyStarted = true;
        LongEventHandler.ExecuteWhenFinished(TestRunner.RunAllThenQuit);
    }
}
