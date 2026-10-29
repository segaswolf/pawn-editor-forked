using System.Collections;
using System.Linq;
using System.Reflection;
using PawnEditor;
using RimWorld;
using Verse;

namespace PawnEditorInGameTests;

/// <summary>
/// Defs other mods add from their own startup code (Backstory Constructor's backstories, for one) must
/// reach the pickers. Bug #36: the backstory list was copied at startup and could miss them.
/// </summary>
public static class LateDefsTests
{
    [InGameTest]
    public static void BackstoryPicker_ListsEveryBackstoryDef()
    {
        var field = typeof(ListingMenu_Backstories).GetField("backstoriesBySlot", BindingFlags.NonPublic | BindingFlags.Static);
        Check.NotNull(field, "ListingMenu_Backstories.backstoriesBySlot (renamed?)");
        var bySlot = field.GetValue(null) as IDictionary;
        Check.NotNull(bySlot, "the picker's backstory lists");

        foreach (var slot in new[] { BackstorySlot.Childhood, BackstorySlot.Adulthood })
        {
            var listed = ((IList)bySlot[slot]).Count;
            var defined = DefDatabase<BackstoryDef>.AllDefsListForReading.Count(def => def.slot == slot);
            Check.Equal(defined, listed, $"{slot} backstories in the picker");
        }
    }
}
