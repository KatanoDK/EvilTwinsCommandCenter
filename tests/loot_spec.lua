-- A returning Mythic+ dungeon must resolve outside the latest expansion tier.
local state = { tier = 1, difficulty = 14, class = 8, spec = 64, slot = 5, instance = 100, boss = 0 }
EncounterJournal = { instanceID = 100, encounterID = 0, IsShown = function() return false end }
C_AddOns = { LoadAddOn = function() end }
function InCombatLockdown() return false end
function EJ_GetNumTiers() return 2 end
function EJ_GetCurrentTier() return state.tier end
function EJ_SelectTier(value) state.tier = value end
function EJ_GetDifficulty() return state.difficulty end
function EJ_SetDifficulty(value) state.difficulty = value end
function EJ_GetLootFilter() return state.class, state.spec end
function EJ_SetLootFilter(class, spec) state.class, state.spec = class, spec end
function EJ_GetInstanceByIndex(index, raid)
    if index ~= 1 then return end
    if raid and state.tier == 2 then return 200, "Current raid" end
    if not raid and state.tier == 1 then return 100, "Returning dungeon", nil, nil, nil, nil, nil, nil, nil, nil, 500 end
    if not raid and state.tier == 2 then return 101, "Inactive dungeon", nil, nil, nil, nil, nil, nil, nil, nil, 501 end
end
function EJ_SelectInstance(value) state.instance, state.boss = value, 0 end
function EJ_SelectEncounter(value) state.boss = value end
function EJ_GetEncounterInfoByIndex(index)
    if index == 1 then return "First boss", nil, 900 end
