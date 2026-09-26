# Evil Twins Command Center

A Retail WoW guild addon for browsing character snapshots, raid prep, gear wishlists, talent exports, bags, and SimulationCraft profiles.

## Install

Copy the `EvilTwinsCommandCenter` folder into:

`World of Warcraft/_retail_/Interface/AddOns/`

Reload the UI or restart WoW, then type `/etcc` or `/eviltwins`.
Run `/etcc test` for a quick in-game snapshot, bag, and transfer encoding check.

Installing and enabling this addon is the opt-in. It automatically captures and shares the character's gear, build, wishlist, and carried bags with online guild members at ranks 0–5 who run it. There are no sharing checkboxes or prompts. If the current [SimulationCraft addon](https://github.com/simulationcraft/simc-addon) is also installed, its equipped-gear export is shared automatically. SimC transfers include equipped gear only. The current SimulationCraft switch combines bags and bank, so the addon shares carried bags separately and excludes bank data.

## What version 0.5 does

- Gives guild ranks 0 through 5 (Guild Master through rank 5) access to guild browsing, SimC requests, bags, wishlists, and raid prep. Lower ranks with the addon share their own data automatically but cannot browse guild data or send leader requests. The cutoff is currently fixed at rank index 5.
- Shows snapshots from guildmates who had the addon enabled while a leader with the addon was online. Previously received snapshots remain available offline in the leader's SavedVariables.
- Shows equipped item links, equipped item level, current specialization, and the active talent export string.
- Lets eligible leaders maintain up to 20 wishlist items on their own character, entered as an item link or item ID with an optional note. Wishlists are manual; the addon does not infer upgrades.
- Publishes a SimC export on login and after gear or build changes when the SimulationCraft addon is available. Online leaders running the addon save the latest export locally, with its capture time.
- Publishes carried bags on login and after bag changes. A leader can select a character and click **View bags** to see the last received item links and quantities. Bank contents are not scanned or shared.
- Lets you select a guildmate and click **View last SimC** to copy their latest received export, even when they are offline. **Request fresh** asks their addon for a new export while they are online.
- Gives guild officers a **Guild raid prep** overview: each roster character's last known enchant, gem, repair, and supply status in one list. Members with no shared snapshot appear as **No data**. Click a character to see missing equipped slots, missing enchants, empty sockets, gear below 80% durability, and carried supply counts.
- Adds **Refresh guild gear** to that overview. Online guild members with the addon capture and send their current gear to the requesting leader automatically in the background. Replies are spaced out, and the overview shows the capture time and number of fresh snapshots received. Offline members retain their last known state.
- Provides local **Supply settings** for an editable checklist. Enter acceptable item IDs and a minimum carried count for flask, food, combat potion, health potion, augment rune, and weapon enhancement. IDs within a category are alternatives and their bag counts are added together. An empty ID list leaves that category unconfigured. Settings stay on the officer's installation.
- Sends small, paced addon messages and retries when messaging is throttled or temporarily locked down.

Offline viewing works only when a leader's addon received the guildmate's snapshot, SimC export, or bag snapshot while both were online, or when the guildmate's addon replied to a leader sync. Each timestamp shows when its data was captured; it may be older than their current state. Supply counts require a complete bag snapshot. The report checks enchant presence, not whether an enchant is optimal for a specialization, and checks carried consumables rather than active buffs. There is no central server or automatic way to retrieve a character's state after everyone with a copy has logged off. Up to 50 SimC profiles are retained per installation.

Disabling or uninstalling the addon stops new captures and replies. A leader who already cached a snapshot may retain it until cache cleanup; uninstalling cannot revoke those copies. Earlier addon versions may still broadcast to the whole guild until upgraded.

## Architecture

The addon uses Blizzard's guild and whisper addon-message channels. Guild requests use the guild channel; shared snapshot, bag, and SimC data are sent as whispers to eligible online leaders. Data are encoded, split into small parts, sent through a paced queue, and reassembled by the receiver. Recipients check the sender's current guild rank before answering leader requests. Lower ranks discard and clear cached guild data from older addon versions. The addon does not use a server or reach the internet. Snapshots, local supply settings, and last received bags and SimC exports are stored per WoW installation in `EvilTwinsCCDB` SavedVariables.

## Development notes

This version targets Retail interface `120100` (Midnight 12.1). The SimulationCraft integration uses its `SimulationcraftAPI.GetSimcProfile` interface, introduced in March 2026. The dependency is optional; browsing, builds, bags, and wishlists work without it. No SimulationCraft source is bundled.

There is no automated in-game test environment in this repository yet. Test with a rank 5 or higher leader and a lower-rank member before wider use: enable the addon on both, verify the member sees only the syncing status, verify the leader sees the member's snapshot and bags, press **Refresh guild gear** and check the capture time changes without any member interaction, then test cached and fresh SimC while both are online and after one logs off. Also test that a leader's rank change removes access.
