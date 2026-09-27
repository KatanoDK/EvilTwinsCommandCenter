now = 100000
function time() return now end
local leader, combat, raiding = true, false, true
function InCombatLockdown() return combat end
function IsInRaid() return raiding end
function GetNumGroupMembers() return 4 end
function GetNormalizedRealmName() return "Realm" end
function GetCurrentRegion() return 1 end
local names = { "Self", "Mage", "Tank", "Missing" }
function UnitFullName(unit) return names[tonumber(unit:match("%d+"))], "Realm" end
function UnitGUID(unit) return unit == "player" and "Player-1" or "Player-" .. unit:match("%d+") end
function UnitGroupRolesAssigned(unit) return unit == "raid3" and "TANK" or "DAMAGER" end
ET = { key = "Self-Realm", db = { simcProfiles = {}, characters = {} }, UI = {} }
function ET:CanUseLeaderTools() return leader end
function ET:GetGuildRankIndex() return 5 end
function ET:GetCachedSimC(key, guid) local c = self.db.simcProfiles[key]; return c and c.guid == guid and c or nil end
function ET:GetSimCProfile() return 'deathknight="Self"\nspec=unholy\n' end
function ET:StoreSimC(key, guid, updated, profile) self.db.simcProfiles[key] = { guid=guid, updated=updated, profile=profile }; return true end
local requests = {}
ET.Comm = { RequestSimC = function(_, key, guid) requests[#requests + 1] = { key, guid }; return true end }

function RunRaidChecks()
    ET:StoreSimC("mAGE-realm", "Player-2", 99000, 'mage="Mage"\nspec=fire\n# collector diagnostics\n# Gear from Bags\n# head=,id=123\n')
    ET:StoreSimC("Missing-Realm", "Old-Guid", 99999, 'mage="Missing"\nspec=arcane\n')
    assert(ET.RaidSimC:Refresh())
    assert(#requests == 2, "DPS guildmates reply automatically; tank must not be requested")
    local members = ET.RaidSimC:Members()
    assert(#members == 4 and members[1].state == "Ready" and members[2].spec == 63)
    assert(members[2].updated == 99000, "Cached capture time must be retained")
    assert(members[3].profile == "" and members[3].state == "Skipped: tank/healer")
    assert(members[4].state == "Missing profile", "GUID mismatches cannot reuse another character's profile")
    local code = ET.RaidSimC:Export()
    RaidFixtureCode = code
    local fields = ET.Protocol:Unpack(ET.Protocol:FromBase64(code:sub(6)), 284)
    assert(fields[1] == "1" and fields[3] == "us" and fields[4] == "4" and #fields == 32)
    assert(not fields[17]:find("#", 1, true), "Raid exports omit comments and bag alternatives to keep clipboard size small")
    assert(not ET.RaidSimC:Refresh(), "Collection requests must be rate limited")
    ET:StoreSimC("mAGE-realm", "Player-2", 100000, 'mage="Mage"\nspec=holy\n')
    assert(ET.RaidSimC:Members()[2].state == "Missing: unsupported specialization", "Use exported specialization rather than a stale snapshot")
    leader = false
    assert(not ET.RaidSimC:Export() and not ET.RaidSimC:Refresh(), "Lower ranks cannot export or refresh")
    leader = true; combat = true
    assert(not ET.RaidSimC:Export() and not ET.RaidSimC:Refresh(), "Collect only outside combat")
    combat = false; raiding = false
    assert(not ET.RaidSimC:Export(), "A guild roster is not a raid roster")
    print("PASS: current raid collection, automatic requests, DPS/tank filtering, cache timestamps, GUID checks, permissions, combat gates and raid clipboard codec.")
end
