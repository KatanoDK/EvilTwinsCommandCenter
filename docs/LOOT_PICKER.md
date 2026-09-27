# Wishlist loot picker

Open your own character's **Wishlist** tab and click **Browse boss / dungeon loot**. Other characters' wishlists remain read-only. Character tools continue to require guild ranks 0–5.

Choose **Raids** or **Mythic+ dungeons**, then a source, boss (or all bosses), difficulty, and loot filter. Raid sources come from the newest Adventure Guide expansion tier. Dungeon sources come from the game's active challenge map table and are matched across all journal tiers, so returning dungeons appear too. The default filter is your current specialization; choose **All classes** for off-spec items and shared tokens. Search accepts item names or slot names.

Click **Add** beside an item. The item and its raid/dungeon/boss source are saved in your own wishlist, then shared through the existing snapshot system. Already-added items are marked **Added**. The wishlist retains its 20-item limit and rejects duplicates. Manual item links and IDs remain supported.

The picker reads Blizzard's Adventure Guide loot, not a downloaded item catalogue. Missing item names are requested from the game and refreshed when the data arrives. Open the guide once if journal data is unavailable. The picker asks you to close an open Adventure Guide and restores its tier, difficulty, loot filters, and remembered instance/boss after queries. Browsing is unavailable in combat.

Entries represent an item ID, not an exact item level, bonus list, or upgrade track. Difficulty changes the displayed journal preview; it does not lock the wishlist to that difficulty. Mythic+ falls back to Mythic base loot if a dungeon has no Keystone journal preview. No loaded drops is reported explicitly; the picker does not invent missing seasonal data.

## Carried gear and simulations

SimC exports now include equipment from backpack/bags as commented alternatives under `### Gear from Bags`, matching the upstream collector's format for Raidbots Top Gear. Consumables, general inventory, and bank items are excluded from that section. Equipped slots remain the active SimC configuration. Identical alternatives are deduplicated by their complete item options; different variants remain separate. Missing carried gear data blocks an incomplete export until it loads.

The official collector is still invoked with `noBags=true` to avoid its broader bag/bank scan, then our carried-only section is appended. The native exporter uses the same section. Bag changes schedule a new shared SimC export. Previously cached exports retain the data captured at their timestamp; request a fresh export from an online guildmate to receive the new format. The addon does not run a simulation or decide upgrades from item level alone.

## Layout direction

The main character panel stays unchanged. Navigation uses attached tabs with a selected edge, rather than action buttons. Crafting uses profession headers, counts, indented recipe rows, selected crafter highlighting, and opaque panels. Known recipes are shown by default; **All scanned recipes** includes unlearned recipes. Search opens matching groups without losing your earlier expansion choices. Crafting and loot lists recycle visible rows, removing the former 200-recipe display cap.

Useful next ideas are an officer filter for only missing prep requirements, clearer snapshot-age badges, and a guild loot view that shows everyone wanting a selected drop. These are suggestions, not additional features in this build.

Research references: [WeakAuras options and its group/scroll interface](https://github.com/WeakAuras/WeakAuras2/blob/main/WeakAurasOptions/WeakAurasOptions.lua), [Blizzard's Adventure Guide implementation](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_EncounterJournal/Mainline/Blizzard_EncounterJournal.lua), [journal item API](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_APIDocumentationGenerated/EncounterJournalDocumentation.lua), [challenge map API](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_APIDocumentationGenerated/ChallengeModeInfoDocumentation.lua), and [SimulationCraft bag export format](https://github.com/simulationcraft/simc-addon/blob/master/core.lua). The addon does not add any of these addons as a dependency.