end
function EJ_GetEncounterInfo() return "First boss" end
function EJ_IsValidInstanceDifficulty(value) return value ~= 17 end
function EJ_GetNumLoot() return 3 end
function UnitClass() return "Mage", "MAGE", 8 end
function GetSpecialization() return 1 end
function GetSpecializationInfo() return 64 end
C_ChallengeMode = {
    GetMapTable = function() return { 42, 43 } end,
    GetMapUIInfo = function(id)
        return id == 42 and "Returning dungeon" or "Not journalled", id, 100, nil, nil, id == 42 and 500 or 999
    end,
}
C_EncounterJournal = {
    GetSlotFilter = function() return state.slot end,
    SetSlotFilter = function(value) state.slot = value end,
    ResetSlotFilter = function() state.slot = 15 end,
    GetLootInfoByIndex = function(index)
        assert(state.slot == 15, "Old journal slot filters must not hide wishlist items")
        if index <= 2 then return { itemID = 123, name = "Test gear", slot = "Head", encounterID = 900 } end
        return { itemID = 124, slot = "Trinket", encounterID = 900 }
    end,
}
local requests = {}
C_Item = { RequestLoadItemDataByID = function(id) requests[#requests + 1] = id end }
function GetItemInfo() return nil end
ET.db.wishlists = { [ET.key] = {} }
function ET:AddWish(id, note)
    id = tonumber(id)
    if #self.db.wishlists[self.key] >= 20 then return false, "Full" end
    for _, wish in ipairs(self.db.wishlists[self.key]) do if wish.id == id then return false, "Duplicate" end end
    self.db.wishlists[self.key][#self.db.wishlists[self.key] + 1] = { id = id, note = note }
    self.db.characters[self.key].wishlist = self.db.wishlists[self.key]
    return true
end
function ET:CanUseLeaderTools() return true end
function ET:Print(message) self.lastMessage = message end
function Ambiguate(key) return key:match("^[^-]+") end
function ET:GetGuildRankIndex(key) return key ~= "Stranger-Realm" and 8 or nil end
C_Timer = { NewTimer = function(_, callback) return { callback = callback, Cancel = function() end } end }

local function restored()
    assert(state.tier == 1 and state.difficulty == 14 and state.class == 8 and state.spec == 64
        and state.slot == 5 and state.instance == 100 and state.boss == 0, "Journal state must be restored")
end

function RunLootChecks()
    local L = ET.Loot
    local raids = L:Sources("RAID")
    assert(#raids == 1 and raids[1].id == 200)
    restored()
    local dungeons = L:Sources("DUNGEON")
    assert(#dungeons == 1 and dungeons[1].id == 100 and dungeons[1].tier == 1)
    restored()
    local bosses = L:Bosses(raids[1])
    assert(#bosses == 2 and bosses[2].id == 900)
    local items = L:Items(raids[1], 900, 15, true)
    assert(#items == 2 and items.pending and #requests == 1, "Deduplicate drops and load missing names")
    restored()
    local oldInfo = C_EncounterJournal.GetLootInfoByIndex
    C_EncounterJournal.GetLootInfoByIndex = function(index)
        assert(state.boss == 0, "All bosses must clear a previous encounter selection")
        return oldInfo(index)
    end
    assert(#L:Items(raids[1], 0, 15, false) == 2)
    C_EncounterJournal.GetLootInfoByIndex = oldInfo
    local invalid = L:Items(raids[1], 0, 17, false)
    assert(invalid.error)
    restored()
    local broken = L:Query(function() error("Unavailable data") end)
    assert(not broken)
    restored()
    EncounterJournal.IsShown = function() return true end
    local blocked, message = L:Sources("RAID")
    assert(not blocked and message:find("Close the Adventure Guide", 1, true))
    EncounterJournal.IsShown = function() return false end
    assert(L:Add(items[1], raids[1]) and L:HasWish(items[1].id))
    assert(ET.db.wishlists[ET.key][1].note:find("Current raid / First boss", 1, true))
    assert(not L:Add(items[1], raids[1]), "Duplicates must be rejected")
end

function RunDirectoryUIChecks()
    local UI = ET.UI
    ET.db.crafting = { [ET.key] = { professions = {
        [164] = { name = "Blacksmithing", updated = 100, recipes = {} },
        [171] = { name = "Alchemy", updated = 100, recipes = { { id = 9999, name = "Potion", learned = true } } },
    } } }
    for index = 1, 1000 do
        ET.db.crafting[ET.key].professions[164].recipes[index] = {
            id = index, name = string.format("Recipe %04d", index), learned = index < 1000 }
    end
    UI:ShowCrafting()
    assert(#UI.craftingDisplay == 2 and #UI.craftingRecipeRows == 2, "Professions start collapsed")
    assert(UI.craftingDisplay[2].group.known == 999, "Unknown recipes are hidden by default")
    UI.craftingRecipeRows[2].scripts.OnClick(UI.craftingRecipeRows[2])
    assert(#UI.craftingDisplay == 1001 and #UI.craftingRecipeRows <= 13, "Large profession lists must reuse visible rows")
    UI.craftingScroll:SetVerticalScroll(46 * 500)
    assert(#UI.craftingRecipeRows <= 13 and UI.craftingRecipeRows[1].data.id > 400)
    UI.craftingSearch:SetText("Recipe 1000")
    UI.craftingSearch.scripts.OnTextChanged()
    assert(#UI.craftingDisplay == 0, "Unlearned recipes require All scanned recipes")
    UI.craftingFilter.choices[2].pick("ALL")
    assert(#UI.craftingDisplay == 2 and UI.craftingDisplay[2].data.id == 1000)
    UI.craftingSearch:SetText("Potion")
    UI.craftingSearch.scripts.OnTextChanged()
    assert(#UI.craftingDisplay == 2 and UI.craftingDisplay[1].expanded, "Search opens matching professions")
    UI.craftingSearch:SetText("")
    UI.craftingSearch.scripts.OnTextChanged()
    assert(UI.craftingExpanded.Blacksmithing, "Search must preserve expansion choices")
    UI.selected = ET.key
    UI:ShowLootPicker()
    assert(UI.lootFrame.shown and #UI.lootSources == 1 and #UI.lootDisplay == 2)
    local addedRow
    for _, row in ipairs(UI.lootRows) do if row.item.id == ET.db.wishlists[ET.key][1].id then addedRow = row end end
    assert(addedRow and not addedRow.add.enabled and addedRow.add.text == "Added")
    UI.lootSearch:SetText("Head")
    UI.lootSearch.scripts.OnTextChanged()
    assert(#UI.lootDisplay == 1)
    UI.lootSearch:SetText("")
    UI.lootSearch.scripts.OnTextChanged()
    local nextRow
    for _, row in ipairs(UI.lootRows) do if row.item.id == 124 then nextRow = row end end
    nextRow.add.scripts.OnClick()
    assert(ET.Loot:HasWish(124) and not nextRow.add.enabled, "Add must use the recycled row's current item")
    UI.lootTypeDropdown.choices[2].pick("DUNGEON")
    assert(UI.lootSource.id == 100 and UI.lootDifficulty == 8)
    function ET:CanUseLeaderTools() return false end
    UI:RefreshLoot()
    assert(not UI.lootFrame.shown and not ET.Loot:Add({ id = 125 }, UI.lootSource))
end
