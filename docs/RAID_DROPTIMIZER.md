# Raid DPS upgrades

The standalone local simulator screen has been replaced with a raid workflow. Collect the raid once, run a separate Droptimizer for each ready DPS player, and compare how much each drop helps the players who can use it.

## Collect the raid in WoW

1. Join the raid on a guild character with leader access (rank indices 0–5).
2. Open `/etcc` → **Build & SimC → Raid DPS export**.
3. Click **Refresh raid SimC** outside combat. Online guildmates with the addon respond automatically. There are no member prompts.
4. Wait for profiles to arrive and check the capture times. Tanks/healers are skipped; missing profiles, unavailable guild data, and players outside the guild are listed explicitly. Members without the addon cannot provide an export.
5. Click **Copy raid export**, then Ctrl+A / Ctrl+C in the copy window.
6. In the desktop app, open **Raid upgrades → Import raid export** and paste the code.

The group list comes from the current raid, with up to 40 members. A ready entry must contain a recognized DPS specialization from its exported profile. Cached profiles are allowed and retain their capture time; collection does not claim they are current. Raid exports contain the equipped baseline, active build, supported racial/consumable fields and Omnium traits. Comment-only bag alternatives are omitted to keep the clipboard export small. Normal individual SimC exports still contain carried gear.

Re-import after gear/talent changes. Results are retained for profiles whose active input is unchanged; changed or departed players lose their previous results from the active comparison.

## Optional local queue

1. Extract a current official [Windows SimulationCraft download](https://www.simulationcraft.org/download.html). Click **Choose engine** and select `simc.exe`. The engine location from the previous simulator is retained.
2. Click **Load loot data**. The first load needs the internet; a complete validated snapshot is cached for 24 hours. Public data comes from [Raidbots' developer data](https://www.raidbots.com/developers), downloaded anonymously. Character exports stay on the PC during local runs.
3. Choose a supported raid, LFR/Normal/Heroic/Mythic, and screening (1,000), standard (5,000), or detailed (20,000) iterations.
4. Click **Queue local Droptimizers**. Each player gets a baseline and one-item alternatives. Characters run sequentially; finished results appear immediately. A failed character does not stop the remaining queue.

Local generation is currently verified for **Midnight Season 2 / The Venomous Abyss**. It includes trash, class/spec-appropriate armor and weapons, tier-token rewards, catalyst alternatives and omni-token slot-level offsets. Boss sequence levels and the season's Veteran/Champion/Hero/Myth tracks select the drop level. Very rare drops use the next sequence level. Unknown season rules are rejected and can be handled by importing completed Raidbots reports.

This uses the same CPU engine as a basic local simulation and is not a speed shortcut. Testing more items increases the work. The real-engine smoke test used official 1210-01 build `7ffaabf`, 1,000 iterations, and 56 candidates for an Unholy Death Knight; it took roughly 83 seconds on this PC. Other specs, loot counts, precision and computers differ. Background mode uses half the available CPU threads at below-normal process priority. The queue uses 300-second Patchwerk, one target, standard raid buffs, 95% confidence and the engine's default class rotation. The progress estimate is approximate and can change as different classes run.

**Stop queue** terminates the active engine process and keeps finished results. Clicking the queue button again retries remaining/failed players if the source, difficulty and precision match. Changing those settings starts a new comparison. App restart updates wait until the queue/download is stopped or finished.

### What the local results assume

- Drops are tested at their original loot level. Equipped gear remains the exported baseline. Bags and equipped-item upgrade tracks are not optimized.
- Existing enchants for the replaced slot are retained. Existing gems are copied only into intrinsic sockets on the candidate. No extra sockets or automatic enchant/gem optimization are added.
- Rings/trinkets are tried in both slots, and repeated copies of a unique item are excluded. Supported weapon swaps retain an existing compatible pair; two-handed main-hand swaps empty the off hand when appropriate. Staff-to-one-hand transitions that require a second new weapon are skipped and listed in the player's notes.
- One drop is tested at a time. A tier token can test each usable reward; the strongest reward/slot for that drop and item level is retained. This is a drop priority estimate, not an optimized multi-drop gear set or a full reproduction of Raidbots' settings.
- Augmentation uses the engine's single-player model; full raid contribution is not represented. Engine implementation warnings are retained.
- Only positive gains are shown, with up to 100 per player. An asterisk marks gains within the combined simulation uncertainty, including unknown uncertainty. Screening results should be checked with more iterations before close loot decisions.

## Using Raidbots instead

Select a ready player, click **Copy player's SimC**, open **Raidbots**, paste the export into Droptimizer and run it there. Then click **Import player's report** and paste the completed report link. Repeat for each DPS player. This app does not automatically submit jobs to Raidbots.

The report must match the collected player's name, realm, region, specialization, equipped gear and talent string. Use the same loot sources/difficulty and fight style/duration/target count across the raid; incompatible comparisons are rejected. More advanced Raidbots settings, such as upgraded equipped baselines or gem/enchant optimization, should also be consistent across players. Results do not include players with failed or missing simulations.

## Read and share results

**Raid group** shows availability, individual errors and each player's best positive gain. Double-click a ready player to open their loot results. **Loot priorities** gives the result list more room, filters by player and searches item names, boss names or drop IDs. It sorts by absolute DPS gain to help compare the benefit of a drop across players; percentages and uncertainty remain visible. Hover an item for report settings, source and conversion details.

Select a completed result and click **Copy addon report**. In WoW, paste it into **Wishlist & upgrades → Import report**. The existing leader report sharing then distributes it to online eligible officers. Local reports have no Raidbots web link, so **Copy report link** is disabled for them. Results remain separate from manual wishlists.

The active raid session and completed results are saved under `%LOCALAPPDATA%/EvilTwinsUpdater/raid-upgrades/`. Local run subfolders retain input, JSON, HTML and console logs. They are not written to WoW's folders or SavedVariables. These files can contain character/build information; delete the saved session/run folders when no longer needed.

## Verification

`dotnet run --project updater/EvilTwinsUpdater.Tests -c Release -- .test-work/install-tests` covers raid codec boundaries, a Lua-generated export, 40-member groups, DPS eligibility, identity/spec matching, unsafe input rejection, current loot levels, trash, tier-token conversions and weapon restrictions, alongside the existing process/cancellation tests. Lua tests cover automatic requests, cache timestamps, GUID checks, combat/rank gates and export size reduction.

For a real-engine smoke test using the committed public loot fixture and an Unholy baseline:

```powershell
dotnet run --project updater/EvilTwinsUpdater.Tests -c Release -- --verify-raid C:/SimulationCraft/simc.exe C:/Exports/unholy.simc .test-work/raid-real-engine
```

The export's character name should be `Fixture` (the official `MID2_Death_Knight_Unholy_Rider` example is renamed automatically). The smoke test runs the generated candidates and verifies progress and addon report encoding. UI checks with a fixture engine additionally exercised a 40-member grid, failures, filters, restart persistence, resume and cancellation. A real raid still needs an in-game collection/sharing test.
