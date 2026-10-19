# Evil Twins Windows updater

Addon and updater branding stay at **0.1.0** during testing. Git commits identify addon builds; installed app packages use increasing `0.1.0-dev.N` identifiers so self-updates can recognize newer code. Downloads are public and require no GitHub login.

## Install once

1. Exit your old portable updater using its system tray menu.
2. Download and run [EvilTwinsUpdater-Setup.exe](https://github.com/KatanoDK/EvilTwinsCommandCenter/releases/download/updater/EvilTwinsUpdater-Setup.exe).
3. Setup installs for your Windows user in `%LOCALAPPDATA%/EvilTwinsUpdaterApp`, creates Desktop and Start menu shortcuts, and adds a Windows uninstall entry. It includes its own .NET runtime and targets Windows x64.
4. Your existing folder and automatic-check settings are retained. If needed, click **Choose folder** and select WoW's `_retail_` folder containing `Wow.exe`.
5. Click **Check / update now** to check immediately. Leave automatic checks enabled for checks every 15 minutes while the app is open or minimized to its tray icon. Closing the app stops checks; no Windows startup task is registered.

The installer is unsigned, so Windows can still display **Windows protected your PC**. Installation and self-updating do not solve SmartScreen reputation by themselves; trusted code signing is a separate future step.

## App updates

**Check / update now** checks both the addon and installed application. App checks also work before choosing a Retail folder, and an addon download failure does not prevent the app check. When newer app code is available, click **Update app & restart**. It downloads and verifies the package, saves settings, replaces the application, and restarts it. Future app updates do not require downloading another ZIP or installer. The initial portable copy needs the one-time Setup installation before this works.

App packages use [Velopack](https://docs.velopack.io/getting-started/wpf), pinned to version 1.2.158. Application updates use the fixed public `updater` release's `releases.win.json` feed. Complete packages are uploaded before publishing their feed. Addon-only changes retain the existing installer and app build; the publisher compares a normalized hash of application source and packaging inputs. A failed check leaves the current app available for a manual retry. Packages are checked before staging; newer installed app versions are not downgraded.

## Addon updates and recovery

- Addon updates, **Install ZIP**, and **Restore previous build** work while WoW is open. The game continues using loaded code until `/reload`. The updater reminds you to reload; it does not trigger the reload itself.
- Only `Interface/AddOns/EvilTwinsCommandCenter` is replaced. Other addons and the `WTF` folder containing saved data are preserved.
- Downloaded addon archives must match GitHub's SHA-256 asset digest and published commit. Unexpected paths, oversized files, inconsistent versions, and incomplete packages are rejected before replacement.
- One previous addon installation is kept in `Interface/AddOns/.EvilTwinsCommandCenter-backup`. **Restore previous build** swaps it back and disables automatic checks so it stays installed.
- **Install ZIP** accepts clean addon builds produced by `scripts/package_addon.py`. Local ZIPs have archive validation but no independently published checksum.
- Settings remain in `%LOCALAPPDATA%/EvilTwinsUpdater/settings.json`. Choose the physical Retail folder; linked install paths are unsupported.
- Uninstalling the Windows app does not remove the WoW addon or its saved guild data.

The app's **Droptimizer** tab imports completed reports into the addon; see [Droptimizer instructions](DROPTIMIZER.md).

In **Raid upgrades**, **Install / update SimulationCraft** downloads the latest official Windows x64 nightly, extracts it and automatically selects its local engine. It checks only when clicked, skips downloading an intact current build, supports cancellation and keeps the selected engine on failure. **Choose engine** still supports a manually installed copy. See [engine update details](RAID_DROPTIMIZER.md#optional-local-queue).

The desktop app uses a dark sidebar, rounded cards and a table of imported upgrades. **Raid upgrades** replaces the standalone simulator screen: import the raid's DPS exports, queue individual local Droptimizers, or import completed Raidbots reports for those players. Results can be filtered by player or searched by drop/boss. Local queues display progress and an approximate time remaining, preserve completed results when stopped, and resume remaining players with the same settings. See [raid Droptimizer setup](RAID_DROPTIMIZER.md).

## Build and test

With the .NET 10 SDK:

```powershell
dotnet run --project updater/EvilTwinsUpdater.Tests/EvilTwinsUpdater.Tests.csproj -c Release -- .test-work/install-tests
dotnet publish updater/EvilTwinsUpdater/EvilTwinsUpdater.csproj -c Release -r win-x64 --self-contained true -p:DebugType=embedded -o dist/updater
dotnet tool install vpk --version 1.2.158 --tool-path .test-work/tools
.test-work/tools/vpk.exe pack --packId EvilTwinsUpdaterApp --packVersion 0.1.0-dev.1 --packDir dist/updater --runtime win-x64 --mainExe EvilTwinsUpdater.exe --packTitle "Evil Twins Updater" --noPortable --delta None --outputDir dist/app
```

Velopack produces `EvilTwinsUpdaterApp-win-Setup.exe`; the release publisher names the download `EvilTwinsUpdater-Setup.exe`.

Tests use fake Retail folders and Velopack test installation directories. They cover addon installs and rollback, locked-file failures, preservation of other addons and saved data, archive and commit validation, public feeds, checksums, app version ordering, staged downloads, and corrupt app packages. They do not install on the real PC, exercise an actual installed app restart, or replace in-game testing.

`.github/workflows/build-release.yml` runs Lua and .NET checks on every push to `master`, publishes changed app source with `scripts/publish_updater.ps1`, and publishes the addon separately as `build-<commit SHA>`. Both remain development releases. The app reads addon build releases rather than GitHub's latest stable release endpoint.

To verify the published app feed and stage its real package in an isolated test directory without installing or restarting anything:

```powershell
dotnet run --project updater/EvilTwinsUpdater.Tests -c Release -- --verify-app-feed .test-work/published-app-tests
```
