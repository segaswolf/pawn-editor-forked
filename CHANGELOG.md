# Changelog

All notable changes to this project will be documented in this file.


## [v3.2.3] - unreleased — Layout layer

### Added
- **`Utils/Layout.cs`**: rect arithmetic that cannot produce an invalid rect. `TryTakeTop` /
  `TryTakeLeft` return false instead of handing back a row that doesn't fit; `Columns` splits with
  weights and minimum widths and always fits inside the parent; `Proportional` is the grow-with-bounds
  pattern used in several places; `ScrollViewWidth` only subtracts the scrollbar when the content
  actually overflows.
  Motivation: `Rect.TakeTopPart` returns a full-height row even with no room left, which is how the
  Betrayer checkbox ended up drawn (and clickable) on top of the bottom buttons. The class computes
  layout as data so it can be checked before anything is painted.

- **Compatibility self-check at startup**: one log line listing which compat layers hooked in, and a
  warning naming any whose mod is installed but whose API could not be resolved. Our compat layers
  reach into other mods by reflection, so an upstream rename used to fail silently and only surface
  weeks later as a player report.
- **"Betrayal in" row** on the Bio tab for pawns flagged as betrayers (Trauma & Integrity). The mod
  stores *when* the betrayal fires, rolled at random up to ~600 in-game days, but nothing showed it.
  Now it is visible and editable.

- **Test project** (`Source/PawnEditor.Tests`, not shipped): 19 NUnit tests covering `Layout`. Runs
  with `dotnet test Source\PawnEditor.Tests` in under two seconds, with no game required — it links
  the source file rather than referencing the mod assembly. It caught a real bug on its first run
  (see below), in code written the day before and already applied to the Bio tab.

### Fixed
- **`Layout.Columns` pushed columns outside their parent** when the rect was narrower than the gaps
  alone required. Only the column widths were being shrunk, so each gap kept nudging the next column
  further right and the last one landed outside — the exact failure the class exists to prevent. The
  spacing now shrinks too.

### Changed
- Trauma & Integrity rows and the Bio tab's three-column split now go through `Layout`, replacing the
  hand-rolled arithmetic that produced both of the overlap bugs fixed in v3.2.1.
- Missing option icons in the appearance editor are only reported after they stay missing for 180
  draws. Mods that load textures on a background thread (Faster Game Loading and similar) hand back a
  null texture that resolves moments later, so reporting on the first miss named other authors' mods
  as broken when their art was merely still loading.

See `Dev Notes/Pawn Editor Forked/LEARNINGS_UI_LAYOUT.md` for the research this came from.


## [v3.2.1] - 2026-09-18 — Hotfix

All of these came from player reports. Not fully verified in-game at release time.

### Fixed
- **Forced apparel was silently un-forced.** Selecting a worn item in the Gear tab detaches it so the
  edit preview can render without it, then re-wears it. The round trip preserved `locked` but not the
  forced flag, and `Pawn_ApparelTracker.Remove` clears forced via `forcedHandler.SetForced(ap, false)`
  — so merely clicking an item made pawns swap that apparel out later on their own.
- **"Show hidden hediffs" did nothing.** The checkbox set `HealthCardUtility.showAllHediffs` correctly,
  but `UITable.CheckRecache` only rebuilds rows when the target pawn changes, so the displayed list
  stayed cached. Toggling now invalidates the table cache.
- **"Customize face" opened a blank panel in pregame.** NL Facial Animation's `NL_SelectPartWindow`
  calls `Find.Selector` every frame, and RimWorld implements that as `((UIRoot_Play)UIRoot).mapUI` — a
  hard cast that throws `InvalidCastException` outside a running game. The button is no longer offered
  where it cannot work; the built-in Facial Animation tab covers pregame editing.
- **Trauma & Integrity rows drew past the bottom of their panel.** `Rect.TakeTopPart` returns a
  full-height row even with no room left, so the Betrayer checkbox — and its tooltip region — ended up
  over the bottom buttons. Since a checkbox reacts to a click anywhere inside it, clicking there could
  toggle Betrayer unnoticed. Rows now stop at the panel edge.
- **Hardened the editor backdrop** (possible cause of a reported crash when opening the editor mid-game,
  unconfirmed). It is excluded from the resizable-windows patch via the new `IExcludeFromResizePatch`
  marker, no longer reassigns its own `windowRect` during `DoWindowContents`, and closes itself quietly
  instead of erroring if it fails to draw.

### Added
- **"Clear betrayer flag on all pawns"** under Quick actions on the Health tab (only with Trauma &
  Integrity installed). Reports how many pawns are flagged and asks for confirmation. Offered as an
  explicit action rather than an automatic cleanup: nothing in the data distinguishes an accidental
  flag from one the player set deliberately or one the mod set through its own gameplay.


