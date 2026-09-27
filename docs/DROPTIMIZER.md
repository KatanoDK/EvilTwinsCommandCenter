# Droptimizer upgrades

1. Run a Droptimizer simulation on Raidbots as usual.
2. Open the installed updater's **Droptimizer** tab, paste the completed report link, and click **Import report**.
3. Click **Copy addon import code**.
4. In WoW, open **Wishlist & upgrades → Import report**, paste the code, and click **Import & share**.

Ranks 0–5 can import a report for any current guild character in the same region. Reports automatically share with online guild leaders using the addon, and saved reports are exchanged when a leader comes online. Members do not need to approve requests. Reports remain readable offline after they have been received. The character needs a shared addon snapshot to appear in the character panel.

## What leaders see

- Up to 100 positive upgrades, ranked by estimated DPS gain and percentage against that report's baseline.
- Item icon, drop name, simulated item level, boss, dungeon, or raid trash source, and difficulty (for example, **Heroic**).
- A **Convert to tier** marker when the desired drop was simulated after conversion. The original drop ID and resulting tier ID are retained separately.
- Report date, specialization, fight style, duration, target count, and equipped upgrade/conversion assumptions.
- A warning when saved talents, gear, enchants, or gems differ, when a build match cannot be verified, or when the report is over a week old.
- **Copy report link** to review the full report and assumptions in a browser.

Manual wishlists remain separate below the simulated upgrades. A newer report replaces the older one for the same character, specialization, and source/difficulty scope. Other specializations and scopes remain separate. An empty positive-upgrade list is valid and replaces the old report for that scope. Each addon installation retains at most 100 reports, removing the oldest first.

Choose **Manual wishlist (hide report)** in the report dropdown to collapse the upgrade list and bring the wishlist into view. The report stays saved and can be selected again. The choice is remembered per character and specialization until a UI reload. Importing a report selects it again. Wishlist additions and removals are limited to your logged-in character; another guild character's wishlist is read-only.

## Limits

The importer uses completed report JSON at `https://www.raidbots.com/reports/<report-id>/data.json`. It does not submit simulations, need a Raidbots login, or bypass expired/private reports. This format is not a guaranteed public integration API; unexpected result formats are rejected rather than interpreted as upgrades. The integration was verified against the provided raid Droptimizer report. Other report types, healing/tank metrics, or older reports without structured character identity are rejected.

Gains apply to the simulated build and assumptions. They are not a best-in-slot declaration or a comparison of loot value between different players. A small gain within the report's statistical uncertainty is marked with `*`. Imported reports do not rerun when gear changes. Upgrade levels, enchants, gems, and conversions can require resources the character does not currently have; check the original report's assumptions. Owning the same item ID does not automatically complete an upgrade, because item level and variants can differ.

WoW addons cannot fetch the web report directly. The updater downloads it and generates a bounded, length-prefixed import code. Pasting that code does not execute Lua or write live SavedVariables files. Only report summaries, equipped input fingerprints, and positive upgrades are saved and shared; full reports and bag data are not uploaded to this project's repository or a server.

## Developer checks

The sanitized fixture uses synthetic character identity and talents. .NET tests check source/level matching, conversion IDs, duplicate slot variants, DPS calculations, invalid URLs/reports, and UTF-8 byte lengths. Lua tests decode the actual .NET fixture, validate guild permissions, exercise chunked sharing, and check stale build detection and UI layout.

To verify a live report without installing anything:

```powershell
dotnet run --project updater/EvilTwinsUpdater.Tests -c Release -- --verify-report https://www.raidbots.com/simbot/report/REPORT_ID
```
