# Changelog

All notable changes to this project will be documented in this file.


## [Unreleased]

### Added
- **The trait picker shows what each trait changes** (requested by xWOLFxz): skills, stats, pain,
  needs and the rest, the same text as the trait's tooltip in Bio, so mod traits can be compared
  before adding them. The expertise window does the same: what it changes per level on the left,
  and at the pawn's level on the right.

## [v3.2.5] - 2026-10-28 — Expertise editor, more mod support and 1.7 fixes

### Added
- **Vanilla Skills Expanded expertise can be picked and edited** (requested by Yamaraja777 and
  xWOLFxz). An Expertise button next to the skills opens a two-column window, like the Life Lessons
  one: pick an expertise on the left (with search, and sorting by skill or A-Z), and on the right
  change its level or remove it. Vanilla Skills Expanded's own rules decide what can be picked: the
  ones it refuses stay listed, greyed out, with its reason (for example, the skill is below 15).
  Cancel undoes these changes too.
  A note on the limit: Pawn Editor keeps Vanilla Skills Expanded's rules as they are. With a limit
  of 5 you can pick up to 5; once a pawn is full, remove one to pick another. If you lower the limit,
  pawns already above it keep what they have, because VSE only checks the limit when adding; to
  change one of theirs, remove expertise until they are below the new limit. That's how VSE works,
  and the editor follows it on purpose.
- **Isekai RPG Leveling support** (requested by teo-lost2). An "Isekai stats" button in Bio opens its
  window, and duplicating and blueprints keep level, XP and stats. Randomizing traits no longer
  removes the rank trait.
- **Gradient Hair pattern picker** (requested by Hijack Hornet and FormalCyborg). Choose the gradient
  pattern in the appearance editor, next to the second color.
- **Royal titles from any faction that has them** (requested by cole_boby and KaneDraco). The Royalty
  tab has a faction selector, so Dynamic Mod Settlements and VFE Empire titles and honor work too.
- **Prepare Carefully's equipment tab works alongside Pawn Editor** (reported by IkeFanboy64). Its
  starting equipment can be edited and removed again, not only added.

### Changed
- **Adulthood backstories follow vanilla.** RimWorld only gives one to pawns generated at 20 or
  older. Ageing a pawn past 20 in the editor now gives it one; adults from 13 to 19 show "None" and
  can still pick one by hand.
- **Trauma & Integrity rows follow that mod's settings** (requested by Kimo'). A stat you turned off
  there no longer shows in the Bio tab.

### Fixed
- **The color sliders ignored clicks on RimWorld 1.7** (reported by Kita). The hue, saturation,
  value and RGB sliders read the mouse in a way 1.7 no longer supports; they work again.
- **Backstories, traits and thoughts added late by other mods were missing from the pickers**
  (reported by Drestalos, for Backstory Constructor). The lists are now built when the picker opens.
- **The appearance editor applied changes instantly and Cancel did not undo them** (reported by
  OxTailSafu). Cancel now restores the pawn as it was.
- **"Randomize > Traits" could leave a pawn with no traits** (reported by 2ScoopsPlz).
- **"Randomize all" replaced the pawn with a new one** at the end of the list. It now randomizes the
  same pawn, keeping name, faction, relations and gear.
- **The repeat button repeated a different menu option**, and there is a new undo button.
- **Randomize and undo removed genes granted by Character Development quirks.**
- **"Randomize > Relations" seemed to do nothing.** It now lives in Social and asks for confirmation,
  since it also changes relatives.
- **Undo showed up where it could not undo anything** (Gear, Social), and two empty randomize options
  were removed from Health.
- **The appearance editor failed to open with Humanoid Alien Races** for pawns not drawn yet.
- **A custom xenotype with a missing icon flooded the log with an error every frame.** It now warns
  once, naming the mod, and shows the basic icon.
- **The warning when turning a pawn into a child mentioned removing parent relations**, which never
  happens; that line is gone.

## [v3.2.4] - 2026-10-13 — RimWorld 1.7 support, animated health bars and a big cleanup

