# LootNet

A client mod for [SPT](https://www.sp-tarkov.com/) that tracks the flea market value of everything you loot during a raid and shows a polished summary screen when you extract.

**Version:** 1.0.9  
**SPT Version:** 4.0.13  
**License:** MIT

---

## Features

- **Live loot value** — displays the running flea market value of your inventory while in raid, shown as a native-looking label in the inventory screen
- **Raid summary screen** — after extraction, shows an animated breakdown of your total loot value, top 5 finds, kill count, and (if using PIT Fireteam) follower kill stats
- **PMC & Scav support** — PMC raids track newly acquired gear only; Scav raids include your spawn loadout
- **Flea + handbook fallback** — uses live flea market prices, falls back to handbook values if flea data is unavailable
- **Secret easter egg** — find it in the F12 BepInEx config menu

---

## Changelog

### v1.0.8
- **Fika team summary** — coop raids open a ranked scoreboard showing the whole fireteam at once: team haul total with a rising count-up animation, and one row per player with rank medal, loot bar, kills, items, and XP
- Every player's top finds are shown expanded at once for easy comparison; click a row to collapse it, and the board auto-compacts for larger squads
- Summaries are relayed at the moment of extraction (host and clients), so teammates appear without waiting for the raid to fully end
- Coop raids open the scoreboard directly; solo and non-Fika raids are unchanged

### v1.0.6
- **XP earned tracking** shown as `+XXX XP EARNED` under the loot value, with the survival/extract bonus revealing as a separate animated sub-label
- **Death summary** — dedicated red "YOU ARE DEAD / FAILED TO EXTRACT" title, death stinger sound, and panel shake when you die
- **In-Raid Inventory Counter toggle** to hide the running counter (F12 config)
- **Default Flea Market Rules toggle** to only count flea-sellable items (F12 config); quest items are always excluded
- Teammates redesigned as horizontal cards under the left-column stats

### v1.0.5
- **Raid History panel** — new `HISTORY` button in the main menu nav bar opens a side drawer showing all raids from the current session
  - Each card shows map, raid type (PMC / SCAV), relative time, total loot value, and kill summary
  - PIT Fireteam squad kills are included in the kill line when the mod is detected
  - Empty-state message shown when no raids have been recorded yet
  - Capped at 15 entries (oldest dropped silently); a notice is shown at the bottom when the cap is reached
- Fixed GPU texture leak in the raid summary screen
- Internal: extracted shared raid-state reset logic, cached PIT reflection delegate

### v1.0.4
- Full-screen animated raid summary screen after extraction
- Native EFT UI loot value bar in the inventory screen
- Live item tracking during raid (PMC and Scav modes)
- PIT Fireteam follower kill stats on the summary screen

---

## Installation

1. Download the latest `LootNet.zip` from [Releases](../../releases)
2. Extract into your SPT install folder — the DLL lands in `BepInEx\plugins\` automatically
3. Launch SPT as usual

---

## Optional: PIT Fireteam integration

If you have [PIT Fireteam](https://hub.sp-tarkov.com/files/file/2362-pit-fireteam/) installed, LootNet will automatically detect it and show a **TEAMMATES** section on the summary screen with each follower's kill count.

No configuration needed — it's a soft dependency.

---

## Building from source

```sh
cd LootNet
dotnet build -c Release
```

The PostBuild step copies `LootNet.dll` directly to your SPT `BepInEx\plugins\` folder.  
A distributable `LootNet.zip` is also produced automatically.

> Requires .NET SDK with `netstandard2.1` support and a local SPT installation for DLL references.

---

## Project structure

```
LootNet/
├── LootNet.csproj
├── Plugin.cs                    ← BepInEx entry point, config, patch registration
├── Patches/
│   ├── InventoryPatch.cs        ← tracks items added/removed from player inventory
│   ├── InventoryScreenPatch.cs  ← inventory open/close + raid end hooks
│   └── MenuScreenPatch.cs       ← injects the HISTORY button into the main menu nav bar
├── Services/
│   ├── PriceService.cs          ← fetches & caches flea/handbook prices from SPT server
│   └── RaidTracker.cs           ← tracks loot value, kills, PIT follower stats, and raid history
└── UI/
    ├── LootValueDisplay.cs      ← in-raid inventory overlay (cloned from EFT's native UI)
    ├── RaidSummaryDisplay.cs    ← animated post-raid summary screen
    ├── RaidHistoryDisplay.cs    ← side drawer showing session raid history
    └── RaidHistoryMenuButton.cs ← builds and injects the HISTORY nav button
```

---