## [v3.2] - 2026-09-11 — Facial Animation, Gear, Royalty & Ferny's mods

Most of the new editor sections come from a pull request by **Lucius127**.

> There is no v3.1 release. That work was never published on its own — the cycle kept being
> fix, test, another report arrives, repeat — so it shipped as part of v3.2. The jump from
> v3.0 to v3.2 in this file is intentional.

### Added
- **Facial Animation tab** inside "Edit appearance" (NL Facial Animation): per-part editing for eyes,
  brows, lids, mouth, skin and head, plus eye colour and second eye colour.
- **Gear window** rebuilt: large pawn preview, apparel with condition and per-item editing, equipment,
  possessions, and a material picker.
- **Royalty tab** with three pages: titles (faction, title, honor, successors), permits (cost, minimum
  title, Grant), and psycasts (psylink, psyfocus, entropy, paths, meditation foci).
- **VPE psycast state saved with blueprints** (level, experience, unlocked paths), not only on duplicate.
  Previously this lived inside `WriteRoyalTitles`, which early-returns for pawns with no royal title.
- **Ferny's mods**: Trauma & Integrity editable from the Bio tab (with State and Betrayer), a
  Development tab with searchable Wants and Quirks, and Progression: Education proficiency handling.
- **Progression: Education proficiencies** treated as tiers of a track (`ProficiencyDef.tiers` ->
  `ProficiencyTierDef.traitDef`): picking "fluent speech" removes "mute", a separate "Traits (including
  forced ones)" reroll swaps a pawn's tier within its own track, and those traits are excluded from the
  normal trait reroll — they have `commonality 0`, so the generator could never restore them.
- **Appearance editor**: drag-to-rotate and zoom on the preview, responsive layout that reflows on
  resize, colour palette in a side column on wide windows, Source/Category filters on the xenotype
  picker.

### Changed
- The appearance editor's drag-bar was removed; it conflicted with drag-to-rotate. Panel widths are now
  proportional with clamped minimum and maximum.
- The editor window opens slightly larger, and the Groups column is wider when Ferny's mods are present.
- The editor dims what is behind it, so vanilla's character page stops competing for attention.

### Fixed
- **Randomizing traits no longer wipes them.** Traits were removed and only re-added on growth
  birthdays, so a pawn younger than the first growth moment — or an unlucky roll with heavy trait mods
  — could end up with none. Added an unbounded-by-birthday second pass (capped at 20 attempts) and a
  restore of the originals if the reroll produces nothing.
- **Saving no longer leaks global Harmony patches.** `SaveLoadUtility` installs global patches
  (`Scribe_Values.Look`, `Thing`/`Pawn.ExposeData`, `PostLoadIniter`) while writing. The save path had
  no try/finally, so an exception mid-save left them installed for the rest of the session — after
  which `ReassignLoadID` rewrote IDs during unrelated loads. The load path already had this guard.
- **Missing tails, ears and other modded appearance genes now appear.** The cosmetic-gene filter only
  admitted genes that were 100% cosmetic, hiding anything with a minor mechanical side effect.
- **Typing in a search box no longer fires game hotkeys** (`absorbInputAroundWindow` /
  `preventCameraMotion` on listing menus; `Dialog_EditItem` swallows key events while a field has focus).
- **The text caret is visible in the name field.** It is drawn from the global
  `GUI.skin.settings.cursorColor` / `cursorFlashSpeed`, which another UI mod can leave invisible; the
  name fields now force a visible caret and restore the previous values afterwards.
- **The window can no longer be squashed into an unusable state.** `IMinWindowSize` feeds
  `WindowResizer.minWindowSize` directly, so the floor is enforced before the new rect is committed
  rather than corrected a frame later. No maximum.
- **Bottom buttons no longer overlap.** They are laid out as one evenly spaced, centred group with
  clamped slot widths, instead of mixing centred and right-anchored anchoring.
- **Random faction selection could never pick the last faction** — `Rand.Range(int, int)` is
  max-exclusive and the code passed `Count - 1`. Replaced with `RandomElement()`.
- **The repeat-randomize button** is a normal size again and re-runs the correct option in every
  language (tracked by index instead of by matching the label text).
- **Animal training list scrolls.** Every `TrainableDef` was drawn directly into the panel with no
  scroll view, so with modded trainables the entries at the bottom fell outside the window.
- **Broken modded textures** draw a placeholder and log one line naming the mod, instead of flooding
  the log every frame. `TexPawnEditor` no longer throws during static construction on a bad body-type
  icon.

