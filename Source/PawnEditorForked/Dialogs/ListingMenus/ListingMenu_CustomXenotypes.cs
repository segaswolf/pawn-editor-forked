using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

/// <summary>
/// Listing of the user's saved custom xenotypes, with search, icons and tooltips.
/// Opened from the "Custom xenotypes..." button in ListingMenu_Xenotypes.
/// </summary>
public class ListingMenu_CustomXenotypes : Window, IMinWindowSize
{
    private const float MinHeight = 380f;
    private const float RowHeight = 34f;
    private const float RowGap = 2f;
    private const float IconWidth = 30f;
    private const float CloseButtonHeight = 40f;
    private const float SearchBarHeight = 30f;
    private const float SearchIconWidth = 24f;
    private const float ScrollbarWidth = 16f;
    private const int MaxGenesInTooltip = 8;

    public Vector2 MinWindowSize => new(InitialSize.x, MinHeight);

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
        // Has a search box: absorb input so typing doesn't trigger game hotkeys.
        absorbInputAroundWindow = true;
        preventCameraMotion = true;
        RefreshList();
    }

    public override Vector2 InitialSize => new(400f, 500f);

    private void RefreshList()
    {
        CharacterCardUtility.cachedCustomXenotypes = null; // force a reload from disk
        _cachedList = new List<CustomXenotype>();

        try
        {
            var cached = CharacterCardUtility.CustomXenotypes;
            if (cached != null && cached.Count > 0)
            {
                _cachedList = cached.ToList();
                return;
            }

            LoadFromDisk();
        }
        catch (System.Exception ex)
        {
            Log.Warning($"[Pawn Editor] RefreshList custom xenotypes: {ex.Message}");
        }
    }

    /// <summary>Fallback when vanilla's cache comes back empty.</summary>
    private void LoadFromDisk()
    {
        foreach (var filePath in GenFilePaths.AllCustomXenotypeFiles)
        {
            try
            {
                if (GameDataSaveLoader.TryLoadXenotype(filePath.FullName, out var xenotype))
                    _cachedList.Add(xenotype);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Pawn Editor] Failed to load custom xenotype from {filePath.Name}: {ex.Message}");
            }
        }
    }

    public override void DoWindowContents(Rect inRect)
    {
        // Resizable: keep a floor so the rows and search box don't collapse.
        windowRect.width = Mathf.Max(windowRect.width, InitialSize.x);
        windowRect.height = Mathf.Max(windowRect.height, MinHeight);

        DrawHeader(ref inRect);
        DrawCloseButton(ref inRect);
        DrawSearchBar(ref inRect);
        inRect.yMax -= 4f;

        var filtered = FilteredList();
        if (filtered.Count == 0) DrawEmptyMessage(inRect);
        else DrawList(inRect, filtered);
    }

    private static void DrawHeader(ref Rect inRect)
    {
        using (new TextBlock(GameFont.Medium))
            Widgets.Label(inRect.TakeTopPart(Text.LineHeight + 4f), "PawnEditor.CustomXenotypes".Translate());
        inRect.yMin += 8f;
    }

    private void DrawCloseButton(ref Rect inRect)
    {
        if (Widgets.ButtonText(inRect.TakeBottomPart(CloseButtonHeight).ContractedBy(4f), "Close".Translate()))
            Close();
    }

    private void DrawSearchBar(ref Rect inRect)
    {
        var searchRect = inRect.TakeBottomPart(SearchBarHeight).ContractedBy(2f);
        GUI.DrawTexture(searchRect.TakeLeftPart(SearchIconWidth).ContractedBy(4f), TexButton.Search);
        _searchText = Widgets.TextField(searchRect, _searchText);
        if (!_searchText.NullOrEmpty() && Widgets.ButtonImage(searchRect.TakeRightPart(20f).ContractedBy(2f), TexButton.CloseXSmall))
            _searchText = "";
    }

    private List<CustomXenotype> FilteredList() =>
        _searchText.NullOrEmpty()
            ? _cachedList
            : _cachedList.Where(x => x.name.IndexOf(_searchText, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();

    private void DrawEmptyMessage(Rect rect)
    {
        using (new TextBlock(TextAnchor.MiddleCenter))
        {
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(rect, _cachedList.Count == 0
                ? "PawnEditor.NoCustomXenotypes".Translate()
                : "PawnEditor.NoSearchResults".Translate());
            GUI.color = Color.white;
        }
    }

    private void DrawList(Rect outRect, List<CustomXenotype> xenotypes)
    {
        var viewRect = new Rect(0, 0, outRect.width - ScrollbarWidth, xenotypes.Count * RowHeight);
        Widgets.BeginScrollView(outRect, ref _scrollPos, viewRect);
        try
        {
            for (var i = 0; i < xenotypes.Count; i++)
            {
                var rowRect = new Rect(0, i * RowHeight, viewRect.width, RowHeight - RowGap);
                if (i % 2 == 1) Widgets.DrawLightHighlight(rowRect);
                DrawRow(rowRect, xenotypes[i]);
            }
        }
        finally
        {
            Widgets.EndScrollView();
        }
    }

    private void DrawRow(Rect rowRect, CustomXenotype custom)
    {
        DrawIcon(rowRect.TakeLeftPart(IconWidth).ContractedBy(3f), custom);
        rowRect.xMin += 4f;

        // Delete asks first, then removes the saved file.
        var deleteRect = rowRect.TakeRightPart(rowRect.height).ContractedBy(6f);
        if (Widgets.ButtonImage(deleteRect, TexButton.Delete)) ConfirmDelete(custom);
        TooltipHandler.TipRegion(deleteRect, "Delete".Translate());

        // Genes from mods no longer loaded: the game drops them silently, so the row says so.
        var check = CustomXenotypeCheck.For(custom.name);
        if (Mouse.IsOver(rowRect))
        {
            Widgets.DrawHighlight(rowRect);
            TooltipHandler.TipRegion(rowRect, RowTooltip(custom, CustomXenotypeCheck.Describe(check)));
        }

        if (Widgets.ButtonText(rowRect, RowLabel(custom, check), drawBackground: false, overrideTextAnchor: TextAnchor.MiddleLeft))
            Apply(custom, check);
    }

    private static void DrawIcon(Rect iconRect, CustomXenotype custom)
    {
        if (custom.IconDef?.Icon == null)
        {
            GUI.DrawTexture(iconRect, Widgets.PlaceholderIconTex);
            return;
        }

        GUI.color = XenotypeDef.IconColor;
        GUI.DrawTexture(iconRect, custom.IconDef.Icon);
        GUI.color = Color.white;
    }

    private static string RowTooltip(CustomXenotype custom, string missingNote)
    {
        var inheritableText = custom.inheritable
            ? "PawnEditor.Inheritable".Translate()
            : "PawnEditor.NotInheritable".Translate();
        var geneNames = string.Join(", ", custom.genes.Take(MaxGenesInTooltip).Select(g => g.LabelCap.ToString()));
        if (custom.genes.Count > MaxGenesInTooltip) geneNames += "...";

        var tip = custom.name.CapitalizeFirst() + "\n\n"
                  + inheritableText.Colorize(ColoredText.SubtleGrayColor)
                  + "\n" + ("Genes".Translate() + ": " + custom.genes.Count).Colorize(ColoredText.SubtleGrayColor)
                  + "\n" + geneNames.Colorize(ColoredText.SubtleGrayColor);
        if (missingNote != null) tip += "\n\n" + missingNote.Colorize(ColorLibrary.RedReadable);
        return tip;
    }

    private static string RowLabel(CustomXenotype custom, CustomXenotypeCheck.Report check)
    {
        var label = custom.name.CapitalizeFirst();
        if (check.HasMissingGenes)
            label += " " + "PawnEditor.MissingGenesShort".Translate(check.MissingGenes.Count).Colorize(ColorLibrary.RedReadable);
        return label;
    }

    private void Apply(CustomXenotype custom, CustomXenotypeCheck.Report check)
    {
        TabWorker_Bio_Humanlike.SetXenotype(_pawn, custom);
        TabWorker_Bio_Humanlike.RecacheGraphics(_pawn);
        PawnEditor.Notify_PointsUsed();
        if (check.HasMissingGenes)
            Messages.Message("PawnEditor.AppliedWithMissingGenes".Translate(custom.name, check.MissingGenes.Count,
                string.Join(", ", check.MissingGenes)), MessageTypeDefOf.CautionInput, false);
        Close();
    }

    private void ConfirmDelete(CustomXenotype xenotype) =>
        Find.WindowStack.Add(new Dialog_Confirm("ConfirmDelete".Translate(xenotype.name.CapitalizeFirst()), "ConfirmDeleteXenotype",
            () =>
            {
                var path = GenFilePaths.AbsFilePathForXenotype(xenotype.name);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                RefreshList();
            }, true));
}
