# Evil Twins Command Center

A Retail WoW guild addon for browsing opt-in character snapshots, gear wishlists, talent exports, and SimulationCraft profiles.

## Install

Copy the `EvilTwinsCommandCenter` folder into:

`World of Warcraft/_retail_/Interface/AddOns/`

Reload the UI or restart WoW, then type `/etcc` or `/eviltwins`.
Run `/etcc test` for a quick in-game snapshot and transfer encoding check.

Each member who wants to publish a character checks **Share my gear, build, and wishlist**. Gear and talent data are captured from that character and shared with guildmates running the addon. To answer SimC requests, also check **Allow guild SimC requests** and install the current [SimulationCraft addon](https://github.com/simulationcraft/simc-addon). SimC transfers include equipped gear only; bag and bank contents are excluded.

## What version 0.1 does

- Shows snapshots from guildmates who have opted in and been online while another addon user was online. Previously received snapshots remain available offline in SavedVariables.
- Shows equipped item links, equipped item level, current specialization, and the active talent export string.
- Lets each character maintain up to 20 wishlist items, entered as an item link or item ID with an optional note. Wishlists are manual; the addon does not infer upgrades.
- Requests a fresh SimulationCraft profile from an online guildmate who enabled SimC sharing. The received profile opens in a copyable window.
- Sends small, paced addon messages and retries when messaging is throttled or temporarily locked down.

Profiles are opt-in. Turning profile sharing off sends an online revocation message; members who were offline may retain a previously cached copy until a future sync or cache cleanup. Do not use the addon as a private data channel.

## Architecture

The addon uses Blizzard's guild and whisper addon-message channels. Snapshot and SimC data are encoded, split into small parts, sent through a paced queue, and reassembled by the receiver. It does not use a server or reach the internet. Snapshot data are stored per WoW installation in `EvilTwinsCCDB` SavedVariables. A future companion app could provide an off-game dashboard.

## Development notes

This first version targets Retail interface `120100` (Midnight 12.1). The SimulationCraft integration uses its `SimulationcraftAPI.GetSimcProfile` interface, introduced in March 2026. The dependency is optional; browsing, builds, and wishlists work without it. No SimulationCraft source is bundled.

There is no automated in-game test environment in this repository yet. Test with two guild characters before wider use: enable sharing on both, verify snapshot exchange, add and remove wishlist items, copy a talent string, and request a SimC profile with SimulationCraft installed on the sender.
