local _, ET = ...
local Loot = {}
ET.Loot = Loot

-- The journal is a shared global query context. Restore its filters after each
-- query, and leave an open Adventure Guide alone while the user is browsing it.
function Loot:Ready()
    if InCombatLockdown and InCombatLockdown() then return false, "Browse loot after leaving combat." end
    if C_AddOns and C_AddOns.LoadAddOn then C_AddOns.LoadAddOn("Blizzard_EncounterJournal") end
    if not EJ_GetNumTiers or not C_EncounterJournal or not C_EncounterJournal.GetLootInfoByIndex then
        return false, "Adventure Guide data is not available yet. Open the guide once, then try again."
    end
    if EncounterJournal and EncounterJournal:IsShown() then
        return false, "Close the Adventure Guide to browse loot here."
    end
    return true
end

function Loot:Query(callback)
    local ready, err = self:Ready()
    if not ready then return nil, err end
    local tier, difficulty = EJ_GetCurrentTier(), EJ_GetDifficulty()
    local class, spec = EJ_GetLootFilter()
    local slot = C_EncounterJournal.GetSlotFilter and C_EncounterJournal.GetSlotFilter()
    local instance = EncounterJournal and EncounterJournal.instanceID
    local encounter = EncounterJournal and EncounterJournal.encounterID
    local ok, result = pcall(callback)
    EJ_SelectTier(tier)
    if instance then
        EJ_SelectInstance(instance)
        if encounter then EJ_SelectEncounter(encounter) end
    end
    EJ_SetDifficulty(difficulty)
    EJ_SetLootFilter(class, spec)
    if slot and C_EncounterJournal.SetSlotFilter then C_EncounterJournal.SetSlotFilter(slot) end
    if not ok then return nil, "Loot data is still loading. Try again shortly." end
    return result
end

local function sortNames(entries)
    table.sort(entries, function(a, b) return a.name:lower() < b.name:lower() end)
end

function Loot:Sources(kind)
    return self:Query(function()
        local entries = {}
        if kind == "RAID" then
            local tier = EJ_GetNumTiers()
            EJ_SelectTier(tier)
            for index = 1, 100 do
                local id, name = EJ_GetInstanceByIndex(index, true)
                if not id then break end
                entries[#entries + 1] = { id = id, name = name, tier = tier, raid = true }
            end
        else
            -- The active challenge map table includes returning seasonal dungeons.
            local active, byMap, byName = {}, {}, {}
            if C_ChallengeMode and C_ChallengeMode.GetMapTable and C_ChallengeMode.GetMapUIInfo then
                for _, id in ipairs(C_ChallengeMode.GetMapTable() or {}) do
                    local name, _, _, _, _, map = C_ChallengeMode.GetMapUIInfo(id)
                    if name then active[#active + 1] = { name = name, map = map } end
                end
            end
            for tier = 1, EJ_GetNumTiers() do
                EJ_SelectTier(tier)
                for index = 1, 100 do
                    local id, name, _, _, _, _, _, _, _, _, map = EJ_GetInstanceByIndex(index, false)
                    if not id then break end
                    local source = { id = id, name = name, tier = tier, raid = false }
                    if map then byMap[map] = source end
                    byName[name] = source
                end
            end
            local seen = {}
            for _, map in ipairs(active) do
                local source = byMap[map.map] or byName[map.name]
                if source and not seen[source.id] then
                    entries[#entries + 1] = source
                    seen[source.id] = true
                end
            end
        end
        sortNames(entries)
        return entries
    end)
end

function Loot:Bosses(source)
    return self:Query(function()
        EJ_SelectTier(source.tier)
        EJ_SelectInstance(source.id)
        local entries = { { id = 0, name = "All bosses" } }
        for index = 1, 100 do
            local name, _, id = EJ_GetEncounterInfoByIndex(index, source.id)
            if not name then break end
            entries[#entries + 1] = { id = id, name = name }
        end
        return entries
    end)
end

function Loot:Items(source, boss, difficulty, forSpec)
    return self:Query(function()
        EJ_SelectTier(source.tier)
        EJ_SelectInstance(source.id)
        if EJ_IsValidInstanceDifficulty and not EJ_IsValidInstanceDifficulty(difficulty) then
            if source.raid then return { error = "This raid does not have that difficulty. Choose another difficulty." } end
            difficulty = 23 -- Mythic base loot if the journal has no Keystone preview.
        end
        EJ_SetDifficulty(difficulty)
        if boss and boss > 0 then EJ_SelectEncounter(boss) end
        local class = forSpec and select(3, UnitClass("player")) or 0
        local spec = forSpec and GetSpecialization() and GetSpecializationInfo(GetSpecialization()) or 0
        EJ_SetLootFilter(class or 0, spec or 0)
        if C_EncounterJournal.ResetSlotFilter then C_EncounterJournal.ResetSlotFilter() end
        local entries, seen, pending = {}, {}, false
        for index = 1, EJ_GetNumLoot() do
            local info = C_EncounterJournal.GetLootInfoByIndex(index)
            if info and info.itemID and not seen[info.itemID] then
                seen[info.itemID] = true
                local name = info.name or GetItemInfo(info.itemID)
                if not name then
                    pending = true
                    if C_Item and C_Item.RequestLoadItemDataByID then C_Item.RequestLoadItemDataByID(info.itemID) end
                end
                entries[#entries + 1] = { id = info.itemID, name = name or ("Item #" .. info.itemID),
                    link = info.link, icon = info.icon, slot = info.slot,
                    boss = info.encounterID and EJ_GetEncounterInfo(info.encounterID) or "" }
            elseif not info then pending = true end
        end
        sortNames(entries)
        entries.pending = pending
        return entries
    end)
end

function Loot:HasWish(itemID)
    for _, wish in ipairs(ET.db.wishlists[ET.key] or {}) do if wish.id == itemID then return true end end
    return false
end

function Loot:Add(item, source)
    if not ET:CanUseLeaderTools() then return false, "Your guild rank does not have access to wishlists." end
    local note = source.name .. (item.boss and item.boss ~= "" and (" / " .. item.boss) or "")
    return ET:AddWish(tostring(item.id), note)
end
