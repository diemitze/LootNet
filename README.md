# LootNet

LootNet tracks what everything you loot in a raid is worth, then lays it all out on a summary screen when the raid ends.

Built for SPT 4.1. Released under the MIT license.

## What it does

**Raid summary.** When a raid ends, your loot fills a stash grid one item at a time, most valuable first, each at its real size with its in-game icon. The total counts up as the items land. Guns and armor show up assembled, with their attachments and plates folded into one item. Next to the grid you get your kills, XP, how the raid stacks up against your recent ones, and a closing line that fits how it went. If you died, the grid fades to grey and the total is struck through.

**Live loot value.** While you're in a raid, your inventory screen shows the running value of what you've picked up.

**PMC and Scav raids.** On a PMC raid only what you find counts, not the gear you brought in. On a Scav raid your whole loadout counts, since it was free.

**Raid history.** A HISTORY button in the main menu lists your recent raids, and any of them can be replayed. Turn on *Persist Raid History* to keep them between sessions.

**Prices.** Values come from the flea market, with handbook prices as a fallback. Quest items never count. Anything you can't sell on the flea is left out too, unless you turn that off.

## Optional integrations

**PIT Fireteam.** If [PIT Fireteam](https://hub.sp-tarkov.com/files/file/2362-pit-fireteam/) is installed, the summary lists your squad: each follower's kills, who went down, and the squad's total. Nothing to set up.

**Fika.** In co-op raids, everyone's summary is shared and shown together on one team scoreboard. This needs `LootNetFika.dll` (it's in the release zip) and the LootNet server mod.

## Installing

1. Download `LootNet.zip` from [Releases](../../releases).
2. Extract it into your SPT folder. It contains:
   - `BepInEx/plugins/LootNet/`: the client mod and the Fika companion
   - `SPT_Runtime/user/mods/LootNetServer/`: the server mod
3. Start SPT as usual.

The server mod provides the price data and relays summaries in Fika raids. Without it, prices fall back to handbook values.

## Settings

Open the BepInEx config with F12.

| Setting | Default | What it does |
|---|---|---|
| Show In-Raid Inventory Counter | On | Shows the running loot value in your inventory during a raid |
| Manual Dismiss Only | Off | The summary stays up until you click it, instead of closing after 15 seconds |
| Persist Raid History | Off | Saves raid history to `BepInEx/plugins/LootNet/raid_history.json`, keeping the last 15 raids |
| Use Handbook Prices | Off | Uses handbook prices instead of flea prices. Useful with custom traders or no flea market. Needs a restart |
| Default Flea Market Rules | On | Only counts items that can be sold on the flea. Turn off if another mod unlocks the flea filter |

There's also a hidden setting in there somewhere.

## Building

You need the .NET SDK and a local SPT 4.1 install. The project files reference the game DLLs by absolute path, so point them at your own install first.

```sh
dotnet build -c Release
```

The build copies `LootNet.dll` into your SPT plugins folder, builds the server mod and the Fika companion, and packs everything into `LootNet.zip`.

## Layout

```
Plugin.cs              entry point, config, patch registration
Patches/               inventory tracking, raid end, main menu button
Services/
  RaidTracker.cs       loot, kills, XP, squad and raid history
  PriceService.cs      prices from the server mod
  LootIconCache.cs     item icons
  Quips.cs             closing lines
UI/
  RaidSummaryDisplay.cs   summary screen
  TeamSummaryDisplay.cs   Fika team scoreboard
  RaidHistoryDisplay.cs   history panel
  LootValueDisplay.cs     in-raid inventory counter
fika/                  Fika companion plugin
server/                SPT server mod
```