### Fixed
- **Clicking outside the editor made the game unresponsive** (reported by rarirari). The click landed
  on the dimmed background behind the editor, and RimWorld brought that background to the front,
  covering the editor and catching every click after it. The editor now keeps the input around it,
  so the background can never come forward. On the character creation screen a click outside no
  longer closes the editor either; in a colony it still does.
- **Dragging pawns in the list moved the whole window instead of reordering them** (reported by
  rarirari). The editor window no longer moves by dragging its body; resize it from the corner.
- **Combat Extended ammo could not be added to the starting resources** (reported by The Gentleman).
  CE makes its ammo eligible while the game finishes loading, after Pawn Editor had already built that
  list. The list is now read when the picker opens, so it also follows CE's "enable ammo system"
  setting.
- **Body types that mods use internally made pawns invisible.** Some mods add body types for their
  own purposes (Big and Small's animal body points at a blank texture on purpose), and the Shape
  button listed them like any other. They are now listed last, marked "(mod special)" with a warning
  tooltip, and never picked by Randomize. A body type counts as ordinary when a backstory or the
  pawn's HAR race uses it, so nothing is hardcoded.
- **The Bio tab's Shape button and the appearance editor offered different body types.** The Shape
  button ignored HAR race restrictions and showed internal names instead of labels. Both now use the
  same list.
- **Weapons refused by another mod vanished.** Progression: Education takes a weapon back when the
  pawn lacks the proficiency, and drops it on the ground (in a colony) or nowhere at all (on the
  character creation screen, when loading a blueprint or duplicating). Pawn Editor still respects that
  rule, but now keeps the weapon in the pawn's possessions and says why.
- **The bottom buttons touched each other on narrower windows** (Close next to Delete, Load next to
  Teleport).
- **Adding a backer pawn with "No faction" selected failed.** It now adds a plain colonist.
- **Dragging a pawn to the end of the list failed** in a colony ("Could not reorder elements"). Dropping
  it at the end now moves it to the last place.
- **Bio > Groups: "Random faction" and "Random non-colonist" never picked the last faction** in the
  list.
- **Editing an item that nobody carries could fail when opening the colour picker.** The "Favorite
  colour" shortcut is simply left out when there is no pawn.
- **Adding a persona trait to a bladelink weapon didn't update the points.** Removing one already did.
- "Randomize all - including faction" could call itself in a loop in some translations. It now works
  from a fixed copy of the menu.

### Added
- **Custom xenotypes warn about missing genes.** When a saved custom xenotype uses genes from a mod
  that isn't loaded anymore, the game silently drops them. The custom xenotypes list now marks it
  "(N missing genes)", the tooltip names the genes and the mods that were active when you saved it
  but aren't now, and applying it shows a message listing what was skipped.
- **RimWorld 1.7 (Waylith) support.** Tested on the 1.7 unstable branch: the editor, its Harmony
  patches and its compatibility layers load and work. Nothing changes for 1.6.
- **Health tab: severity and immunity bars** for diseases, with a "Severity X% · Immunity Y%" label.
  They glide to the new value when you edit them.
- **"UI animations" setting** to turn those animations off.

### Changed
- **Appearance editor: the xenotype is now changed from a row at the top of the Cosmetic genes tab**,
  next to the genes it sets, instead of a button under the preview. It opens the same xenotype list as
  the Bio tab, with search and the custom xenotypes section. The preview panel gains the space.
- **Custom xenotypes can be deleted from the custom xenotypes list**, with a confirmation.
- **Appearance editor: the Source and Category filters open a searchable list** instead of a float
  menu that, with many mods, filled the screen in columns. Mods are sorted alphabetically and show how
  many options each one adds.
- **Appearance editor: a grid left empty by the Source filter says so**, with a "Show all sources"
  button. The filter is shared by every tab, so picking a gene mod in Cosmetic genes emptied the
  hair and body grids with no explanation.
- **Appearance editor: "Ignore xenotype restrictions" is shown on every tab**, the Cosmetic genes tab
  included, and is the only control for that setting. That tab's own "Override unsupported genes"
  checkbox, which did not respond to clicks, is gone.
- **The developmental stage warning is translatable** (the list shown before turning a pawn into a
  child or an adult).
- **"Default colour" and "Favorite colour"** in the item colour picker are translatable, and "None" in
  the style menu too.
- The Bio tab's columns sit a little higher, so the skills list no longer touches the bottom buttons.
- **Large internal cleanup** of the pawn list, bottom buttons, Bio tab, appearance editor, item editor,
  settings and custom xenotypes list: long methods split into small named ones. No behaviour change
  intended; it makes bug reports much faster to trace.


## [v3.2.3] - 2026-09-28 — Forced apparel, appearance editor fixes and sturdier compat

### Added
- **"Betrayal in" row** on the Bio tab for pawns flagged as betrayers (Trauma & Integrity). The mod
  stores *when* the betrayal fires, rolled at random up to ~600 in-game days, but nothing showed it.
  Now it is visible and editable.
- **Compatibility summary at startup**: one log line listing which compatibility layers hooked in, and
  a warning naming any whose mod is installed but could not be hooked (usually after that mod changed
  its code).

### Fixed
- **Editing a worn item still un-forced it** (reported by OxTailSafu after the v3.2.1 fix). Editing
  colour, style or quality builds a fresh copy of the item and wears that, and the copy started out
  neither locked nor forced. It now inherits both.
- **Duplicating a pawn, pasting apparel, and saving or loading a blueprint also lost the "forced"
  flag.** All of them now keep it. Blueprints store it too; older blueprints load as not forced, and
  older versions of the mod simply ignore it.
- **Thoughts that can't be removed now say why** (reported by OxTailSafu). Only memories are stored
  on the pawn and can be deleted; situational thoughts are recalculated from the pawn's current state.
  The editor used to show an empty cell; it now shows a dimmed icon with a tooltip.
- **One broken compatibility layer stopped every compat after it from loading.** Each one is now
  isolated.
- **A mod item that breaks an appearance grid, or a mod section on the Bio tab, no longer blanks the
  window.** Only that part is skipped, and the log names the section, the pawn and the mod.
- **Appearance editor: Randomize tattoos could put a body tattoo on the face, and the reverse.**
  Randomize now picks only from what the pickers show, so body type and head also respect the Source
  filter, genes, developmental stage and HAR. It shows a message when the filters leave nothing to pick.
- **Appearance editor: the Source filter on the Beard sub-tab listed hair mods**, so a beard-only mod
  could not be filtered to.
- **Appearance editor: switching tabs could land on the wrong sub-tab** (opening Hair could show
  Beards). Each tab remembers its own sub-tab, and the hair sub-tab is labelled "Hair" instead of
  "Head".
- **Appearance editor: the "Customize face" button was not translatable.**
- **A rare freeze** in a helper that looped forever when handed an empty list.

### Changed
- "Missing icon" warnings in the appearance editor wait until the icon has stayed missing for a
  while. Mods that load textures in the background (Faster Game Loading and similar) no longer get
  blamed for art that was simply still loading.
- The appearance editor's "Xenotype" tab is now called "Cosmetic genes", which is what it edits. The
  xenotype itself is still changed from the button on the left panel.
- Removed the "Unlock window / Lock window" button from the appearance editor. It belonged to a preview
  splitter that no longer exists. Resize the window from its corner as before.


## [v3.2.1] - 2026-09-18 — Hotfix

All of these came from player reports. Not fully verified in-game at release time.

### Fixed
- **Forced apparel was silently un-forced.** Selecting a worn item in the Gear tab briefly takes it off
  so the preview can be drawn without it, then puts it back. It kept "locked" but not "forced", so
  merely clicking an item made pawns swap that apparel out later on their own.
- **"Show hidden hediffs" did nothing.** The setting changed, but the health table only rebuilt its
  rows when you switched pawns, so the list never updated. Toggling now refreshes it immediately.
- **"Customize face" opened a blank panel on the character creation screen.** NL Facial Animation's
  own window only works inside a running game. The button is no longer offered where it cannot work;
  the Facial Animation tab in "Edit appearance" covers that screen.
- **Trauma & Integrity rows drew past the bottom of their panel**, so the Betrayer checkbox and its
  tooltip ended up over the bottom buttons. Since a checkbox reacts to a click anywhere inside it, clicking there could
  toggle Betrayer unnoticed. Rows now stop at the panel edge.
- **Possible fix for a crash when opening the editor mid-game** (unconfirmed). The background dim was
  being resized by the same code that resizes editor windows, and the two fought over it. It no longer
  is, and if the dim fails to draw it closes quietly instead of taking anything down with it.

### Added
- **"Clear betrayer flag on all pawns"** under Quick actions on the Health tab (only with Trauma &
  Integrity installed). Reports how many pawns are flagged and asks for confirmation. Offered as an
  explicit action rather than an automatic cleanup: nothing in the data distinguishes an accidental
  flag from one the player set deliberately or one the mod set through its own gameplay.


## [v3.2] - 2026-09-04 — Facial Animation, Gear, Royalty & Ferny's mods

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
  Previously it was lost for pawns without a royal title.
- **Ferny's mods**: Trauma & Integrity editable from the Bio tab (with State and Betrayer), a
  Development tab with searchable Wants and Quirks, and Progression: Education proficiency handling.
- **Progression: Education proficiencies** treated as tiers of a track: picking "fluent speech" removes "mute", a separate "Traits (including
  forced ones)" reroll swaps a pawn's tier within its own track, and those traits are excluded from the
  normal trait reroll, since the game's generator could never restore them.
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
- **An error while saving could corrupt later loads.** If saving failed halfway, temporary changes the
  editor makes while writing stayed active for the rest of the session and could rewrite IDs during
  unrelated loads. They are now always undone, error or not.