### Known issues
- Changing a xenotype's head type can revert to the human shape.
- Pawn IDs are not preserved across saves, so couple compatibility can differ when loaded elsewhere.
- Work in progress, not verified: Gradient Hair support, Anthrosonae "Change fur", RJW sexuality editing.


## [v3.0] - 2026-07-13 — Portable Colonies, Performance & Quality of Life

### Added — Portable colony & pawn save/load
- New **Save colony pawns** / **Load colony pawns**: store a colony as portable pawn blueprints (no map, no research, no world state), replacing the old full-map save that was fragile across game versions and modlists.
- Only the **pawns currently on the map** are saved (a note in the save dialog makes this explicit); anyone away in a caravan or traveling is intentionally left out.
- **Category selection** on save and load: any combination of Humanlike / Animals / Mechs.
- **Load modes**: *Load as new (clones)* or *Replace matching pawns by ID*, chosen in a single loader window (pick colony + mode + categories in one place). Replace is idempotent — pawns loaded from a save are stamped with their origin, so reloading a colony replaces those pawns instead of piling up duplicates.
- **Overwrite vs Merge** when re-saving over an existing colony: *Overwrite* fully replaces the save (the folder is cleared first, so no stale pawns can survive); *Merge* updates only the selected categories and keeps the rest (re-saving just "Animals" no longer wipes the saved humans).

### Added — Richer per-pawn data
- **Animals**: training progress and the assigned master (handler) are saved and restored.
- **Mechs**: overseer link, control-group assignment, **Mechanoid Upgrades** (gogatio) pieces, and mechanitor/mechlink status are all saved and re-applied on load.

### Changed — Reference-safe loading
- Cross-pawn links (bonds, master, overseer) are remapped by **ThingID**, so loading into a game that still has the originals links clone-to-clone — never back to the originals — even with identical-looking pawns.
- **Safeguards**: never forces polygamy against a monogamous ideology (opt-in setting, off by default), never gives an animal a second master, and references that can't be resolved (e.g. a partner left out of the save) drop cleanly with a log note instead of leaving broken links.
- **Transparency**: hediffs that can't be duplicated are listed in the log instead of being silently dropped.
- Removed the legacy map-based (Scribe) colony save/load.

### Added — Windows & quality of life
- Every editor window can be **resized** (drag the bottom-right corner) and **moved** (drag it), covering the main editor, appearance editor, all pick lists and dialogs.
- New **"Remember window position and size"** setting (off by default). Off: windows always reopen centered, so one left off-screen is always recoverable. On: they reopen where and how you left them.

### Performance
- **Appearance editor**: the hair, beard, tattoo, body/head and xenotype pickers no longer rebuild and re-filter their whole option list every frame (cached, rebuilt only on tab/filter/pawn change), and icon and gene grids now draw only the rows visible in the scroll viewport. No more framerate drop with 1000+ hairs or a large modded gene pool.
- **Load freeze fixed**: loading no longer stalls on a forced garbage collection (~4.5s → ~250ms).
- **Portrait fetch** no longer allocates every frame during normal play.
- **Proficiency list caching** (Life Lessons): per-call allocation dropped from ~152 KB to ~20 KB.
- Portrait texture churn fixed; graphics refresh centralized. Added an internal profiler for diagnostics.

### Fixed
- **"Randomize all — keep xenotype"** no longer loses genes and name on baseliner-named xenotypes (e.g. custom "Veldrak"-style xenotypes); endogenes, xenogenes, xenotype name and icon are preserved.
- **Mechanitor "phantom" bandwidth**: cloning/loading a pawn no longer binds the clone to the *original's* mech.
- **Black screen on load** (portrait cache was being fully cleared instead of marked dirty).
- Surgical fix for a `PioneeringComp` null-reference on certain pawns.
- Proficiency list now refreshes correctly after add/remove, with a sanitizer for malformed data.
- Trait **Mute** handling and a Refill/Fulfillment desync.
- Structural gene classifier for more reliable cosmetic/endogene/xenogene handling.


## [v2.4.6] - 2026-05-28

### Fixed — Life Lessons Compatibility
- Compat layer no longer fails to initialize on load (was throwing during startup, leaving the proficiency feature non-functional)
- Proficiency editor no longer opens to a black/empty window
- Removed the exception spam (dozens of identical null-reference errors per frame) that was firing in the background while the proficiency window was open
- Adding or removing a proficiency now actually recalculates the pawn's stat and skill modifiers (was silently failing — the pawn would gain the proficiency on paper but get none of its benefits)
- Defensive null-filtering when reading proficiency lists, so unexpected nulls don't crash the UI

