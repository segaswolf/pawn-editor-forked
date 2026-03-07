# 🐾 Pawn Editor Forked

> An unofficial fork of [Pawn Editor](https://steamcommunity.com/sharedfiles/filedetails/?id=3219801790) for **RimWorld 1.6**

[![Steam Workshop](https://img.shields.io/badge/Steam-Workshop-blue?logo=steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=3667931113)
[![Latest Release](https://img.shields.io/github/v/release/segaswolf/pawn-editor-forked)](https://github.com/segaswolf/pawn-editor-forked/releases/latest)
[![GitHub Stars](https://img.shields.io/github/stars/segaswolf/pawn-editor-forked)](https://github.com/segaswolf/pawn-editor-forked/stargazers)

---

## 📋 What is this?

This is a **community fork** of the original Pawn Editor mod. The original is an incredibly useful tool for editing colonists, but had stability issues with large modlists and certain DLC interactions.

This fork aims to **fix those problems**, improve compatibility with popular mods, and keep the editor running smoothly — even with 1000+ mods loaded.

> ⚠️ **Important:** Use either this version or the original, but **not both at the same time.**

---

## 📢 Stay in the loop

> **→ Check the [Discussions tab](https://steamcommunity.com/sharedfiles/filedetails/discussions/3667931113) for the active WIP thread** — it lists everything currently being worked on, what's been fixed recently, and what's coming next. If you're curious whether a bug is known or a feature is planned, check there first.
>
> **→ Found a bug?** [Report it here](https://github.com/segaswolf/pawn-editor-forked/issues), not in the comments. It helps a lot to include your **mod list** and **`Player.log`** — reports with those get fixed faster.

---

## ⚠️ Work in Progress

This fork is **actively being developed**. While many critical bugs have been fixed, you may still encounter issues.

**Currently in progress:**

- **Pawn save/load (Blueprints):** Individual pawn saving uses a new robust XML format. Appearance on modded races (tails, custom visuals) may not fully restore yet.
- **Starting Preset save/load:** Full colony presets still use the old system. May fail with complex modlists. Migration to new format is planned.
- **Pawn duplication:** Works great for vanilla/human pawns. Modded race features may not copy perfectly yet.

---

## ✨ What's been fixed

### 🔧 Stability

- Fixed crashes when duplicating pawns (`Collection modified`, `Sequence no matching element`)
- Fixed crash in Bio tab when traits change during render
- Fixed startup crashes with TacticalGroups and other mods
- Fixed duplicate pawn ID conflicts that corrupted save files
- Fixed null reference exceptions with xenotypes and gene lists
- Starting Preset load no longer crashes the game on failure
- Stabilized in-game editor button behavior
- Fixed `ListingMenu_Items` crash caused by mods with null style entries

### 🧬 Pawn Duplication (Clone)

- Clones keep the same name as the original (vanilla Obelisk behavior)
- Clones correctly copy gender, appearance, hair, skin color, and melanin
- Clones copy clothing, armor, and weapons with quality, color, and HP
- Ideology certainty is now preserved accurately
- Biological and chronological age no longer get swapped

### 💾 Blueprint Save/Load

- Brand new **XML-based blueprint format** (replaced crash-prone Scribe system)
- Saves everything: bio, traits, skills, genes, hediffs, abilities, apparel, equipment, relations, work priorities, inventory, royal titles, records, and active mod list
- Missing mods/DLCs are **gracefully skipped** when loading — no more crashes!
- Share blueprints between different modlists

### 🎨 Appearance

- Hair, head type, body type, skin color, and fur properly saved and restored
- Melanin values preserved for accurate skin tones
- Style elements (beard, tattoos) correctly handled
- Genes load before appearance to prevent override conflicts

### 🛡️ Mod Compatibility

| Mod | Status |
|-----|--------|
| **Facial Animations** | ✅ Face type, eye color, brow, lid, mouth, skin, head controllers all copy correctly |
| **TacticalGroups** | ✅ Harmony finalizers prevent colonist bar crashes |
| **Vanilla Skills Expanded** | ✅ Passion system compatible |
| **VE Hussar / Giant gene** | ✅ Visual body offset applies correctly after blueprint load |

Tested with **1000+ mods loaded**.

### 🗺️ Faction & NPC Support

- Replacing an NPC faction leader via blueprint now **correctly preserves the leader role** on the new pawn

### 🖥️ UI

- Edit button requires Dev Mode + God Mode (no accidental edits)
- Social tab defaults to showing all relations
- Custom hotkey picker in settings, persists across sessions
- Graphics refresh prevents null texture spam on clones

---

## 📦 Installation

1. **Remove** the original Pawn Editor mod
2. **Subscribe** on [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3667931113)
3. Place it **after Harmony** in your load order
4. That's it!

---

## 🗺️ Roadmap

| Status | Feature |
|--------|---------|
| ✅ | Stable blueprint save/load system |
| ✅ | Pawn duplication with full appearance/gear copy |
| ✅ | Facial Animations compatibility |
| 🚧 | Social relations copy (opinions, family ties) |
| 🚧 | Modded race visuals (tails, ears, custom body parts) |
| 🚧 | Migrate Starting Preset to Blueprint format |
| ⬜ | GradientHair support (dual color) |
| ⬜ | VE Aspirations integration |
| ⬜ | Clone suspicion debuff (optional, for lore) |

---

## ❤️ Credits & Attribution

All original credit belongs to the authors of [Pawn Editor](https://steamcommunity.com/sharedfiles/filedetails/?id=3219801790):

| Name | Role |
|------|------|
| **ISOR3X** | Project lead |
| **legodude17** | Main coder |
| **Taranchuk** | Coder |
| **TreadTheDawnGames** | Community contributor |
| **mycroftjr** | Community contributor |
| **Inglix** | Community contributor |
| **fofajardo** | Community contributor |

**Fork maintained by:** [Segas Wolf](https://steamcommunity.com/id/SegasWolf)

> This fork does not claim ownership of the original concept or implementation.
> If there are any concerns regarding attribution, permissions, or credit, please contact me directly.

---

## 🔗 Links

- 🎮 [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3667931113)
- 💻 [GitHub Repository](https://github.com/segaswolf/pawn-editor-forked)
- 🐛 [Bug Reports](https://github.com/segaswolf/pawn-editor-forked/issues)
- ☕ [Support Development (Ko-fi)](https://ko-fi.com/segaswolf)

---

## 📄 License

The [original repository](https://github.com/ISOR3X/pawn-editor) does not include an explicit license. All original code rights belong to their respective authors. This fork is provided as-is for community use and bug fixing purposes.
