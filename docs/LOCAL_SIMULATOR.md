# Local CPU simulator

The installed updater's **Local simulator** tab runs DPS simulations on your computer using SimulationCraft. No Raidbots subscription or login is needed. It can simulate equipped gear or compare combinations of selected armor, rings and trinkets from the character's bag export. Local results are saved as HTML/JSON reports; they do not yet produce addon import codes.

## Set up once

1. Update the installed app with **Check / update now → Update app & restart**.
2. Open **Local simulator → Get SimulationCraft**, or visit the [official download page](https://www.simulationcraft.org/download.html). Download the current Windows x64 engine for your WoW version and extract the entire archive into a permanent folder.
3. Click **Choose engine** and select **simc.exe** inside the extracted folder. This is the command-line engine, separate from **SimulationCraft.exe**, the graphical application. The app remembers your selection.

The engine is a separate download and is not bundled with our installer. App self-updates preserve the selected engine path but do not update SimulationCraft itself. Keep its build current after game patches and hotfixes. If you move or replace its folder, select the new `simc.exe`.

## Run a character

1. In the addon, copy the character's latest SimC export from **Build & SimC**. Guild leaders can use an export shared by another character.
2. Paste it into **Character export** in the desktop app.
3. To compare alternatives, open **Bag gear → Load bag items**, then check the variants you want to test. The selection shows its combination count before running. Leave the selection empty for an equipped-only simulation.
4. Choose **Background** or **Full speed**, iteration count, average fight duration, and target count.
5. Click **Run simulation**. The engine output, percentage, completed/total runs, elapsed time and estimated time remaining update while it runs. The estimate starts after usable engine progress arrives and changes with actual run speed; reporting takes additional time after the simulations finish. **Cancel** stops the engine. Closing the app also stops an active run; minimizing keeps it running.
6. Read **Bag comparisons** for ranked combinations and **Result / engine output** for baseline DPS, confidence interval and engine notes. Use **Open report** for the HTML report or **Open run folder** for the input, JSON result and console log.

## Compare multiple items

Checked bag items form a pool alongside the equipped choices. The app generates every supported combination within that pool, including changing several slots at once. For example, a new helm and a new chest produce three comparisons: helm only, chest only, and both together, plus the equipped baseline. Results therefore include interactions such as set bonuses, rather than adding individual item gains together.

You can select up to **20 bag variants**, with a maximum of **500 combinations plus the baseline** per run. If a selection exceeds that limit, the app explains it and disables running until you narrow the pool. It never silently truncates the combinations. Large selections take longer: the iteration count applies to each gear set, not the whole batch.

Rings and trinkets are considered in both slots. Combinations that would equip the same item ID twice in those paired slots are excluded. Exact duplicate variants collapse, while different bonus IDs, gems, enchants and other exported item fields remain distinct. Changing the pasted export clears the selection and old results so a different character cannot inherit them. Only `### Gear from Bags` sections are read; bank, vault, merchant and linked-item sections are excluded.

Each row shows the changed items/slots, DPS gain, percentage gain, and **Gain ±**, the combined uncertainty of the baseline and gear-set results at the reported confidence level. Gains inside that uncertainty are marked accordingly. Hover the item cell to see exact item variants, total DPS and iterations. The best tested combination can still lose to equipped gear; the baseline remains the reference for every row.

This version uses the exported gems and enchants; it does not optimize or normalize them. Weapon alternatives appear as unavailable because exports lack the hand restrictions needed to safely build weapon pairs. Unique-equipped category limits and embellishment limits are not yet enforced across combinations: check them before equipping a suggested set. These are comparisons within the selected pool, not a search over all obtainable gear.

**Background** uses half the available CPU threads (at least one) and below-normal process priority. **Full speed** uses all available CPU threads at normal priority. Background mode reduces contention but does not guarantee that WoW's performance is unaffected. Results depend on the character and settings; CPU modes change resource use, not the combat assumptions.

Iteration choices are 1,000, 5,000, and 20,000. Fight duration choices are 180, 300, and 600 seconds, with 1, 3, or 5 targets. Simulations use **Patchwerk**, standard raid buffs, the engine's default class rotation, and fight length varying by 20% around the chosen duration. The engine's reported confidence interval and notes are displayed alongside the result. Simulation support varies by specialization and mechanics; a successful DPS result is not a healing or tank-survivability evaluation.

Exports are restricted to one character's identity, talents, equipped gear, and supported consumable/racial fields. File includes, Armory imports, output directives, user-supplied profile sets, and custom action lists are rejected. The app generates its own bounded profile sets from validated bag selections. Gear data is resolved through the engine's local item database. Missing or unsupported data is reported as an error rather than silently fetched or substituted.

Runs are saved separately under `%LOCALAPPDATA%/EvilTwinsUpdater/simulations/`. They do not modify WoW, live SavedVariables, or another run. Input exports and results stay on your computer; this feature does not upload them. Run folders are retained until you delete them. App restart updates are unavailable while a simulation is running.

## Developer checks

The updater regression suite uses a fixture engine process to verify exported-profile restrictions, class and slot aliases, CPU modes, multi-slot combinations, ring pairs, exact variants, bag-only parsing, selection/combination limits, JSON matching and confidence-interval parsing, chunked engine progress and ETA, engine notes, nonzero exits, retained run files, and cancellation after the process has actually started.

To benchmark a real extracted engine with an addon export:

```powershell
dotnet run --project updater/EvilTwinsUpdater.Tests -c Release -- --verify-local-sim C:/SimulationCraft/simc.exe C:/Exports/character.simc .test-work/local-sim-benchmark
```

This runs the same 1,000-iteration profile in both CPU modes and prints elapsed time, DPS uncertainty, and engine version. If the export includes supported bag alternatives, it selects them all and also verifies every combination and completed engine progress. The smoke tests used official 1210-01 build `7ffaabf`, with its Unholy Death Knight sample reduced to character/gear fields so the default rotation was generated by the engine. The initial equipped-only test took 3.67 seconds in background mode and 1.16 seconds at full speed on a machine with 16 CPU threads. A two-slot comparison (baseline plus three gear sets) took 5.64 and 4.46 seconds respectively. These are individual sample runs, including startup/reporting time, not performance guarantees or controlled scaling benchmarks.
