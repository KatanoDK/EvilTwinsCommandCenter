"""Run with Python and lupa installed: python tests/test_crafting.py."""
from pathlib import Path
from lupa.lua51 import LuaRuntime

root = Path(__file__).resolve().parents[1]
lua = LuaRuntime(unpack_returned_tuples=True)
for path in (root / "EvilTwinsCommandCenter").glob("*.lua"):
    lua.execute("assert(loadstring(...))", path.read_text(encoding="utf-8"))

lua.execute("""
now = 100000
function time() return now end
function IsInGuild() return true end
function UnitGUID() return "Player-self" end
function UnitName() return "Self" end
Enum = {}
ET = { key = "Self-Realm", db = { crafting = {}, characters = {} }, leaderRankCutoff = 5 }
function ET:GetGuildRankIndex(name) return name ~= "Stranger-Realm" and 8 or nil end
function ET:CanUseLeaderTools() return false end
function ET:CanSenderUseLeaderTools() return false end
""")
for name in ("Protocol", "Crafting", "Comm", "SimC"):
    source = (root / "EvilTwinsCommandCenter" / f"{name}.lua").read_text(encoding="utf-8")
    lua.execute("assert(loadstring(...))('TestAddon', ET)", source)
lua.execute((root / "tests" / "crafting_spec.lua").read_text(encoding="utf-8"))
lua.execute((root / "tests" / "simc_spec.lua").read_text(encoding="utf-8"))
ui_lua = LuaRuntime(unpack_returned_tuples=True)
ui_lua.execute((root / "tests" / "roster_spec.lua").read_text(encoding="utf-8"))
for name in ("Consumables", "RaidPrep", "Roster", "UI"):
    source = (root / "EvilTwinsCommandCenter" / f"{name}.lua").read_text(encoding="utf-8")
    ui_lua.execute("assert(loadstring(...))('TestAddon', ET)", source)
    if name == "Roster":
        ui_lua.execute("RunRosterChecks()")
ui_lua.execute("RunUIChecks()")
prep_lua = LuaRuntime(unpack_returned_tuples=True)
prep_lua.execute((root / "tests" / "raid_prep_spec.lua").read_text(encoding="utf-8"))
for name in ("Consumables", "RaidPrep"):
    source = (root / "EvilTwinsCommandCenter" / f"{name}.lua").read_text(encoding="utf-8")
    prep_lua.execute("assert(loadstring(...))('TestAddon', ET)", source)
prep_lua.execute("RunPrepChecks()")
print("All Lua files compile; crafting, ranks, SimC, roster, tabs, and automatic raid-prep checks passed.")
