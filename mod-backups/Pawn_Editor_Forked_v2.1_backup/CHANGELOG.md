# Changelog

## v2.1 — 2026-02-17

All fixes below applied to the community fork. Original mod version was 1.2.0.

### Critical fixes

- **#001 IntField reset** (`UIUtility.cs`): `intBuff` now initializes to current `value` instead of `-1`. Added stable `GUI.SetNextControlName()` so focus detection works across RW versions. Fields no longer snap to minimum while typing.

- **#002 Favorite color Def mutation** (`LeftList.cs`): Color picker callback now finds the nearest `ColorDef` by RGB distance and assigns it as a reference, instead of mutating the shared `ColorDef.color` property. Prevents one pawn's color change from affecting all pawns.

- **#004 Missing body part kills pawn** (`ListingMenu_Hediffs.cs`): `Hediff_MissingPart` no longer defaults to `corePart` (torso). Injuries can still default to corePart safely. Missing parts either use the first non-core part or return a failure message asking the user to select one.

### High-severity fixes

- **#006 Edit button requires Dev Mode** (`PawnEditorMod.cs`): Removed `DebugSettings.ShowDevGizmos` gate from `AddEditButton`. The gizmo now always appears when `ShowOpenButton` is enabled in settings.

- **#016 Dev toolbar disappears** (`PawnEditorMod.cs`): Replaced fragile IL transpiler (`AddDevButton`) with a safe postfix (`AddDevButtonPostfix`). The toolbar can no longer be corrupted by RimWorld version differences in the IL layout.

- **#019 Duplicate pawn IDs** (`SaveLoadPatches.cs`): `ReassignLoadID` now handles both `"loadID"` and `"id"/"thingIDNumber"` labels, with a guard to only remap Thing IDs when `curParent is Thing`. Prevents stale IDs from prior saves persisting after load.

### Medium-severity fixes

- **#007 Child backstory missing** (`TopRightButtons.cs`): `SetDevStage()` now generates a random Adulthood backstory when transitioning child→adult (if none exists), and clears Adulthood when going back to child.

- **#008 Copy/Paste item loss** (`CopyPaste.cs`): `Clone()` now copies `stackCount`, `CompQuality`, and `CompColorable`. Inventory iteration uses a snapshot list to avoid collection-modified errors. Items no longer lose stack size, masterwork quality, or dye colors when pasted.

- **#009 Scenario items lost** (`StartingThingsManager.cs`): `ProcessScenario()` now takes a snapshot of `AllParts` before iterating. Wrapped in try-catch that calls `RestoreScenario()` on failure. Scenario items are no longer permanently lost if an error occurs mid-processing.

- **#010 Xenotype NullReferenceException** (`TopRightButtons.cs`): `ClearXenotype()` and `SetXenotype()` now check for null `pawn.genes`, null gene lookups, and snapshot gene lists before removal loops. No more NRE when working with custom xenotypes.

- **#012 Invisible weapons after load** (`TabWorker_Gear.cs`): `GetSaveLoadItems()` now has `OnLoad` callbacks that call `RecacheGraphics()` and `ClearCaches()`. Gear is visually refreshed after loading a preset.

- **#018 Mechs go rogue** (`LeftPanel.cs`): `AddPawn()` auto-assigns the first available Mechanitor as Overseer for mechs added in pregame. Shows a warning if no Mechanitor is available.

### Infrastructure

- `packageId` changed to `segaswolf.pawneditor.fork` (no conflict with original)
- Harmony ID changed to `segaswolf.pawneditor.fork`
- Added translation keys: `PawnEditor.SelectBodyPart`, `PawnEditor.MechNoOverseer`
- DLL compiled against RimWorld 1.6.4489-beta references

### Known issues not yet addressed

| # | Bug | Reason |
|---|-----|--------|
| 005 | Relationship tab shows only 2 colonists | Vanilla `SocialCardUtility` limitation in pregame |
| 011 | Tabs don't refresh when switching pawns | Needs runtime investigation |
| 013 | Gene list stripped/incomplete | Needs runtime investigation |
| 014 | Pawn names don't allow spaces | Vanilla Unity IMGUI limitation |
| 015 | RW 1.6 API incompatibilities (partial) | Needs 1.6 DLLs for full compilation testing |
| 017 | Social stats reset on game launch | Needs runtime investigation |
