# Pawn Editor — Community Fork

**Maintained by:** Segas Wolf  
**Original authors:** ISOR3X, legodude17, Taranchuk, TreadTheDawnGames, mycroftjr, Inglix, fofajardo  
**RimWorld:** 1.5 / 1.6  
**Requires:** [Harmony](https://steamcommunity.com/workshop/filedetails/?id=2009463077)

---

## What is this?

A community fork of Pawn Editor with stability and bug fixes for RimWorld 1.6. The original mod is no longer actively maintained.

## Fixes included (v2.1)

| # | Bug | Severity |
|---|-----|----------|
| 001 | Age/numeric fields reset to minimum while typing | Critical |
| 002 | Favorite color corrupts all pawns sharing a ColorDef | Critical |
| 004 | Adding "Missing Part" without selecting body part kills the pawn | Critical |
| 006 | In-game "Edit" button only visible with Dev Mode | High |
| 007 | Child to Adult transition leaves Adulthood backstory null | Medium |
| 008 | Copy/Paste loses stack count, quality, and dye color | Medium |
| 009 | Scenario starting items permanently lost on error | Medium |
| 010 | Custom xenotype operations throw NullReferenceException | Medium |
| 012 | Weapons/apparel invisible after loading a gear preset | Medium |
| 016 | Dev toolbar disappears when mod is active (transpiler failure) | High |
| 018 | Mechanitor mechs go rogue on scenario start | Medium |
| 019 | Duplicate pawn IDs when loading presets multiple times | High |

## Installation

1. Remove or disable the original Pawn Editor mod
2. Copy this folder to your RimWorld `Mods/` directory (or symlink from Git)
3. Enable "Pawn Editor" in the mod list — uses `packageId: segaswolf.pawneditor.fork`

## Building from source

```bash
cd Source/PawnEditorForked
dotnet restore
dotnet build -c Release
# Output -> ../../1.6/Assemblies/PawnEditor.dll
```

Requires .NET SDK with net472 targeting pack. Uses NuGet packages for RimWorld references (`Krafs.Rimworld.Ref`).
