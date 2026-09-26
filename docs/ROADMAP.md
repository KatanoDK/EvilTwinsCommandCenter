# Roadmap

The addon starts with a small shared data model: character snapshot, wishlist, and optional SimC export. Possible additions, in rough priority order:

1. **Loot map:** pick a raid boss or drop and see everyone who has marked it as wanted, with their notes.
2. **Alt linking:** let a member group their own characters under one display name.
3. **Raid prep:** flag missing enchants, gems, and consumables from a member's published snapshot.
4. **Crafting directory:** members publish recipes or services they offer and current crafting requests.
5. **Upgrade tracking:** import a user's own SimC or Raidbots upgrade results into the wishlist, with date and source.
6. **Companion dashboard:** an optional desktop helper reads the addon's SavedVariables after logout or `/reload` and uploads approved fields to a private guild service. This would make snapshots available outside WoW and while everyone is offline.

Before adding a companion, validate the in-game data model and sharing experience with several guild members. The desktop helper would be a separate component; WoW addons cannot call web services directly.

