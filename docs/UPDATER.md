# Private addon updater proposal

The addon stays at version `0.1.0` while it is being tested. A separate build identifier (the Git commit SHA) lets an updater distinguish new builds even when the addon version does not change. `scripts/package_addon.py` includes both values in `build.json` inside the test ZIP.

## Sharing the first tests

Package the addon and send the ZIP to a guildmate in a private guild channel. The ZIP contains one `EvilTwinsCommandCenter` folder ready to extract into Retail's `Interface/AddOns`. This needs no distribution service or GitHub account on the tester's side.

## Proposed Windows updater

1. The guildmate selects their Retail installation once.
2. They sign in to the private download source.
3. A successful build after each push produces a ZIP and its commit identifier.
4. The updater checks for a different commit, downloads the ZIP, validates it, and stages the files.
5. With WoW closed, it backs up and replaces only `Interface/AddOns/EvilTwinsCommandCenter`. The separate `WTF` folder containing SavedVariables is preserved.
6. The app shows the installed build and update status, with a manual check and rollback option.

This requires a separate desktop app: the WoW addon itself does not download and install its own files. No updater or installation task is implemented or enabled yet.

## Private downloads

**GitHub access for each tester:** use the existing private repository and private build assets. Each tester authenticates with their own GitHub account and needs repository access. The app must not bundle the owner's token or a shared repository credential. GitHub documents [private artifact downloads](https://docs.github.com/en/rest/actions/artifacts) and [authenticated release asset downloads](https://docs.github.com/en/rest/releases/assets).

**Guild accounts without repository access:** use a small authenticated download service in front of private storage. Members sign in through the guild's chosen identity provider, and the service grants download access to approved guild members. This requires hosting and access management, but avoids giving every member repository access.

Start with the ZIP for a two-person in-game test. Choose the sign-in model before implementing the updater; it determines whether we can use GitHub directly or need the download service.
