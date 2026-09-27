$ErrorActionPreference = 'Stop'
# This script runs in GitHub Actions. It publishes the app only when its inputs change.
$repository = $env:GITHUB_REPOSITORY
if (-not $repository -or -not $env:GITHUB_RUN_NUMBER) { throw 'Run this publisher in GitHub Actions.' }
$sourceHash = (python scripts/app_source_hash.py).Trim()
if ($LASTEXITCODE -ne 0) { throw 'App source hash failed.' }
$previous = $null
try {
    $previous = Invoke-RestMethod "https://github.com/$repository/releases/download/updater/app-build.json"
} catch {
    # Missing release is expected on the initial publication; other errors must not hide failures.
    if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
}
if ($previous.sourceHash -eq $sourceHash) {
    Write-Output 'Updater source unchanged; keeping the existing installer and app feed.'
    exit 0
}
$appVersion = "0.1.0-dev.$env:GITHUB_RUN_NUMBER"
dotnet publish updater/EvilTwinsUpdater/EvilTwinsUpdater.csproj -c Release -r win-x64 --self-contained true -p:DebugType=embedded -o dist/updater
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
dotnet tool install vpk --version 1.2.158 --tool-path .test-work/tools
if ($LASTEXITCODE -ne 0) { throw 'Packaging tool installation failed.' }
& .test-work/tools/vpk.exe pack --packId EvilTwinsUpdater --packVersion $appVersion --packDir dist/updater --mainExe EvilTwinsUpdater.exe --packTitle 'Evil Twins Updater' --packAuthors 'Evil Twins' --runtime win-x64 --noPortable --delta None --shortcuts Desktop,StartMenuRoot --outputDir dist/app
if ($LASTEXITCODE -ne 0) { throw 'Installer packaging failed.' }
Copy-Item -LiteralPath dist/app/EvilTwinsUpdater-win-Setup.exe -Destination dist/app/EvilTwinsUpdater-Setup.exe
@{ sourceHash = $sourceHash; version = $appVersion; commit = $env:GITHUB_SHA } | ConvertTo-Json | Set-Content dist/app/app-build.json -Encoding utf8
@'
Evil Twins Updater 0.1.0 development builds.

Download EvilTwinsUpdater-Setup.exe once. Exit the old portable updater from its tray menu before installing. Setup creates Desktop and Start menu shortcuts and a Windows uninstall entry. Your existing addon folder and settings are preserved.

Use Check / update now for addon and app checks. When an app update is available, choose Update app & restart. Keep the app open or minimized for automatic checks every 15 minutes. Addon updates can install while WoW is open; type /reload afterward.

The installer is currently unsigned and Windows may display a SmartScreen warning. No GitHub login is required.
'@ | Set-Content dist/app/release-notes.md -Encoding utf8
gh release view updater --repo $repository *> $null
if ($LASTEXITCODE -ne 0) {
    gh release create updater --repo $repository --target $env:GITHUB_SHA --title 'Evil Twins Updater — Install & update' --prerelease --notes-file dist/app/release-notes.md
    if ($LASTEXITCODE -ne 0) { throw 'App release creation failed.' }
}
# Versioned packages stay available. Publish the new feed last, once every referenced asset exists.
$packages = @(Get-ChildItem -LiteralPath dist/app -Filter '*-full.nupkg' | ForEach-Object FullName)
if ($packages.Count -ne 1) { throw 'Expected one complete app package.' }
gh release upload updater @packages dist/app/EvilTwinsUpdater-Setup.exe --repo $repository --clobber
if ($LASTEXITCODE -ne 0) { throw 'App package upload failed.' }
gh release upload updater dist/app/releases.win.json --repo $repository --clobber
if ($LASTEXITCODE -ne 0) { throw 'App feed upload failed.' }
gh release upload updater dist/app/app-build.json --repo $repository --clobber
if ($LASTEXITCODE -ne 0) { throw 'App source manifest upload failed.' }
gh release edit updater --repo $repository --notes-file dist/app/release-notes.md
if ($LASTEXITCODE -ne 0) { throw 'App release notes failed.' }
