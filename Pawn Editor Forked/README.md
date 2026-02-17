# Pawn Editor Forked (Community Fork)

Maintained fork of Pawn Editor for RimWorld 1.6.

## Purpose

This fork keeps Pawn Editor usable in large modlists and ongoing colonies.
It focuses on stability fixes, compatibility, and predictable in-game behavior.

## Latest change (v2.0.2)

- In-game top-right Pawn Editor button now appears only with Dev Mode + God Mode.
- In-game `Edit` gizmo appears only with Dev Mode + God Mode.
- Social tab defaults to showing all relations on first open.

This avoids exposing the edit gizmo during normal gameplay while keeping quick access for debug/admin workflows.

## Package ID

- `segaswolf.pawneditor.fork`

## Load order

- Place after Harmony.
- Use this fork instead of the original Pawn Editor package.

## Notes for Workshop release

- Keep the same package ID: `segaswolf.pawneditor.fork`
- Upload folder: this mod root (`About`, `Defs`, `Languages`, `Textures`, `1.6`, optional `Source`)
