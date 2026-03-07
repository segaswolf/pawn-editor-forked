# GitHub Issue Notes — v3d7 fixes pending confirmation

Estos son los dos issues para postear/comentar en GitHub, pidiendo a los usuarios que confirmen si el fix resuelve su problema.

---

## Issue: Faction goes leaderless after blueprint replace

**Título sugerido:**
`Bug: Replacing an NPC faction leader via blueprint leaves faction leaderless`

**Cuerpo:**

When using "Replace pawn" with a blueprint on a pawn who is the leader of a non-player faction, the faction loses its leader reference after the operation.

**Steps to reproduce:**
1. Open Pawn Editor in-game
2. Select an NPC pawn who is a faction leader (visible in the Social tab or world map)
3. Use Load → Replace selected pawn with any blueprint
4. Check the faction — it will be leaderless

**Expected:** The new pawn should inherit the faction leader role.

**Status:** Fix applied in v3d7 (`PawnEditorUI.cs`). The faction's `leader` field is now restored to the new pawn after replacement.

**Asking users to confirm:** If you've experienced this, please reply whether this is resolved after updating to v3d7.

---

## Issue: VE Hussar Giant gene visual offset not applied after blueprint load

**Título sugerido:**
`Bug: Giant gene draw offset missing on loaded pawns (VE Hussar / VFE-Deserters)`

**Cuerpo:**

When loading a pawn from a blueprint who has the Giant gene (from Vanilla Factions Expanded - Hussar or similar), the visual body size offset is not applied correctly. The pawn appears at the wrong scale or position until you either:
- Reload the save, or
- Re-apply the xenotype manually

**Root cause:** Gene `PostAdd()` runs during blueprint load before the pawn is spawned, so the renderer (`pawn.Drawer`) is null and cannot apply visual offsets. The portrait cache and texture atlas were not being invalidated post-spawn.

**Status:** Fix applied in v3d7 (`LeftPanel.cs`, `PawnEditorUI.cs`). Portrait and texture atlas caches are now explicitly invalidated after the pawn is spawned on the map.

**Asking users to confirm:** If you've seen this with Giant gene or any other gene that applies a body draw offset, please reply whether this is resolved after updating to v3d7. Also let us know if other genes (e.g. from other VE mods) show the same behavior.

---

*Notas para Segas: puedes postear estos como issues nuevos o como comentarios en threads existentes si ya hay reportes de usuarios. La idea es que confirmen antes de marcarlos como cerrados en el milestone de v3d7.*
