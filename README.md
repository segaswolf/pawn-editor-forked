# 🐾 Pawn Editor Forked

A community-maintained fork of [Pawn Editor](https://steamcommunity.com/sharedfiles/filedetails/?id=3219801790) for **RimWorld 1.6 and 1.7**, maintained by [Segas Wolf](https://github.com/segaswolf).

> ⚠️ **Use either this fork or the original — not both at the same time.**

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3667931113) · [Changelog](CHANGELOG.md) · [Releases](https://github.com/segaswolf/pawn-editor-forked/releases) · [Report a bug](https://github.com/segaswolf/pawn-editor-forked/issues)

---

## 📋 What is this?

Pawn Editor lets you edit your colonists, starting pawns and NPCs from one window: bio, traits, skills, genes, health, gear, relations, needs and appearance. This fork keeps it working on current RimWorld, fixes the crashes and save/load issues of the original, and adds compatibility with popular mods. It is tested with a 1000+ mod list.

**Current version: v3.2.4** — tested on RimWorld 1.6 and on the 1.7 (Waylith) unstable branch.

---

## ✨ Highlights

- **Save and load pawns (blueprints)** with all their mod data, using RimWorld's own save system. Missing mods are skipped instead of crashing, so blueprints can be shared between mod lists.
- **Portable colonies**: save every pawn of a colony and bring them into another game.
- **Duplicate pawns** with gear, hediffs, psycasts, relations and memories.
- **Appearance editor** with live preview, searchable filters by mod and category, cosmetic genes, gradient hair and fur.
- **Health tab** with animated severity and immunity bars.
- **Starting items and resources** editor on the character creation screen, Combat Extended ammo included.
- **Custom xenotypes** list with search, delete, and a warning when genes from removed mods are missing.
- **Errors stay contained**: if a mod breaks one section, only that section is skipped and the log names the mod.

See the [Changelog](CHANGELOG.md) for everything that changed in each version.

---

## 🛡️ Mod compatibility

Dedicated support for:

| Mod | What works |
|-----|------------|
| Vanilla Psycasts Expanded | Paths, nodes, XP and level, in blueprints and duplicates |
| Vanilla Skills Expanded | Passions and expertise |
| Vanilla Aspirations Expanded | Aspirations, with safe life-stage changes |
| Vanilla Races Expanded – Android | Energy need kept when duplicating |
| Facial Animations | Every face part, editable and copied |
| Humanoid Alien Races | Race restrictions for heads, bodies and body types |
| Character Development, Life Lessons, Progression: Education | Their skills, proficiencies and traits in the editor, blueprints and duplicates |
| Trauma and Integrity | Its sections on the Bio tab, betrayal countdown included |
| Mechanoid Upgrades, Bionic Modularity, Modular Modules | Dedicated handling for their parts and upgrades |
| Gradient Hair, Anthrosonae | Appearance options |
| TacticalGroups | No colonist bar crashes |

At startup the log lists which compatibility layers hooked in, and warns about any that a mod update broke.

---

## 📦 Installation

1. Remove the original Pawn Editor.
2. Subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3667931113).
3. Load it **after Harmony**.

---

## 🐛 Bug reports

Use [GitHub Issues](https://github.com/segaswolf/pawn-editor-forked/issues) or the [Steam bug reports thread](https://steamcommunity.com/workshop/filedetails/discussion/3667931113/). Please include your mod list and `Player.log`; reports with both get fixed much faster.

Where to find the log:
- **RimWorld 1.6:** `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`
- **RimWorld 1.7:** the same folder, inside `Logs\`

---

## 🤝 Contributing

Contributions are welcome. Open an issue first so we can agree on the approach.

- Source only: no compiled `.dll` files or IDE files in PRs.
- Open PRs from a feature branch.
- Run the tests before sending: `dotnet test Source\PawnEditor.Tests`. They run against whichever RimWorld you have installed (1.6, 1.7 or both).

---

## ❤️ Credits & attribution

All original credit belongs to the authors of [Pawn Editor](https://steamcommunity.com/sharedfiles/filedetails/?id=3219801790):

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
- 🐛 [Bug reports](https://github.com/segaswolf/pawn-editor-forked/issues)
- 💬 [Steam discussions](https://steamcommunity.com/sharedfiles/filedetails/discussions/3667931113)
- ☕ [Support on Ko-fi](https://ko-fi.com/segaswolf)
- 📦 [Original Pawn Editor](https://steamcommunity.com/sharedfiles/filedetails/?id=3219801790)

---

## 📄 License

The original repository does not include an explicit license. All original code rights belong to their respective authors. This fork is provided as-is for community use and bug fixing purposes.
