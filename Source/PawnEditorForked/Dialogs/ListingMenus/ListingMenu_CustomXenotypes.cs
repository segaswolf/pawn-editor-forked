using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Dedicated listing for user-created custom xenotypes.
/// Opened from the "Custom xenotypes..." button in ListingMenu_Xenotypes.
/// Provides search, icons, tooltips — same experience as the main xenotype listing.
/// Refreshes automatically when new customs are created via the Xenotype Editor.
/// </summary>
public class ListingMenu_CustomXenotypes : Window
{
    private readonly Pawn _pawn;
    private Vector2 _scrollPos;
    private string _searchText = "";
    private List<CustomXenotype> _cachedList;

    public ListingMenu_CustomXenotypes(Pawn pawn)
    {
        _pawn = pawn;
        draggable = true;
        closeOnClickedOutside = true;
        onlyOneOfTypeAllowed = true;
        RefreshList();
    }

    public override Vector2 InitialSize => new(400f, 500f);

    private void RefreshList()
    {
        CharacterCardUtility.cachedCustomXenotypes = null; // Force reload from disk
        _cachedList = new List<CustomXenotype>();

        try
        {
            // Try vanilla cache first
            var cached = CharacterCardUtility.CustomXenotypes;
            if (cached != null && cached.Count > 0)
            {
                _cachedList = cached.ToList();
                return;
            }

            // Fallback: load directly from disk
            foreach (var filePath in GenFilePaths.AllCustomXenotypeFiles)
            {
                try
                {
                    var xenotype = new CustomXenotype();
                    if (GameDataSaveLoader.TryLoadXenotype(filePath.FullName, out xenotype))
                        _cachedList.Add(xenotype);
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[Pawn Editor] Failed to load custom xenotype from {filePath.Name}: {ex.Message}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning($"[Pawn Editor] RefreshList custom xenotypes: {ex.Message}");
        }
    }

    public override void DoWindowContents(Rect inRect)
    {
        // Header
        using (new TextBlock(GameFont.Medium))
            Widgets.Label(inRect.TakeTopPart(Text.LineHeight + 4f), "PawnEditor.CustomXenotypes".Translate());

        inRect.yMin += 8f;

        // Bottom: Close button
        var closeRect = inRect.TakeBottomPart(40f).ContractedBy(4f);
        if (Widgets.ButtonText(closeRect, "Close".Translate()))
            Close();

        // Search bar
        var searchRect = inRect.TakeBottomPart(30f).ContractedBy(2f);
        var searchIconRect = searchRect.TakeLeftPart(24f);
        GUI.DrawTexture(searchIconRect.ContractedBy(4f), TexButton.Search);
        _searchText = Widgets.TextField(searchRect, _searchText);
        if (!_searchText.NullOrEmpty() && Widgets.ButtonImage(searchRect.TakeRightPart(20f).ContractedBy(2f), TexButton.CloseXSmall))
            _searchText = "";

        inRect.yMax -= 4f;

        // Filtered list
        var filtered = _cachedList;
        if (!_searchText.NullOrEmpty())
            filtered = _cachedList.Where(x => x.name.IndexOf(_searchText, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        if (filtered.Count == 0)
        {
            using (new TextBlock(TextAnchor.MiddleCenter))
            {
                GUI.color = ColoredText.SubtleGrayColor;
                Widgets.Label(inRect, _cachedList.Count == 0
                    ? "PawnEditor.NoCustomXenotypes".Translate()
                    : "PawnEditor.NoSearchResults".Translate());
                GUI.color = Color.white;
            }
            return;
        }

        // Scrollable list
        var viewRect = new Rect(0, 0, inRect.width - 16f, filtered.Count * 34f);
        Widgets.BeginScrollView(inRect, ref _scrollPos, viewRect);

        for (var i = 0; i < filtered.Count; i++)
        {
            var custom = filtered[i];
            var rowRect = new Rect(0, i * 34f, viewRect.width, 32f);

            // Alternate row highlight
            if (i % 2 == 1)
                Widgets.DrawLightHighlight(rowRect);

            // Icon
            var iconRect = rowRect.TakeLeftPart(30f).ContractedBy(3f);
            if (custom.IconDef?.Icon != null)
            {
                GUI.color = XenotypeDef.IconColor;
                GUI.DrawTexture(iconRect, custom.IconDef.Icon);
                GUI.color = Color.white;
            }
            else
            {
                GUI.DrawTexture(iconRect, Widgets.PlaceholderIconTex);
            }

            rowRect.xMin += 4f;

            // Hover highlight + tooltip
            if (Mouse.IsOver(rowRect))
            {
                Widgets.DrawHighlight(rowRect);
                var inheritableText = custom.inheritable
                    ? "Inheritable".Translate()
                    : "PawnEditor.NotInheritable".Translate();
                var geneNames = custom.genes.Count <= 8
                    ? string.Join(", ", custom.genes.Select(g => g.LabelCap.ToString()))
                    : string.Join(", ", custom.genes.Take(8).Select(g => g.LabelCap.ToString())) + "...";
                var tip = custom.name.CapitalizeFirst() + "\n\n"
                    + inheritableText.Colorize(ColoredText.SubtleGrayColor)
                    + "\n" + ("Genes".Translate() + ": " + custom.genes.Count).Colorize(ColoredText.SubtleGrayColor)
                    + "\n" + geneNames.Colorize(ColoredText.SubtleGrayColor);
                TooltipHandler.TipRegion(rowRect, tip);
            }

            // Label — click to select
            if (Widgets.ButtonText(rowRect, custom.name.CapitalizeFirst(), drawBackground: false, overrideTextAnchor: TextAnchor.MiddleLeft))
            {
                TabWorker_Bio_Humanlike.SetXenotype(_pawn, custom);
                TabWorker_Bio_Humanlike.RecacheGraphics(_pawn);
                PawnEditor.Notify_PointsUsed();
                Close();
            }
        }

        Widgets.EndScrollView();
    }
}