### Changed — Proficiency Editor Redesigned
- Rebuilt as a two-column trade-style window: "Can learn" on the left, "Known" on the right
- Click a proficiency to move it across — learning grants prerequisites automatically, removing it also cleans up descendant proficiencies that would otherwise be left with broken prereqs
- Window stays open after each change so you can configure several proficiencies in a single pass (the previous version closed after every add)
- Search box filters both columns at once
- Counters in the column headers show totals at a glance
- Tooltips include the proficiency's category and a click-action hint

### Added — Compatibility Layer
- New compat methods: `RemoveProficiency`, `CanLearn`, `RefreshModifiers`
- All reflection signatures re-verified against the actual Life Lessons assembly to prevent silent mismatches in the future


## [v2.4.4] / [v2.4.5] - 2026-05-15

### Fixed
- **Backstory transitions**: Changing a pawn's age from 18 to 13 now properly removes the adult backstory. Going from 13 to 18 generates a new contextual adult backstory matching the childhood. The age field is no longer locked to the current developmental stage.
- **Item stacking**: Adding the same item multiple times now correctly increments the stack count instead of creating separate entries. Also fixes the point system not properly tracking duplicate items.
- **DeepSave fix**: Duplicating pawns with abilities no longer causes save corruption errors.

### Performance
- **Search box FPS fix**: Fixed a major performance issue where clicking the search box with large modlists (600+ mods) would drop the framerate to ~5 FPS. The item lookup was using an O(n) linear search — now uses O(1) hash lookup.

### Code Quality
- Extracted cosmetic gene discovery and backstory transition logic into dedicated utility classes
- Split large methods into focused, well-named smaller methods
- Added XML documentation to all public classes and methods across 11 files
- Removed leftover debug logging from previous development sprints
- Replaced magic numbers with named constants
- Labels fixed to consistent sentence case throughout the editor

### Notes
- These code-quality changes don't affect gameplay but make the codebase much easier to understand for anyone wanting to contribute or create compatibility patches.


## [v2.4.3] - 2026-04-24

### Changed — Appearance Editor Cosmetic Genes Rewrite
- The Xenotype tab in the appearance editor has been completely rewritten. Previously it had hardcoded gene categories that missed most modded cosmetic genes — tails, ears, horns, heads, bodies, and more were invisible.
- **All cosmetic genes visible**: tails, ears, horns, tusks, antennae, heads, bodies, fur, eyes, hair color, skin color, and everything else your mods add
- **Dynamic category discovery**: scans all loaded `GeneCategoryDef`s automatically — no hardcoded lists, no manual updates needed when you add new mods
- **No more duplicated genes**: VREA android copies and Astrogene archite variants are filtered automatically
- **Instant visual feedback**: selecting a gene now updates the pawn portrait immediately
- **Baseliners unlocked**: Baseliner pawns can now access all cosmetic genes without needing "Ignore xenotype restrictions"
- Works with: Alpha Genes, Vanilla Races Expanded (all), Cyanobot's Genes, Det's Xenotypes, and any mod that defines cosmetic `GeneCategoryDef`s

### Added — Blueprint & Duplication Data
- **VAspirE Aspirations**: now saved/loaded in blueprints and copied during duplication
- **VSE Expertise**: expertise type, level, and XP preserved in blueprints and duplication
- **Trait skill bonuses**: adding/removing traits now correctly adjusts skill levels
- **DeepSave fix**: duplicating pawns with abilities no longer causes save errors


## [v2.4.2] - 2026-04-07

### Changed — Xenotype Selection UI
- Replaced the massive FloatMenu xenotype dropdown with a searchable listing window
- Xenotype listing now shows icons, tooltips with description, gene count, and inheritable status
- Custom (user-created) xenotypes appear in a dedicated section below the listing with forced cache load
- Added "Xenotype editor..." button inside the listing for quick access
- Added confirmation dialog when changing xenotype: warns about gene reset, user decides
- HAR race restrictions are applied automatically to filter incompatible xenotypes

### Added — VRE Android Compatibility
- Restored "Android editor..." button that was lost when FloatMenu was replaced
- VREAndroidCompat.cs detects VRE Androids and opens Window_CreateAndroidXenotype via reflection

### Added — VAspirE Life Stage Safeguards
- Changing a pawn from adult to child/baby now clears all aspirations (children cannot have them)
- Changing a pawn from child/baby to adult generates fresh random aspirations via SetInitialLevel
- Warning dialog now lists aspiration removal when changing to a non-adult stage
- CompleteSilent mode: completing aspirations in pre-colony no longer triggers growth moment letters

