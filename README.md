# Evil Twins Command Center

A Retail WoW guild addon for browsing opt-in character snapshots, gear wishlists, talent exports, and SimulationCraft profiles.

## Install

Copy the `EvilTwinsCommandCenter` folder into:

`World of Warcraft/_retail_/Interface/AddOns/`

Reload the UI or restart WoW, then type `/etcc` or `/eviltwins`.
Run `/etcc test` for a quick in-game snapshot and transfer encoding check.

Each member who wants to publish a character checks **Share my gear, build, and wishlist**. Gear and talent data are captured from that character and shared with guildmates running the addon. To share SimC, also check **Share SimC with guild cache** and install the current [SimulationCraft addon](https://github.com/simulationcraft/simc-addon). SimC transfers include equipped gear only; bag and bank contents are excluded.

## What version 0.2 does

- Shows snapshots from guildmates who have opted in and been online while another addon user was online. Previously received snapshots remain available offline in SavedVariables.
- Shows equipped item links, equipped item level, current specialization, and the active talent export string.
- Lets each character maintain up to 20 wishlist items, entered as an item link or item ID with an optional note. Wishlists are manual; the addon does not infer upgrades.
- Publishes an opted-in SimC export on login and after gear or build changes. Online guildmates running the addon save the latest export locally, with its capture time.
- Lets you select a guildmate and click **View last SimC** to copy their latest received export, even when they are offline. **Request fresh** asks their addon for a new export while they are online.
- Sends small, paced addon messages and retries when messaging is throttled or temporarily locked down.

Profiles are opt-in. Offline viewing works only when your addon received the guildmate's SimC export while both of you were online, or when their addon replied to your sync after you logged in. The displayed timestamp shows when the export was captured; it may be older than their current gear or build. There is no central server or automatic way to retrieve a character's state after everyone with a copy has logged off. Up to 50 SimC profiles are retained per installation.

Turning sharing off sends an online revocation message. Members who were offline may retain a previously cached copy until a future sync or cache cleanup. Do not use the addon as a private data channel.

## Architecture

The addon uses Blizzard's guild and whisper addon-message channels. Snapshot and SimC data are encoded, split into small parts, sent through a paced queue, and reassembled by the receiver. It does not use a server or reach the internet. Snapshots and last received SimC exports are stored per WoW installation in `EvilTwinsCCDB` SavedVariables. A future companion app could provide an off-game dashboard.

## Development notes

This first version targets Retail interface `120100` (Midnight 12.1). The SimulationCraft integration uses its `SimulationcraftAPI.GetSimcProfile` interface, introduced in March 2026. The dependency is optional; browsing, builds, and wishlists work without it. No SimulationCraft source is bundled.

There is no automated in-game test environment in this repository yet. Test with two guild characters before wider use: enable sharing on both, verify snapshot exchange, add and remove wishlist items, copy a talent string, confirm the SimC timestamp appears, log one character off, view its last SimC from the other, and request a fresh profile when both are online.
