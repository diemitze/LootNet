# Changelog

## [1.2.0]

### Added
- **Stash grid raid summary.** The end-of-raid screen is rebuilt around your loot: every find is laid out in an inventory grid at its real size, with its in-game icon, short name and value, tinted by value tier. Cells drop in from most to least valuable while the total ticks up beside them
  - Guns and armor arrive as one cell, assembled. Barrels, stocks, mags, scopes and plates mounted on something you looted are folded into it, carrying their value, and the icon shows the gun with its attachments
  - Stacks (ammo, bolts) get one cell with a count
  - A big raid shrinks the cells to fit. Anything that still does not fit is summed in a "+N more items" row
  - On a death the grid drains to grey and the total is struck through
  - The layout keeps a 1920x1080 frame centred on screen, so ultrawide and 16:10 get margin instead of clipping
- **PIT Fireteam squad block** on the summary when PIT Fireteam is installed: every follower with their kills, who went down (KIA), and squad totals. Followers with no kills are listed too, and deaths are tracked during the raid
- **Around 80 new closing lines**, plus new situations with their own lines: a new personal best, scav runs, a squad wipe, extracting after losing squadmates, and your followers outkilling you. A line no longer comes back until 12 others have been shown
- Raid history now stores the full loot list, so replays show the whole grid

### Changed
- The cinema layout (letterbox bars, map watermark, top find block) is replaced by the grid layout
- The raid comparison line reads "WOULD HAVE BEEN YOUR 2ND BEST" on a death instead of ranking loot you lost
- Item icons are requested through the same path the stash uses. The previous path skipped the game's render queue, so a burst of requests could come back blank or wrong. Icons now also start rendering the moment the raid ends and keep filling in for as long as the summary is open

### Notes
- Replays of raids saved before 1.2.0 only have their top five items, shown as small cells with the rest in the "+N more items" row

---

## [1.1.2]

### Fixed
- **`/lootnet/prices` returned nothing on some installs.** If the server's flea price table held a `NaN` or infinite value for any single item, `System.Text.Json` refused to write the response and the whole request failed, so the client silently fell back to handbook prices and the server log filled with `.NET number values such as positive and negative infinity cannot be written as valid JSON`. Non-finite prices are now skipped, per item, in all three places they can enter: the static price table, a live offer's requirement cost, and the averaged live-offer price

---

## [1.1.1]

### Added
- **Fika companion ships again**: `LootNetFika.dll` is back in the release zip, built against Fika 2.4.2 (the 4.1 build). It was held out of the package while Fika had no 4.1 release, which left the coop team summary, the live teammate extract relay, and team/personal high scores silently inactive for Fika users. No action needed beyond installing the update
- **Map watermark** on the raid summary: the map name set large and ghosted at 5% white, filling the empty upper band of the screen. Auto-sizes down so a long name like "Streets of Tarkov" stays clear of the top find block
- **Raid comparison line** beside the loot total, ranked against your stored raid history: `BEST RAID YET`, `3RD BEST OF YOUR LAST 12`, or `+142% VS YOUR AVERAGE`. Placed from the total's measured width once the count-up settles, so an eight-figure haul cannot push it into the loot list
- **Top find**: the raid's most valuable item shown with its real in-game icon, opposite the watermark. Falls back to text alone when the icon does not resolve

### Changed
- **Raid summary backdrop reworked** for depth and legibility:
  - The frozen frame is now defocused, downsampled through a 1/6 render texture and scaled back up, so small text no longer sits on sharp gameplay detail
  - The screenshot plate is fully opaque and slightly cooled. It was at 90% alpha, letting a tenth of the still-rendering scene bleed through as a faint ghost
  - The flat backdrop tint is replaced by a vertical gradient, light at the top and heaviest across the lower band where the typography sits. A uniform wash read flat
  - The vignette is lighter and its falloff starts further out. It used to be darkest in the corners, which is exactly where the title and the loot list live
  - Soft shadow under the raid title and the loot total
- Loot-row contrast on bright maps (snow, daytime) goes from 3.3:1 to 5.1:1. Dark maps are effectively unchanged

### Fixed
- The raid summary leaked a full-resolution screenshot texture every raid. The capture is now released once it has been downsampled

### Internal
- Item icons are resolved through a new cache built to survive the two ways this fails silently: a load that faults while the UI is mid-transition is retried rather than blacklisted for the session, and a cached sprite whose asset bundle has been unloaded is revalidated on read and re-fetched. Pending state is always cleared, so an interrupted load cannot leave a template stuck
- `RaidStats` carries a `TopItemTemplates` list alongside `TopItems` rather than widening that tuple, which would have broken the Fika DTO, the team summary store, and every deconstruction on the team screen
- The Fika companion is bundled by default now that it builds against 2.4.2; opt out with `-p:BundleFika=false`. Verified against Fika 2.4.2 by compilation and by checking the patched members still exist (`CoopGame.Stop`, `CoopGame.Extract`, `FikaBackendUtils.IsHeadless`, `CoopHandler.TryGetCoopHandler`)
- `preview-backdrop.html`: a browser A/B harness that reproduces the summary's backdrop math, including the real downsample, so the layers can be tuned without launching a raid