### Fixed
- Fixed pawns spawning inside walls when duplicating or loading blueprints in-game
- Fixed ListingMenu_PawnKindDef crash when modded PawnKindDefs have empty lifeStages or null bodyGraphicData
- Fixed custom xenotype tooltip showing garbled text ("Nòt ìnhêrìtàblê") due to missing translation key
- Fixed Xenotype Editor crashing in-game with NullRef (now uses index -1 for post-colony)
- Fixed aspirations not regenerating when changing pawn from child to adult (was calling CheckCompletion instead of SetInitialLevel)

## [v2.4.1] - 2026-04-01

### Changed — VAspirE (Vanilla Aspirations Expanded) Compatibility
- Reworked the "Edit Aspirations" menu into a proper multi-selection editor
- Aspirations are now displayed in a searchable alphabetical list
- Current aspirations are preselected automatically when opening the menu
- Added 4-5 aspiration selection rules to match VAspirE's intended design
- Added live selected counter in the aspiration editor
- Added OK/Cancel confirmation flow with rollback-safe editing

### Notes
- This update improves the pre-colony aspiration editing workflow introduced in v2.4.0
- Future improvements may include filtering by content source (Core, DLCs, Mods)

## [v2.4.0] - 2026-03-30

### Added — VAspirE (Vanilla Aspirations Expanded) Compatibility
- Aspiration icons in the Needs tab are now clickable: click to mark as completed, click again to revert
- Fulfillment need bar no longer shows +/- buttons (they had no effect since the system recalculates based on completed aspirations)
- New "Edit Aspirations" button in the Needs tab bottom panel — opens a listing to add aspirations from the full pool of valid aspirations for the pawn
- Quick Actions menu now includes "Complete all aspirations" and "Reset all aspirations" options
- Full reflection-based compatibility layer — no hard dependency on VAspirE

### Fixed
- Fixed static constructor crash in ListingMenu_Items when a ThingStyle had null StyleDef (ThingStyles dictionary null key error)
- Fixed static constructor crash in ListingMenu_PawnKindDef when a modded PawnKindDef had empty lifeStages or null bodyGraphicData
- Fixed inconsistent property/field access (thingDefStyle.styleDef vs .StyleDef) causing silent mismatches in style lookups

### Notes
- VAspirE integration is Phase 1 (pre-colony editor). In-game editing will come in a future update

## [v2.3.1] - 2026-03-28

### Fixed
- Starting items no longer disappear after editing pawns in pregame (idempotency guard + GoToMainMenu hook)
- Passions and skill levels preserved when changing backstory (save/restore around GenerateSkills)
- Hotkey can be fully disabled via right-click (sets to None); Escape cancels picker

## [v2.3.0] - 2026-03-28

### Major: Blueprint & Duplication Overhaul (Tracker Transplant)
- Complete rewrite of blueprint save/load and pawn duplication
- New system automatically preserves all mod data without per-mod patches
- VPE Psycasts: paths, unlocked nodes, XP, and level fully preserved
- Mechlink, Cyberlink, and all Hediff_Level types correctly duplicated
- 35+ mod components automatically preserved via reflection
- Duplication now uses the same system as blueprints

### Fixed
- Passion sanitizer: mods like Alpha Skills with passion values 3+ no longer reset to None
- Ideo fallback: fixed crash when loading blueprints for pawns whose faction ideo couldn't resolve
- Action bars: fixed missing gizmo bar on loaded/duplicated pawns
- Discard crash: fixed NullReferenceException when replacing a pawn via blueprint
- VRE Android: energy need correctly preserved during duplication
- TacticalGroups: compatibility patches applied via finalizer

### Known Issues
- VAspirE: Need_Fulfillment may crash during load — investigating
- Blueprint overwrite: rewriting an existing file may cause issues — save as new file recommended

## [v3d10] - 2026-03-14

### Added
- Added a warning when changing a pawn's life stage through the age combo box.

### Changed
- Reorganized the blueprint save/load code to make future maintenance and updates easier.
- Remaining points now behave like an actual budget instead of reflecting colony value in a confusing way.

### Fixed
- Humanlike pawns now stay within the correct age range for their current life stage.
- Incompatible equipped gear is no longer lost when changing a pawn's life stage through the mod.
- Installed prosthetics now remain in place when changing life stage through the mod.

### Notes
- Androids continue to follow their own race-specific logic and are not forced into regular biological life stage limits.

### Work in Progress
- Prosthetics with extra modules are still under review for duplicate/blueprint parity.
- Android energy restore on duplicate/blueprint load is still being worked on.
- The backstory issue is still under investigation.