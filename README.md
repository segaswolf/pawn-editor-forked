# 🐾 Pawn Editor Forked

An unofficial community fork of [Pawn Editor](https://steamcommunity.com/sharedfiles/filedetails/?id=3219801790) for RimWorld 1.6, maintained by [Segas Wolf](https://github.com/segaswolf).

> ⚠️ **Use either this fork or the original — not both at the same time.**

---

## 📋 What is this?

The original Pawn Editor is an incredibly useful tool for editing colonists, but had stability issues with large modlists and certain DLC interactions. This fork fixes those problems, improves compatibility with popular mods, and keeps the editor running smoothly — even with 1000+ mods loaded.

All credit for the original mod belongs to its authors. See [Credits](#-credits--attribution) below.

---

## 📦 Installation

1. Remove the original Pawn Editor mod
2. Subscribe on [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3667931113)
3. Place it **after Harmony** in your load order

---

## 🐛 Bug Reports & Feedback

→ Use the [GitHub Issues](https://github.com/segaswolf/pawn-editor-forked/issues) or the [Steam Bug Reports thread](https://steamcommunity.com/workshop/filedetails/discussion/3667931113/). Include your mod list and `Player.log` — reports with those get fixed faster.

→ For active development status, check the [Steam Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3667931113) WIP thread first.

---

## ⚠️ Work in Progress

This fork is actively being developed. Many critical bugs have been fixed, but some areas are still being worked on:

| Area | Status |
|------|--------|
| Blueprint save/load (individual pawns) | ✅ v2.3.0: New Tracker Transplant system preserves all mod data automatically |
| Pawn duplication | ✅ v2.3.0: Uses same system as blueprints — full mod data preservation |
| Starting Preset save/load | 🚧 Still uses old system — may fail with complex modlists |
| HAR race visuals (tails, custom bodies) | 🚧 May not fully restore |

---

## ✨ What's been fixed

### 🔧 Stability
- Fixed crashes when duplicating pawns (`Collection modified`, `Sequence no matching element`)
- Fixed crash in Bio tab when traits change during render
- Fixed startup crashes with TacticalGroups and other mods
- Fixed duplicate pawn ID conflicts that corrupted save files
- Fixed null reference exceptions with xenotypes and gene lists
- Fixed `ListingMenu_Items` crash caused by mods with null style entries
- Starting Preset load no longer crashes the game on failure
- All silent `catch {}` blocks now log warnings — no more invisible failures

### 🧬 Pawn Duplication (v2.3.0 — Tracker Transplant)
- **v2.3.0**: Duplication now uses the same Tracker Transplant system as blueprints — all mod data preserved automatically
- VPE Psycasts, Mechlink, Cyberlink, and all mod hediffs duplicated correctly
- Clones keep the same name as the original (vanilla Obelisk behavior)
- Clones correctly copy gender, appearance, hair, skin color, and melanin
- Clones copy clothing, armor, and weapons with quality, color, and HP
- Ideology certainty is now preserved accurately
- Biological and chronological age no longer get swapped
- Social memories and opinion relations copy correctly (uses RimWorld 1.6 `ISocialThought` API)
- Legacy field-by-field copy retained as automatic fallback

### 💾 Blueprint Save/Load (v2.3.0 — Tracker Transplant)
- **Complete rewrite**: Blueprints now automatically preserve ALL mod data without per-mod patches
- Uses RimWorld's own save system (Scribe) to capture 100% of pawn state, then transplants data objects directly into a fresh pawn
- VPE Psycasts: paths, unlocked nodes, XP, and psycaster level fully preserved
- Mechlink, Cyberlink, and all Hediff_Level types correctly duplicated
- Saves everything: bio, traits, skills, genes, hediffs, abilities, apparel, equipment, relations, work priorities, inventory, royal titles, records
- Missing mods/DLCs are gracefully skipped when loading — no more crashes
- Blueprints can be shared between different modlists
- 35+ mod components automatically preserved via reflection

### 🎨 Appearance
- Hair, head type, body type, skin color, and fur properly saved and restored
- Melanin values preserved for accurate skin tones
- Style elements (beard, tattoos) correctly handled
- Genes load before appearance to prevent override conflicts

### 🛡️ Mod Compatibility

| Mod | Status |
|-----|--------|
| Vanilla Psycasts Expanded | ✅ Paths, nodes, XP, level — fully preserved in blueprints and duplicates |
| Facial Animations | ✅ Face type, eye color, brow, lid, mouth, skin, head controllers all copy correctly |
| TacticalGroups | ✅ Harmony finalizers prevent colonist bar crashes |
| Vanilla Skills Expanded | ✅ Passion system compatible |
| VE Hussar / Giant gene | ✅ Visual body offset applies correctly after blueprint load |
| VRE Android | ✅ Energy need preserved during duplication |
| Alpha Skills | ✅ Custom passion levels (3+) no longer reset to None |
| Vanilla Aspirations Expanded | ⚠️ Need_Fulfillment may crash during load — investigating |

Tested with 1000+ mods loaded.

### 🗺️ Faction & NPC Support
- Replacing an NPC faction leader via blueprint now correctly preserves the leader role on the new pawn

### 🖥️ UI
- Edit button requires Dev Mode + God Mode (no accidental edits)
- Social tab defaults to showing all relations
- Custom hotkey picker in settings, persists across sessions
- Graphics refresh prevents null texture spam on clones

---

## 🗺️ Roadmap

| Status | Feature |
|--------|---------|
| ✅ | Blueprint save/load with automatic mod data preservation |
| ✅ | Pawn duplication with full mod data preservation |
| ✅ | VPE Psycasts fully preserved (paths, nodes, XP, level) |
| ✅ | Facial Animations compatibility |
| ✅ | Social relations copy (opinions, family ties) |
| ✅ | 35+ mod components automatically handled |
| 🚧 | VE Aspirations compatibility |
| 🚧 | Modded race visuals (tails, ears, custom body parts) |
| 🚧 | Migrate Starting Preset to Blueprint format |
| ⬜ | Passion dropdown with Alpha Skills support |
| ⬜ | NL Facial Editor button |

---

## 🤝 Contributing

Contributions are welcome! Before opening a PR, please check the roadmap and open issues to avoid working on something already in progress.

**Current areas under active development (please coordinate first):**
- `PawnBlueprintSaveLoad.cs` — blueprint system, still being iterated
- `PawnDuplicationUtility.cs` — cloning logic
- `FacialAnimCompat.cs` — FA mod integration

**Guidelines:**
- Do not include compiled `.dll` files in PRs — source only
- Do not include IDE files (`.idea/`, `.user`, `.DotSettings.user`)
- Open PRs from a feature branch, not `main`→`main`
- If in doubt, open an issue first to discuss

There is an open review branch `review/pr1-gear-transfer` for evaluating gear transfer contributions.

---

## ❤️ Credits & Attribution

All original credit belongs to the authors of [Pawn Editor]:

| Name | Role |
|------|------|
| [ISOR3X](https://steamcommunity.com/id/isorex) | Project lead |
| legodude17 | Main coder |
| Taranchuk | Coder |
| TreadTheDawnGames | Community contributor |
| mycroftjr | Community contributor |
| Inglix | Community contributor |
| fofajardo | Community contributor |

Fork maintained by **[Segas Wolf](https://github.com/segaswolf)**.

This fork does not claim ownership of the original concept or implementation. If there are any concerns regarding attribution, permissions, or credit, please reach out directly.

---

## 🔗 Links

- 🎮 [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3667931113)
- 🐛 [Bug Reports](https://github.com/segaswolf/pawn-editor-forked/issues)
- 💬 [Steam Discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3667931113)
- ☕ [Support on Ko-fi](https://ko-fi.com/segaswolf)
- 📦 [Original Pawn Editor](https://steamcommunity.com/sharedfiles/filedetails/?id=3219801790 )

---

## 📄 License

The original repository does not include an explicit license. All original code rights belong to their respective authors. This fork is provided as-is for community use and bug fixing purposes.
