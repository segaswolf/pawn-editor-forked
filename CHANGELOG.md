# Changelog

All notable changes to this project will be documented in this file.

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