- **Missing tails, ears and other modded appearance genes now appear.** The cosmetic-gene filter only
  admitted genes that were 100% cosmetic, hiding anything with a minor mechanical side effect.
- **Typing in a search box no longer fires game hotkeys** or moves the camera.
- **The text caret is visible in the name field.** Another UI mod can leave it invisible for every
  window; the name fields now make sure it shows, without changing anything for other windows.
- **The window can no longer be squashed into an unusable state.** It has a minimum size, enforced
  while you resize instead of snapping back afterwards. No maximum.
- **Bottom buttons no longer overlap.** They are laid out as one evenly spaced, centred group with
  clamped slot widths, instead of mixing centred and right-anchored anchoring.
- **Random faction selection could never pick the last faction** in the list.
- **The repeat-randomize button** is a normal size again and re-runs the correct option in every
  language (tracked by index instead of by matching the label text).
- **Animal training list scrolls.** With modded trainables, the entries at the bottom fell outside the
  window.
- **Broken modded textures** draw a placeholder and log one line naming the mod, instead of flooding
  the log every frame. A bad modded body-type icon no longer breaks the editor at startup.

### Known issues
- Changing a xenotype's head type can revert to the human shape.
- Pawn IDs are not preserved across saves, so couple compatibility can differ when loaded elsewhere.
- Work in progress, not verified: Gradient Hair support, Anthrosonae "Change fur", RJW sexuality editing.


