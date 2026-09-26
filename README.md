# Evil Twins Command Center

A Retail WoW guild addon for browsing opt-in character snapshots, gear wishlists, talent exports, bags, and SimulationCraft profiles.

## Install

Copy the `EvilTwinsCommandCenter` folder into:

`World of Warcraft/_retail_/Interface/AddOns/`

Reload the UI or restart WoW, then type `/etcc` or `/eviltwins`.
Run `/etcc test` for a quick in-game snapshot, bag, and transfer encoding check.

Each member who wants to publish a character checks **Share my gear, build, and wishlist**. Gear and talent data are captured from that character and shared with guildmates running the addon. **Share my bags with guildmates** is a separate opt-in that shares item links and quantities from the character's carried bags. To share SimC, also check **Share SimC export with guild cache** and install the current [SimulationCraft addon](https://github.com/simulationcraft/simc-addon). SimC transfers include equipped gear only. The current SimulationCraft switch combines bags and bank, so the addon shares bags separately to avoid including bank data.

## What version 0.3 does

- Shows snapshots from guildmates who have opted in and been online while another addon user was online. Previously received snapshots remain available offline in SavedVariables.
- Shows equipped item links, equipped item level, current specialization, and the active talent export string.
- Lets each character maintain up to 20 wishlist items, entered as an item link or item ID with an optional note. Wishlists are manual; the addon does not infer upgrades.
- Publishes an opted-in SimC export on login and after gear or build changes. Online guildmates running the addon save the latest export locally, with its capture time.
- Publishes an opted-in snapshot of carried bags on login and after bag changes. Select a character and click **View bags** to see the last received item links and quantities. Bank contents are not scanned or shared.
- Lets you select a guildmate and click **View last SimC** to copy their latest received export, even when they are offline. **Request fresh** asks their addon for a new export while they are online.
- Sends small, paced addon messages and retries when messaging is throttled or temporarily locked down.

Profiles are opt-in. Offline viewing works only when your addon received the guildmate's SimC export or bag snapshot while both of you were online, or when their addon replied to your sync after you logged in. Each timestamp shows when its data was captured; it may be older than their current state. There is no central server or automatic way to retrieve a character's state after everyone with a copy has logged off. Up to 50 SimC profiles are retained per installation.

Turning sharing off sends an online revocation message. Members who were offline may retain a previously cached copy until a future sync or cache cleanup. Do not use the addon as a private data channel.

## Architecture

The addon uses Blizzard's guild and whisper addon-message channels. Snapshot, bag, and SimC data are encoded, split into small parts, sent through a paced queue, and reassembled by the receiver. It does not use a server or reach the internet. Snapshots and last received bags and SimC exports are stored per WoW installation in `EvilTwinsCCDB` SavedVariables. A future companion app could provide an off-game dashboard.

## Development notes

This version targets Retail interface `120100` (Midnight 12.1). The SimulationCraft integration uses its `SimulationcraftAPI.GetSimcProfile` interface, introduced in March 2026. The dependency is optional; browsing, builds, bags, and wishlists work without it. No SimulationCraft source is bundled.

There is no automated in-game test environment in this repository yet. Test with two guild characters before wider use: enable sharing on both, verify snapshot exchange, add and remove wishlist items, copy a talent string, enable bag sharing on one character and verify **View bags** on the other, move an item between bags, disable bag sharing and verify revocation, then test cached and fresh SimC while both are online and after one logs off.
