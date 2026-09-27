local S = ET.SimC
ET.version = "0.1.0"
local race = "Scourge"
local equipped = {}
local pendingItem = false
local combat = false
function InCombatLockdown() return combat end
function UnitClass() return "Death Knight", "DEATHKNIGHT" end
function UnitRace() return "Localized race", race end
function UnitLevel() return 90 end
function GetRealmName() return "Test Realm" end
function GetCurrentRegion() return 1 end
function date() return "2026-09-26 12:00" end
function GetInventoryItemLink(_, slot) return equipped[slot] end
function GetInventoryItemID(_, slot) return slot == 1 and pendingItem and 12345 or nil end
C_SpecializationInfo = {
    GetSpecialization = function() return 1 end,
    GetSpecializationInfo = function() return 251, "Localized frost", nil, nil, "DAMAGER", 1 end,
}
C_ClassTalents = { GetActiveConfigID = function() return 50 end }
C_Traits = {
    GenerateImportString = function() return "TESTTALENTSTRING" end,
    GetConfigIDBySystemID = function(id) assert(id == 48); return 60 end,
    GetConfigInfo = function() return { treeIDs = { 70 } } end,
    GetTreeNodes = function() return { 80, 81 } end,
    GetNodeInfo = function(_, node)
        return node == 80 and { ranksPurchased = 1, activeEntry = { entryID = 900, rank = 2 } }
            or { ranksPurchased = 0 }
    end,
}
C_TradeSkillUI = { GetItemCraftedQualityByItemInfo = function() return 5 end }
function GetProfessions() return 1, 2 end
function GetProfessionInfo(id) return "Localized profession", nil, 100, nil, nil, nil, id == 1 and 164 or 171 end

local function item(values)
    for index = 1, #values do values[index] = tostring(values[index]) end
    return "|cffa335ee|Hitem:" .. table.concat(values, ":") .. "|h[Test Item]|h|r"
end
local crafted = item({
    12345, 7000, 111, 0, 333, 0, 0, 0, 90, 0, 0, 13,
    2, 9001, 9002,
    5, 9, 90, 28, 555, 29, 32, 30, 40, 64, 24680,
    0, 2, 8881, 8882,
})
local expected = "head=,id=12345,enchant_id=7000,gem_id=111/0/333,bonus_id=9001/9002,drop_level=90,content_tuning=555,redirected_base_stats=24680,crafted_stats=32/40,gem_bonus_id=8881/8882,crafting_quality=5"
assert(S:ItemLine("head", crafted) == expected, "Crafted item options must preserve IDs, gems, and modifiers.")
equipped[1] = crafted
local profile, err = ET:GetSimCProfile()
assert(profile and not err, "No external addon should be needed.")
assert(profile:find('deathknight="Self"', 1, true))
assert(profile:find("race=undead", 1, true) and profile:find("spec=frost", 1, true))
assert(profile:find("talents=TESTTALENTSTRING", 1, true))
assert(profile:find("omnium_talents=900:2", 1, true))
assert(profile:find("professions=blacksmithing=100/alchemy=100", 1, true))
assert(profile:find(expected, 1, true))
assert(not profile:find("Gear from Bags", 1, true) and not profile:find("Bank", 1, true))
local _, _, roundtrip = ET.Protocol:DecodeSimC(ET.Protocol:EncodeSimC("Player-self", now, profile))
assert(roundtrip == profile, "Native exports must work with cached guild transfers.")

-- Actual SimulationCraft registration: the object is local, available via AceAddon.
assert(_G.Simulationcraft == nil)
local collector = { identity = "registered instance" }
local calls = 0
local function collect(self, debug, noBags, merchant, links)
    assert(self == collector and debug == false and noBags == true and merchant == false and links == nil)
    calls = calls + 1
    return "# Official collector export\ndeathknight=\"Self\"\n"