## [v3.1] - 2026-07-21 — Blueprints come back whole, modular prosthetics

Save, clone or load a pawn and they now come back exactly right — a whole family of body-part bugs is fixed.

- Psycasts (paths, level, XP, learned casts) survive save/load, not just duplication.
- Prosthetics keep their correct left/right side; no more wrecking the clone's good arm.
- No more stray injuries or addictions on loaded pawns.
- Slot-in modules (arm blades, analyzers…) stay on their host part.
- Modular prosthetics from Integrated Implants + Bionic Modularity are properly supported, with guided placement.
- Hediff picker: left/right merged into one entry with a side selector, plus a "fits this pawn" filter.
- Appearance editor & window layout fixes.
- Much harder to break with a big modlist — one mod's bad data can't take the whole editor down anymore.

⚠️ Confirming a crash fix: if the editor ever vanished on you with a "TexPawnEditor" error, please update and let us know if it's gone.

💬 Loved / hated something about the new editing experience? The discussions are open — your feedback shapes the next patch. 💛


## [v3.0] - 2026-07-15 — Portable Colonies, Performance & Quality of Life

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
- Pawn portraits no longer re-render needlessly while editing.

### Fixed
- **"Randomize all — keep xenotype"** no longer loses genes and name on baseliner-named xenotypes (e.g. custom "Veldrak"-style xenotypes); endogenes, xenogenes, xenotype name and icon are preserved.
- **Mechanitor "phantom" bandwidth**: cloning/loading a pawn no longer binds the clone to the *original's* mech.
- **Black screen on load** (portrait cache was being fully cleared instead of marked dirty).
- Fixed an error on certain pawns with the Pioneering component.
- Proficiency list now refreshes correctly after add/remove, with a sanitizer for malformed data.
- Trait **Mute** handling and a Refill/Fulfillment desync.
- More reliable detection of which genes are cosmetic, endogenes or xenogenes.


