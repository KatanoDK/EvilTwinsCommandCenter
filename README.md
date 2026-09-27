# Evil Twins Command Center

A Retail WoW guild addon for browsing a shared crafting directory, character snapshots, raid prep, gear wishlists, talent exports, bags, and SimulationCraft profiles.

## Install

Copy the `EvilTwinsCommandCenter` folder into:

`World of Warcraft/_retail_/Interface/AddOns/`

Reload the UI or restart WoW, then type `/etcc` or `/eviltwins`.
Run `/etcc test` for a quick in-game snapshot, bag, crafting, and transfer encoding check.

Installing and enabling this addon is the opt-in. It automatically captures and shares the character's gear, build, wishlist, carried bags, and SimC export with online guild members at ranks 0–5 who run it. There are no sharing checkboxes or prompts. SimC works without a separate addon: the built-in exporter captures the active talent build and equipped gear. If the [SimulationCraft addon](https://github.com/simulationcraft/simc-addon) is installed and its collector works, its equipped-gear export is used. SimC exports exclude bag and bank items; carried bags are shared separately.

## What version 0.1.0 does

This is a development version. Keep the overall addon version at `0.1.0` during initial in-game testing; Git commits identify individual builds.

- Gives every guild rank access to **Crafting directory**. Opening a profession window automatically scans learned and unlearned recipes and shares the saved scan with guildmates who have the addon. Select a character to see what they have learned, or choose **All guild crafters** and search for a recipe to find who knows it. Each character needs to open each profession at least once. A profession that has not been scanned is unknown, not missing. A known recipe does not guarantee materials, quality, or other crafting requirements.
- Gives guild ranks 0 through 5 (Guild Master through rank 5) access to gear browsing, SimC requests, bags, wishlists, and raid prep. Lower ranks with the addon share their own data automatically and can browse the crafting directory, but cannot browse leader data or send leader requests. The cutoff for leader tools is currently fixed at rank index 5.
- Shows snapshots from guildmates who had the addon enabled while a leader with the addon was online. Previously received snapshots remain available offline in the leader's SavedVariables.
- Shows equipped item links, equipped item level, current specialization, and the active talent export string.
- Lets eligible leaders maintain up to 20 wishlist items on their own character, entered as an item link or item ID with an optional note. Wishlists are manual; the addon does not infer upgrades.
- Publishes a SimC export on login and after gear or build changes, using the installed SimulationCraft collector or the built-in exporter. Online leaders running the addon save the latest export locally, with its capture time.
- Publishes carried bags on login and after bag changes. A leader can select a character and click **View bags** to see the last received item links and quantities. Bank contents are not scanned or shared.
- Lets you select a guildmate and click **View last SimC** to copy their latest received export, even when they are offline. **Request fresh** asks their addon for a new export while they are online.
- Gives guild officers a **Guild raid prep** overview: each roster character's last known enchant, gem, repair, and supply status in one list. Members with no shared snapshot appear as **No data**. Click a character to see missing equipped slots, missing enchants, empty sockets, gear below 80% durability, and carried supply counts.
- Adds **Refresh guild gear** to that overview. Online guild members with the addon capture and send their current gear to the requesting leader automatically in the background. Replies are spaced out, and the overview shows the capture time and number of fresh snapshots received. Offline members retain their last known state.
- Provides local **Supply settings** for an editable checklist. Enter acceptable item IDs and a minimum carried count for flask, food, combat potion, health potion, augment rune, and weapon enhancement. IDs within a category are alternatives and their bag counts are added together. An empty ID list leaves that category unconfigured. Settings stay on the officer's installation.
- Sends small, paced addon messages and retries when messaging is throttled or temporarily locked down.

Offline viewing works only when an addon received a guildmate's data while both were online, or when the guildmate's addon replied to a sync request. Crafting scans are available to every rank; gear, SimC, and bags remain leader-only. Each timestamp shows when its data was captured; it may be older than their current state. Supply counts require a complete bag snapshot. The report checks enchant presence, not whether an enchant is optimal for a specialization, and checks carried consumables rather than active buffs. There is no central server or automatic way to retrieve a character's state after everyone with a copy has logged off. Up to 50 SimC profiles are retained per installation.

Disabling or uninstalling the addon stops new captures and replies. A leader who already cached a snapshot may retain it until cache cleanup; uninstalling cannot revoke those copies. Earlier addon versions may still broadcast to the whole guild until upgraded.

## Architecture

The addon uses Blizzard's guild and whisper addon-message channels. Guild requests use the guild channel; shared snapshot, bag, and SimC data are sent as whispers to eligible online leaders. Profession scans are sent to the guild, and saved scans are sent by whisper to newly online guildmates. Data are encoded, split into small parts, sent through a paced queue, and reassembled by the receiver. Recipients check guild membership before accepting craft data and check the sender's current guild rank before answering leader requests. Lower ranks discard and clear cached leader data from older addon versions. The addon does not use a server or reach the internet. Snapshots, profession scans, local supply settings, and last received bags and SimC exports are stored per WoW installation in `EvilTwinsCCDB` SavedVariables.

## Development notes

For private testing, run `python scripts/package_addon.py`. It creates `dist/EvilTwinsCommandCenter-0.1.0.zip` containing the installable addon folder and a build manifest. Send that ZIP directly to a guildmate through your private guild channel; they can extract its `EvilTwinsCommandCenter` folder into their Retail `Interface/AddOns` folder. No GitHub account is needed for a ZIP you send them. Packaging does not install the addon or touch a WoW folder.

The [Windows updater](docs/UPDATER.md) supports public downloads without a GitHub login. Choose a Retail folder once, click **Check / update now**, or leave automatic checks enabled. It updates this addon even while WoW is open and preserves SavedVariables. Type `/reload` in-game afterward to load the updated code. The repository is fixed in the app. Each successful push publishes a new development build in [Releases](https://github.com/KatanoDK/EvilTwinsCommandCenter/releases).

Lua syntax and crafting protocol checks can be run with `python tests/test_crafting.py` after installing the `lupa` Python package. These use mocked WoW APIs and do not replace an in-game UI and network test.

This version targets Retail interface `120100` (Midnight 12.1). The optional SimulationCraft integration resolves its local addon object from AceAddon's registry and supports both the `SimulationcraftAPI.GetSimcProfile` interface and the older instance method. The built-in exporter follows the [upstream equipped-item format](https://github.com/simulationcraft/simc-addon/blob/master/core.lua), including enchant IDs, gem IDs and bonuses, item bonus IDs, crafted stats and quality, drop-level/content-tuning modifiers, and redirected base stats. It exports the active talents and Midnight's Omnium traits. Other saved talent loadouts are not included in the built-in export. Legacy DISC belts require SimulationCraft's special-effect collector; the built-in exporter reports that requirement rather than guessing the belt effect.

All addon windows use solid opaque backgrounds, and newly shown windows are raised above overlapping windows in the same layer. After updating, test **Generate SimC** with SimulationCraft enabled and disabled, and paste the result into your usual simulator. The automated checks cover profile formatting and compatibility with mocked WoW APIs; they do not run an actual simulation.

There is no automated in-game test environment in this repository yet. Test with a rank 5 or higher leader and a lower-rank member before wider use: enable the addon on both, open a profession on each character, verify both ranks can browse the crafting directory and see learned and unlearned recipes, then verify the leader sees the member's snapshot and bags. Press **Refresh guild gear** and check the capture time changes without any member interaction, then test cached and fresh SimC while both are online and after one logs off. Also test that a leader's rank change removes leader access but retains the crafting directory.
