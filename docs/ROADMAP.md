# Roadmap

The addon now shares character snapshots, wishlists, SimC exports, carried bags, and profession recipe scans. Character tools use Raid prep, Build & SimC, Bags, and Wishlist tabs. The officer's raid prep view uses those snapshots and an automatic current-expansion supply catalogue with a minimum of one per category. The crafting directory is available to every guild rank. Possible additions, in rough priority order:

1. **Loot map:** pick a raid boss or drop and see everyone who has marked it as wanted, with their notes.
2. **Alt linking:** let a member group their own characters under one display name.
3. **Crafting requests:** members post desired crafts or services, building on the shared recipe directory.
4. **Upgrade tracking:** import a user's own SimC or Raidbots upgrade results into the wishlist, with date and source.
5. **Companion dashboard:** an optional desktop helper reads the addon's SavedVariables after logout or `/reload` and uploads approved fields to a private guild service. This would make snapshots available outside WoW and while everyone is offline.

Before adding a companion, validate the in-game data model and sharing experience with several guild members. The desktop helper would be a separate component; WoW addons cannot call web services directly.
