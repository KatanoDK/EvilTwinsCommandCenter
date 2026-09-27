# Raid prep

The character sidebar stays the same. The right side has three tabs:

- **Raid prep:** current enchant and gem names, missing or unavailable data, repairs, missing gear, and carried supplies.
- **Build & SimC:** copy talents, copy the cached SimC export, and generate/request a fresh export including carried gear alternatives for Top Gear.
- **Wishlist:** view a character's wanted items or edit your own list, with a raid boss / Mythic+ loot picker.

The selected character summary and tabs stay visible when scrolling. Guild raid prep and the crafting directory remain available from the window header. Guild ranks 0–5 retain access to character tools; all ranks retain the crafting directory.

## Enchants and gems

The readiness table includes enchantable slots and items with sockets. Slots with unavailable socket data are shown as unknown until game data finishes loading. Off-hand weapons need an enchant; shields and held off-hand items do not. Missing items in enchantable slots remain visible.

Enchant IDs come from the saved item link. Names come from Blizzard's structured item tooltip. Gem IDs and names come from the item API; uncached names temporarily use IDs. Green means present, red means missing, and amber means unavailable. Hover a row for the equipped item and full enchant/gem text. Presence checks do not choose the best enchant or gem for a specialization.

## Automatic supplies

Every category requires **one** acceptable carried item: flask, food, combat potion, health potion, augment rune, and temporary weapon enhancement. Quantities from acceptable alternatives are added together. The catalogue accepts all listed qualities and cauldron/fleeting variants; mana and defensive combat potions are included. Older-expansion items, crafting phials, and utility invisibility potions do not satisfy these requirements.

`Consumables.lua` bundles the Midnight 12.1 item IDs, including Season 2 additions. Guild officers do not enter item IDs or configure quantities. Item names are looked up automatically from shared links and game data. The addon cannot browse websites; catalogue additions ship through normal addon updates. Keep that file current when a patch adds consumables or the expansion changes.

Missing/incomplete bags are **Unknown**, rather than missing supplies. Snapshots older than 24 hours are **Stale**. This checks carried inventory, not active buffs or whether a particular item is optimal for the character. Existing manual supply checklists are retired at login.

## Data references

The catalogue was checked on September 26, 2026 using [SimulationCraft's extracted Midnight item data](https://github.com/simulationcraft/simc/blob/midnight/engine/dbc/generated/item_data.inc). Supplemental variants were checked against Wowhead's collected item data, including [Silvermoon Health Potion](https://www.wowhead.com/item=241304/silvermoon-health-potion), [Concentrated Silvermoon Health Potion](https://www.wowhead.com/item=271883/concentrated-silvermoon-health-potion), [Thalassian Phoenix Oil](https://www.wowhead.com/item=243733/thalassian-phoenix-oil), [Oil of Dawn](https://www.wowhead.com/item=243735/oil-of-dawn), and [Refulgent Weightstone](https://www.wowhead.com/item=237369/refulgent-weightstone).

Blizzard API references: [item functions](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_APIDocumentationGenerated/ItemDocumentation.lua) and [tooltip line types](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_APIDocumentationGenerated/TooltipInfoSharedDocumentation.lua).

Automated tests exercise mocked item APIs, supply variants, incomplete/stale bags, tab visibility, rank restrictions, and large rosters. In-game appearance and cache loading still need guild testing.
