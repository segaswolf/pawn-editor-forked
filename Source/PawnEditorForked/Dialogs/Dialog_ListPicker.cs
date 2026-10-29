using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// A searchable list to pick one entry from, for choices too long for a float menu.
/// </summary>
/// <remarks>
/// <para>
/// With a large mod list, float menus such as the Source and Category filters filled the screen in
/// columns and had no search. This window shows one entry per row, a search box focused on open, and
/// an optional count next to each entry.
/// </para>
/// <para>
/// Generic on purpose: every long menu in the editor can be moved here by building a list of
/// <see cref="Entry"/>, without another window class.
/// </para>
/// </remarks>
public class Dialog_ListPicker : Window
{
    private const float RowHeight = 28f;
    private const string SearchControl = "PawnEditorListPickerSearch";

    private readonly string title;
    private readonly List<Entry> entries;
    private string search = "";
    private Vector2 scrollPosition;
    private bool focusSearch = true;

    public Dialog_ListPicker(string title, IEnumerable<Entry> entries)
    {
        this.title = title;
        this.entries = entries.ToList();

        doCloseX = true;
        closeOnClickedOutside = true;
        onlyOneOfTypeAllowed = true;
        // Has a search box: absorb input so typing doesn't trigger game hotkeys.
        absorbInputAroundWindow = true;
        preventCameraMotion = true;
    }

    public override Vector2 InitialSize => new(420f, 560f);

    /// <summary>One row: its text, an optional count, whether it is the current choice, and what picking it does.</summary>
    public readonly struct Entry
    {
        public Entry(string label, Action onPicked, bool selected = false, int count = -1, bool alwaysShown = false)
        {
            Label = label;
            OnPicked = onPicked;
            Selected = selected;
            Count = count;
            AlwaysShown = alwaysShown;
        }

        public string Label { get; }
        public Action OnPicked { get; }
        public bool Selected { get; }

        /// <summary>Shown greyed next to the label when zero or more.</summary>
        public int Count { get; }

        /// <summary>Kept visible whatever the search says, for entries like "All".</summary>
        public bool AlwaysShown { get; }
    }

    /// <summary>
    /// Entries for a Source filter: "All" first, then every mod that provides one of the candidates,
    /// alphabetically, with how many candidates each one provides.
    /// </summary>
    public static IEnumerable<Entry> ModSources(IEnumerable<Def> candidates, ModContentPack current, Action<ModContentPack> onPicked)
    {
        yield return new Entry("PawnEditor.All".Translate().CapitalizeFirst(), () => onPicked(null), current == null, alwaysShown: true);

        var mods = candidates
            .Where(def => def?.modContentPack != null)
            .GroupBy(def => def.modContentPack)
            .OrderBy(group => group.Key.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var group in mods)
        {
            var mod = group.Key;
            yield return new Entry(mod.Name, () => onPicked(mod), mod == current, group.Count());
        }
    }

    public override void DoWindowContents(Rect inRect)
    {
        using (new TextBlock(GameFont.Medium))
            Widgets.Label(inRect.TakeTopPart(Text.LineHeight + 4f), title);
        inRect.yMin += 6f;

        DrawSearch(inRect.TakeTopPart(30f));
        inRect.yMin += 6f;

        var shown = entries.Where(entry => entry.AlwaysShown || search.NullOrEmpty()
                                           || entry.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        var viewRect = new Rect(0f, 0f, inRect.width - 16f, shown.Count * RowHeight);

        Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);
        try
        {
            for (var i = 0; i < shown.Count; i++)
                DrawRow(new Rect(0f, i * RowHeight, viewRect.width, RowHeight), shown[i], i);
        }
        finally
        {
            Widgets.EndScrollView(); // always closed: an open scroll view blocks Mouse.IsOver for the frame
        }
    }

    private void DrawSearch(Rect rect)
    {
        GUI.DrawTexture(rect.TakeLeftPart(24f).ContractedBy(4f), TexButton.Search);
        GUI.SetNextControlName(SearchControl);
        search = Widgets.TextField(rect, search);
        if (!focusSearch) return;
        GUI.FocusControl(SearchControl);
        focusSearch = false;
    }

    private void DrawRow(Rect row, Entry entry, int index)
    {
        if (index % 2 == 1) Widgets.DrawLightHighlight(row);
        if (entry.Selected) Widgets.DrawHighlightSelected(row);
        Widgets.DrawHighlightIfMouseover(row);

        var text = entry.Count >= 0 ? entry.Label + (" (" + entry.Count + ")").Colorize(ColoredText.SubtleGrayColor) : entry.Label;
        if (!Widgets.ButtonText(row.ContractedBy(4f, 0f), text, drawBackground: false, overrideTextAnchor: TextAnchor.MiddleLeft))
            return;

        entry.OnPicked?.Invoke();
        Close();
    }
}