---

## [1.0.9]

### Added
- **Team & personal high scores (Fika)** — the coop scoreboard now remembers records across raids. Each player's loot bar fills toward their personal best, with a marker showing the record to beat, and the combined team haul tracks the squad's all-time best under the hero total
- Beating a record is celebrated: the bar turns gold with a `★` on the value, confetti bursts from the card, and a triumphant sound plays — a bigger burst fires when the whole team sets a new combined record
- Records persist to disk on the server (survive restarts) and are shared across the fireteam, so everyone sees the same marks and celebrates together; personal bests count team raids only

### Changed
- Loot values now come from live flea market offers instead of the base price table, so custom or inflated flea prices (e.g. an SVM x10 flea) are reflected in raid value. Items with no live offer fall back to the base price

### Fixed
- Stackable items were over-counted by their inventory nesting depth (e.g. a single looted item showing `x4`), visible when a mod makes barter items stackable. The stack scan no longer double-walks the already-recursive item list

## [1.0.8]

### Added
- **Fika team summary** — coop raids now open a ranked scoreboard showing the whole fireteam at once: combined team haul with an animated count-up, and one row per player with rank medal, avatar, survived/KIA status, PMC/scav kills, items, XP, and a loot bar sized against the top earner
- Every player's top finds and XP breakdown are shown expanded at once so you can compare the whole team's loot at a glance; click a row to collapse it, and the board auto-compacts (shorter rows, top-3 finds) for larger squads
- The team haul total animates up with an eased count and a landing pulse, and rises again each time a new teammate's loot reports in
- The top looter gets a gold stripe, and players still in raid show as a pulsing "waiting to extract" row
- Teammate summaries are relayed the moment each player extracts (host and clients), so players appear on the scoreboard without waiting for the whole raid to end
- Scoreboard updates live as teammates extract — new arrivals flash in — and stops polling once everyone has reported

### Changed
- Coop raids open the team panel directly instead of the solo summary; solo and non-Fika raids are unchanged

## [1.0.6]

### Added
- **XP earned tracking** — raids now display the XP you earned shown as a cyan `+XXX XP EARNED` line right under the loot value
  - Survival/extract bonus is detected separately and slides in as a green `+XXX SURVIVED BONUS` sub-label, with the main XP number animating up to the new total
  - Works for both successful extracts and death raids (orange `EXTRACT BONUS` styling when died)
- **Death summary** — dying now shows a dedicated "YOU ARE DEAD" title in red with a "FAILED TO EXTRACT" subtitle, the iconic Tarkov death stinger sound, and a screen shake — no more cheerful "RAID COMPLETE" when you bled out
- **In-Raid Inventory Counter toggle** — new F12 config option to hide the running loot value counter that appears in your inventory during a raid (still tracks in the background for the summary)
- **Default Flea Market Rules toggle** — new F12 config option (default ON) to only count items that can be sold on the flea market toward raid value — disable if you use a mod that unlocks the flea filter
- **Quest items excluded** — quest items are now always filtered out of the raid value regardless of price source, so things like research notes no longer inflate your totals

### Changed
- **Teammates redesign** — PIT Fireteam squad members now appear as horizontal cards in the left column under the stat rows, instead of a stacked list at the bottom of the panel
- **XP line placement** — XP earned sits paired with the loot value at the top of the summary, so the two reward metrics read together at a glance

---

## [1.0.5]

### Added
- **Raid History panel** — new `HISTORY` button in the main menu nav bar opens a side drawer with a full log of raids from the current session
  - Each card shows map name, raid type (PMC / SCAV), time since the raid, total loot value, and kill summary
  - PIT Fireteam squad kills are included in the kill line when the mod is present
  - History is capped at 15 entries; a notice appears at the bottom of the list when the cap is reached and older entries start being dropped

- **Manual dismiss** — raid summary screen can now be set to require a click to close instead of auto-dismissing (toggle in F12 BepInEx config)

### Fixed
- Fixed a GPU texture leak in the raid summary screen (vignette texture was never released)

### Internal
- Extracted shared raid-state reset logic into a single method
- Cached PIT Fireteam reflection delegate to avoid per-kill heap allocations
- Replaced display-string parsing in the value counter with a tracked numeric field

---

## [1.0.4]

### Added
- Full-screen animated raid summary screen shown on extraction
  - Animated loot value counter, top finds list with rank badges, kill stats
  - PIT Fireteam follower kill stats shown in a TEAMMATES section
  - Auto-dismiss timer with manual override via F12 config
  - Optional video easter egg (place `weii weii.mp4` in the plugin folder)
- Native-style loot value bar in the inventory screen, cloned from EFT's own UI
- Live item tracking during raid — PMC raids track newly picked-up gear only; Scav raids include the spawn loadout

---

## [1.0.3]

### Fixed
- Fika compatibility — mod no longer interferes with co-op sessions

---

## [1.0.2]

### Fixed
- Server price fetch error handling improved
- PostBuild deployment path corrected

---

## [1.0.1]

### Fixed
- Inventory display reworked using native EFT UI clone to fix layout issues

---

## [1.0.0]

- Initial release
