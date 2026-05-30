# Changelog

All notable changes to this project will be documented in this file.


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