# Evil Twins Windows updater

The addon stays at version `0.1.0` while it is being tested. Git commit IDs identify individual builds, so the updater detects new code without changing the addon version. The repository and downloadable builds are public. No GitHub login or embedded credential is used.

## Install the updater

1. Open [Releases](https://github.com/KatanoDK/EvilTwinsCommandCenter/releases) and select the newest development build.
2. Download `EvilTwinsUpdater-win-x64.zip`, extract it, and run `EvilTwinsUpdater.exe`. It includes its own .NET runtime and targets Windows x64.
3. Click **Choose folder** and select WoW's `_retail_` folder containing `Wow.exe`.
4. Click **Check / update now**. The button checks immediately and installs a new build if one is available, including while WoW is open. Type `/reload` in WoW afterward to load the updated addon.
5. Leave automatic checks enabled for updates every 15 minutes while the app is running. Minimize it to keep it in the system tray. Closing the app stops checks; it does not register a Windows startup task.

The updater always uses this project's public repository; there is no repository field to configure. Existing folder and automatic-check settings are preserved when replacing the updater.

The executable is not code-signed. Windows may ask you to confirm its publisher when launching it. The app itself does not update automatically; exit the old updater from its tray menu and download a newer updater from Releases when needed.

## Installation and recovery

- Only `Interface/AddOns/EvilTwinsCommandCenter` is replaced. Other addons and the `WTF` folder containing saved data are preserved.
- Updates, local ZIP installs, and restores work while WoW is running. The running game continues using its loaded code until you type `/reload`. The updater shows a reload reminder and a tray notification after installing while WoW is open; it does not trigger a reload itself.
- The downloaded archive must match GitHub's SHA-256 asset digest and its published commit. Unexpected paths, oversized files, inconsistent versions, and incomplete packages are rejected before replacement.
- One previous installation is kept in `Interface/AddOns/.EvilTwinsCommandCenter-backup`. **Restore previous build** swaps it back and disables automatic updates so the restored build stays installed.
- **Install ZIP** accepts a clean build produced by `scripts/package_addon.py`, including a ZIP shared directly by a guildmate. It works without a release feed. Local ZIPs use archive validation but have no independently published checksum to verify against.
- Settings are stored in `%LOCALAPPDATA%/EvilTwinsUpdater/settings.json`. Linked install paths are not supported; choose the physical Retail folder.

## Build and test

With the .NET 10 SDK:

```powershell
dotnet run --project updater/EvilTwinsUpdater.Tests/EvilTwinsUpdater.Tests.csproj -c Release -- .test-work/install-tests
dotnet publish updater/EvilTwinsUpdater/EvilTwinsUpdater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -o dist/updater
```

The tests use generated fake Retail folders under the selected test workspace. They cover updates, rollback, locked-file failures and retry, preservation of other addons and saved data, archive path validation, commit checks, public release selection, and download checksums. They never install into a real WoW folder. Actual in-game reload behavior still needs a guild test.

`.github/workflows/build-release.yml` runs the Lua and updater tests on each push to `master`, packages the addon, builds the Windows updater, and publishes a development release tagged `build-<full commit SHA>`. Releases are marked prerelease because the addon still needs in-game testing. The app reads these build releases directly rather than GitHub's latest stable release endpoint. All downloads are publicly accessible; see [GitHub's asset API](https://docs.github.com/en/rest/releases/assets).
