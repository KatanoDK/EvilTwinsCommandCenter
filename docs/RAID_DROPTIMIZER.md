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

1. Open **Manage engine → Install / update SimulationCraft**. The app finds the latest successful official Windows x64 nightly, downloads and verifies it before extraction, selects `simc.exe`, and saves the selection. No login or separate 7-Zip installation is needed. Clicking again checks for a newer nightly and reuses an intact verified current build. **Choose engine** remains available for an existing extracted download; the engine location from the previous simulator is retained until you select or install another engine. Close engine settings to return to the compact build status; hover that status to see the full path.
2. Click **Load loot data**. The first load needs the internet; a complete validated snapshot is cached for 24 hours. Public data comes from [Raidbots' developer data](https://www.raidbots.com/developers), downloaded anonymously. Character exports stay on the PC during local runs.
3. Choose a supported raid and LFR/Normal/Heroic/Mythic. Precision defaults to **Automatic: 1k → 5k**; fixed screening (1,000), standard (5,000), and detailed (20,000) modes remain available. Under **CPU usage**, choose **Background · half threads** to leave capacity for other apps, or **Full speed · all threads** to use every available CPU thread at normal priority.
4. Check the DPS players you want in the roster's **Run** column, then click **Run checked players**. Ready DPS start checked; **Check all DPS** and **Clear** affect all players, including those hidden by a roster filter. Each player gets a baseline and one-item alternatives. Characters run sequentially; finished results appear immediately. A failed character does not stop the remaining queue. Roster filters change only what you see.

Local generation is currently verified for **Midnight Season 2 / The Venomous Abyss**. It includes trash, class/spec-appropriate armor and weapons, tier-token rewards, catalyst alternatives and omni-token slot-level offsets. Boss sequence levels and the season's Veteran/Champion/Hero/Myth tracks select the drop level. Very rare drops use the next sequence level. Unknown season rules are rejected and can be handled by importing completed Raidbots reports.

This uses the same CPU engine as a basic local simulation. Testing more items increases the work. The earlier real-engine smoke test used official 1210-01 build `7ffaabf`, 1,000 iterations, and 56 candidates for an Unholy Death Knight; it took roughly 83 seconds on this PC. That was a fixed screening run, not a benchmark of automatic refinement. Other specs, loot counts, precision and computers differ. Background mode uses half the available CPU threads at below-normal process priority; Full speed uses all available threads at normal priority and can make WoW or other apps less responsive. The queue uses 300-second Patchwerk, one target, standard raid buffs, 95% confidence and the engine's default class rotation. The progress estimate is approximate and can change as different classes run.

Automatic mode first screens every candidate at 1,000 iterations. A drop is omitted from refinement only when its estimated gain plus twice the combined comparison error is below zero. Unknown uncertainty always survives. If any alternative for a drop survives, all its tested slot/conversion variants are retained. Those retained candidates and a fresh baseline are then run at 5,000 iterations. Only positive gains from that final pass are published; screening and refined estimates are not mixed. A screen with no survivors finishes without a second engine run.

This is a conservative screening heuristic, not a guarantee against missing a small upgrade. Remaining uncertain gains stay marked with an asterisk. Use fixed Standard or Detailed mode to test every candidate at higher precision. Automatic mode can save work when many drops are clearly worse; it adds a screening pass and can take longer than fixed Standard if nearly everything survives. Reports disclose the screening and refinement counts. A failed or cancelled refinement is not saved as a completed player result.

**Pause after player** lets the active player's screening/refinement finish and then waits before starting another player. **Resume queue** continues it; **Keep running** cancels a pause request while the active player is still running. Select a waiting player and click **Run next** to move them to the front without interrupting the active player. Queue positions appear in the roster. **Stop queue** terminates the active engine process (or exits a paused queue) and keeps finished results.

**Retry failed** retries failed DPS players, including unchecked players, while retaining completed results. It requires the same settings as the completed comparison. **Run checked players** skips checked players who already have matching results and starts unfinished players in their saved waiting order. Changing the source, difficulty or precision, including automatic versus fixed precision, starts a new comparison. Changing only CPU mode preserves completed results. Checked players and unfinished order survive restart; queues never restart automatically. Precision and CPU choices are remembered with the local layout. App restart updates wait until the queue/download is stopped or finished.

Engine updates run only when you click **Install / update SimulationCraft**. Downloads show progress and a **Stop download** button. A local queue or another operation disables engine replacement; app restarts wait for the download to finish or stop. Failed or cancelled downloads leave the selected engine intact. Managed installations keep the current engine and one previous copy under `%LOCALAPPDATA%/EvilTwinsUpdater/raid-upgrades/engines/`; manually selected installations are not modified.

The updater uses the official `simulationcraft/simc-publish` GitHub Actions nightly builds, also linked from [SimulationCraft's download page](https://www.simulationcraft.org/download.html). It gets the build identity and SHA-256 digest directly from GitHub's public API over HTTPS, selecting the Windows x64 artifact from a successful scheduled or manually dispatched nightly on the official publishing branch. [nightly.link](https://github.com/oprypin/nightly.link) provides anonymous HTTPS access to that exact artifact ID; it does not supply the trusted checksum. No GitHub account, token, account uploads or background polling are used.

The complete downloaded ZIP must match GitHub's independently fetched SHA-256 digest before the updater reads its contents. HTTP, invalid TLS certificates, unapproved redirects, missing checksums and checksum mismatches are rejected, with no HTTP fallback. ZIP/7z size limits, archive-path checks, Windows x64 executable validation and staging still apply. The current engine is replaced only after verification and extraction succeed. Service outages or anonymous API rate limits leave the selected engine intact and can be retried later. Cached installations from the old HTTP downloader are downloaded again through this verified path on the next successful update. The trust still rests on the official project's build process and GitHub; checksum verification does not claim that software can never contain a bug or malicious upstream change.

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

**Raid group** puts ready DPS first and shows readiness counts, status badges and each player's best positive gain. Filter to **Ready DPS**, **Missing profiles**, or **All players**; tanks and healers appear subdued. The selected-player summary keeps copy actions together. Click **Details** for assumptions, warnings or the full failure message. Double-click a ready player to open their loot results. **Loot priorities** gives the result list more room, filters by player and searches item names, boss names or drop IDs. It sorts by absolute DPS gain to help compare the benefit of a drop across players; percentages and uncertainty remain visible. Hover an item for report settings, source and conversion details.

**Boss / drop** filters by boss and a specific drop, difficulty and item level. It ranks reported positive gains across the raid, marks uncertain gains, shows the reward/conversion for each player, and keeps missing profiles, waiting players and failures visible without inventing a DPS value. Equal gains share a rank. Close or uncertain estimates do not establish a clear winner. With current loot data loaded, known incompatible candidates are excluded using each ready player's loot plan. Without those plans, only eligibility demonstrated by a reported gain is certain. Missing profiles always have unknown eligibility. A completed report that does not list the drop says **No positive gain reported**; this does not mean zero gain, since reports are limited to positive entries and automatic screening can omit candidates. Different fight settings are not ranked together.

Drag the horizontal divider below the player list down to make the list taller, or up to give the local simulation controls more room. Those controls scroll independently when space is limited. **Hide local controls** expands the list to the full available height; **Show local controls** brings them back. The app remembers the divider position and collapsed state. **Loot priorities** and **Boss / drop** always use the full height.

Select a completed result and click **Copy addon report**. In WoW, paste it into **Wishlist & upgrades → Import report**. The existing leader report sharing then distributes it to online eligible officers. Local reports have no Raidbots web link, so **Copy report link** is disabled for them. Results remain separate from manual wishlists.

The active raid session and completed results are saved under `%LOCALAPPDATA%/EvilTwinsUpdater/raid-upgrades/`. Local run subfolders retain input, JSON, HTML and console logs. They are not written to WoW's folders or SavedVariables. These files can contain character/build information; delete the saved session/run folders when no longer needed.

## Verification

`dotnet run --project updater/EvilTwinsUpdater.Tests -c Release -- .test-work/install-tests` covers raid codec boundaries, a Lua-generated export, 40-member groups, DPS eligibility, identity/spec matching, unsafe input rejection, current loot levels, trash, tier-token conversions and weapon restrictions. It also covers player-boundary pause/resume, priority changes, cancellation while paused, drop grouping/ranking, uncertainty, eligibility, reports with multiple sources and incompatible fight settings, alongside the existing process/cancellation tests. Lua tests cover automatic requests, cache timestamps, GUID checks, combat/rank gates and export size reduction.

For a real-engine smoke test using the committed public loot fixture and an Unholy baseline:

```powershell
dotnet run --project updater/EvilTwinsUpdater.Tests -c Release -- --verify-raid C:/SimulationCraft/simc.exe C:/Exports/unholy.simc .test-work/raid-real-engine
```

The export's character name should be `Fixture` (the official `MID2_Death_Knight_Unholy_Rider` example is renamed automatically). The smoke test runs the generated candidates and verifies progress and addon report encoding. Offscreen UI checks with a fixture engine additionally exercised sidebar/startup order, a 40-member grid, checked-player selection, failures, boss/drop filters and ranks, pausing after a player, changing queue priority, failed-only retry, restart persistence, cancellation and minimum-window layouts. A real raid still needs an in-game collection/sharing test.