## [v2.4.8] - 2026-06-15 — Compatibility & crash fixes

- Custom gravship scenarios (Vanilla Gravship Expanded) no longer lose the pawns' starting items.
- Loading older presets with default values no longer fails.
- Fixed a crash with some custom xenotypes.


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


## [v2.4.4] / [v2.4.5] - 2026-05-15

### Fixed
- **Backstory transitions**: Changing a pawn's age from 18 to 13 now properly removes the adult backstory. Going from 13 to 18 generates a new contextual adult backstory matching the childhood. The age field is no longer locked to the current developmental stage.
- **Item stacking**: Adding the same item multiple times now correctly increments the stack count instead of creating separate entries. Also fixes the point system not properly tracking duplicate items.
- **DeepSave fix**: Duplicating pawns with abilities no longer causes save corruption errors.

### Performance
- **Search box FPS fix**: clicking the search box with large modlists (600+ mods) no longer drops the framerate to ~5 FPS.

### Changed
- Labels use consistent sentence case throughout the editor.


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
- Restored the "Android editor..." button that was lost when the xenotype dropdown was replaced

### Added — VAspirE Life Stage Safeguards
- Changing a pawn from adult to child/baby now clears all aspirations (children cannot have them)
- Changing a pawn from child/baby to adult generates fresh random aspirations
- Warning dialog now lists aspiration removal when changing to a non-adult stage
- CompleteSilent mode: completing aspirations in pre-colony no longer triggers growth moment letters

### Fixed
- Fixed pawns spawning inside walls when duplicating or loading blueprints in-game
- Fixed a crash in the pawn kind list with some modded pawn kinds
- Fixed custom xenotype tooltip showing garbled text ("Nòt ìnhêrìtàblê") due to missing translation key
- Fixed the Xenotype Editor crashing when opened in an existing game
- Fixed aspirations not regenerating when changing a pawn from child to adult

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
- VAspirE is optional: Pawn Editor works the same without it

### Fixed
- Fixed a startup crash in the item list with some modded item styles
- Fixed a startup crash in the pawn kind list with some modded pawn kinds
- Fixed item styles sometimes not being found

### Notes
- VAspirE integration is Phase 1 (pre-colony editor). In-game editing will come in a future update

## [v2.3.1] - 2026-03-28

### Fixed
- Starting items no longer disappear after editing pawns in pregame
- Passions and skill levels preserved when changing backstory
- Hotkey can be fully disabled via right-click (sets to None); Escape cancels picker

## [v2.3.0] - 2026-03-28

### Major: Blueprint & Duplication Overhaul
- Complete rewrite of blueprint save/load and pawn duplication
- New system automatically preserves all mod data without per-mod patches
- VPE Psycasts: paths, unlocked nodes, XP, and level fully preserved
- Mechlink, Cyberlink, and all Hediff_Level types correctly duplicated
- 35+ mod components automatically preserved
- Duplication now uses the same system as blueprints

### Fixed
- Passion sanitizer: mods like Alpha Skills with passion values 3+ no longer reset to None
- Ideo fallback: fixed crash when loading blueprints for pawns whose faction ideo couldn't resolve
- Action bars: fixed missing gizmo bar on loaded/duplicated pawns
- Fixed a crash when replacing a pawn via blueprint
- VRE Android: energy need correctly preserved during duplication
- Tactical Groups: fixed errors when both mods are active

### Known Issues
- VAspirE: Need_Fulfillment may crash during load — investigating
- Blueprint overwrite: rewriting an existing file may cause issues — save as new file recommended

## [v3d10] - 2026-03-14

### Added
- Added a warning when changing a pawn's life stage through the age combo box.

### Changed
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