end
collector.GetSimcProfile = collect
LibStub = { GetLibrary = function(_, name, silent)
    assert(name == "AceAddon-3.0" and silent)
    return { GetAddon = function(_, addonName, quiet)
        assert(addonName == "Simulationcraft" and quiet)
        return collector
    end }
end }
SimulationcraftAPI = { GetSimcProfile = collect }
assert(ET:GetSimCProfile():find("Official collector", 1, true))
assert(calls == 1, "Current upstream API must receive its registered instance.")
SimulationcraftAPI = nil
assert(ET:GetSimCProfile():find("Official collector", 1, true))
assert(calls == 2, "Older collectors without the global API must still work.")
collector.GetSimcProfile = function() return "# Invalid collector result", "Talent serialization mismatch" end
assert(ET:GetSimCProfile():find("TESTTALENTSTRING", 1, true), "Collector errors should fall back to native export.")
LibStub = nil

C_SpecializationInfo.GetSpecializationInfo = function() return 250, "Localized blood", nil, nil, "TANK", 1 end
assert(ET:GetSimCProfile():find("role=tank", 1, true), "Role is the fifth API return value.")
C_SpecializationInfo.GetSpecializationInfo = function() return 251, "Localized frost", nil, nil, "DAMAGER", 1 end
C_Traits.GetLoadoutSerializationVersion = function() return 3 end
assert(not ET:GetSimCProfile(), "Unsupported talent serialization must not be treated as compatible.")
C_Traits.GetLoadoutSerializationVersion = nil

race = "ZandalariTroll"
C_UnitAuras = { GetBuffDataByIndex = function(_, index) return index == 1 and { spellId = 292361 } or nil end }
assert(ET:GetSimCProfile():find("zandalari_loa=paku", 1, true))
combat = true
assert(not ET:GetSimCProfile(), "Combat exports must be deferred.")
combat = false
equipped[1] = nil
pendingItem = true
assert(not ET:GetSimCProfile(), "An uncached equipped item must not be silently omitted.")
pendingItem = false
C_Traits.GenerateImportString = function() return "" end
assert(not ET:GetSimCProfile(), "Unloaded talent data must not produce a misleading export.")
assert(not S:ItemLine("head", "item:123:0:0:0:0:0:0:0:0:0:0:0:2:100"), "Incomplete bonus lists must be rejected.")
assert(not S:ItemLine("waist", "item:242664"), "Legacy special effects must not be guessed.")

-- Bag alternatives use Top Gear comments, never replace equipped slots or visit a bank.
local bagItems = { [0] = { { id = 12345, link = crafted }, { id = 12345, link = crafted }, { id = 999, link = "item:999" } },
    [4] = { { id = 54321, link = "item:54321" } } }
function GetItemInfoInstant(id) return id, nil, nil, id == 999 and "" or id == 54321 and "INVTYPE_TRINKET" or "INVTYPE_HEAD" end
C_Container = {
    GetContainerNumSlots = function(bag) assert(bag >= 0 and bag <= 4, "Bank slots must never be scanned"); return #(bagItems[bag] or {}) end,
    GetContainerItemID = function(bag, index) return bagItems[bag][index].id end,
    GetContainerItemLink = function(bag, index) return bagItems[bag][index].link end,
}
local bagProfile = assert(S:AppendCarriedGear("head=,id=1\n"))
assert(bagProfile:find("### Gear from Bags", 1, true) and bagProfile:find("# " .. expected, 1, true))
assert(bagProfile:find("# trinket1=,id=54321", 1, true))
assert(not bagProfile:find("id=999", 1, true), "Non-equipment must not enter a simulation")
local _, occurrences = bagProfile:gsub("id=12345", "")
assert(occurrences == 1, "Identical variants should be deduplicated")
bagItems[0][1].link = nil
assert(not S:AppendCarriedGear("head=,id=1\n"), "Missing bag gear must not silently produce an incomplete export")
bagItems[0][1].link = crafted
C_Traits.GenerateImportString = function() return "TESTTALENTSTRING" end
local fresh = assert(ET:GetSimCProfile())
assert(fresh:find("### Gear from Bags", 1, true), "Native player exports must include carried alternatives")
