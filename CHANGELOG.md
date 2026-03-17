# Changelog

All notable changes to this project will be documented in this file.

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