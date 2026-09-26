local _, ET = ...
local Prep = {}
ET.RaidPrep = Prep

Prep.categories = {
    { key = "flask", label = "Flask" },
    { key = "food", label = "Food" },
    { key = "combatPotion", label = "Combat potion" },
    { key = "healthPotion", label = "Health potion" },
    { key = "augmentRune", label = "Augment rune" },
    { key = "weaponTemp", label = "Weapon enhancement" },
}

local enchantSlots = { [1] = true, [3] = true, [5] = true, [7] = true, [8] = true,
    [11] = true, [12] = true, [16] = true }
local requiredSlots = { 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }
local weaponTypes = { INVTYPE_WEAPON = true, INVTYPE_2HWEAPON = true,
    INVTYPE_WEAPONMAINHAND = true, INVTYPE_WEAPONOFFHAND = true,
    INVTYPE_RANGED = true, INVTYPE_RANGEDRIGHT = true }

function Prep:InitDB()
    if type(ET.db.prepChecklist) ~= "table" then ET.db.prepChecklist = {} end
    for _, category in ipairs(self.categories) do
        local entry = ET.db.prepChecklist[category.key]
        if type(entry) ~= "table" then
            entry = { minimum = 1, ids = {} }
            ET.db.prepChecklist[category.key] = entry
        end
        if type(entry.ids) ~= "table" then entry.ids = {} end
        if type(entry.minimum) ~= "number" then entry.minimum = 1 end
    end
end

function Prep:ParseRequirement(key, idsText, minimumText)
    local category
    for _, candidate in ipairs(self.categories) do
        if candidate.key == key then category = candidate; break end
    end
    if not category then return false, "Unknown supply category." end
    local minimum = tonumber(minimumText)
    if not minimum or minimum % 1 ~= 0 or minimum < 1 or minimum > 999 then
        return false, "Minimum must be a whole number from 1 to 999."
    end
    local ids, seen = {}, {}
    for token in (idsText or ""):gmatch("[^,%s]+") do
        local id = tonumber(token)
        if not id or id % 1 ~= 0 or id < 1 or id > 99999999 then
            return false, "Enter item IDs separated by commas."
        end
        if not seen[id] then ids[#ids + 1], seen[id] = id, true end
    end
    if #ids > 20 then return false, "Use at most 20 acceptable items per category." end
    return { minimum = minimum, ids = ids }
end

function Prep:SaveChecklist(values)
    local checklist = {}
    for _, category in ipairs(self.categories) do
        local row = values[category.key]
        local entry, err = self:ParseRequirement(category.key, row.ids, row.minimum)
        if not entry then return false, category.label .. ": " .. err end
        checklist[category.key] = entry
    end
    ET.db.prepChecklist = checklist
    if ET.UI then ET.UI:Refresh() end
    return true
end

local function slotName(index)
    return ET.slots[index] and ET.slots[index][2] or ("Slot " .. index)
end

local function hasEnchant(link)
    local field = link:match("item:%d+:([^:]*)")
    if field == nil then return nil end
    return (tonumber(field) or 0) > 0
end

local function shouldEnchant(index, link)
    if enchantSlots[index] then return true end
    if index ~= 17 then return false end
    if not C_Item or not C_Item.GetItemInfoInstant then return false end
    local ok, _, _, _, equipType = pcall(C_Item.GetItemInfoInstant, link)
    return ok and weaponTypes[equipType] or false
end

local function getEmptySockets(link)
    if not C_Item or not C_Item.GetItemNumSockets or not C_Item.GetItemGemID then return nil end
    local ok, count = pcall(C_Item.GetItemNumSockets, link)
    if not ok or type(count) ~= "number" then return nil end
    local empty = 0
    for index = 1, math.min(count, 8) do
        local gemOK, gemID = pcall(C_Item.GetItemGemID, link, index)
        if not gemOK then return nil end
        if not gemID or gemID == 0 then empty = empty + 1 end
    end
    return empty
end

local function bagCounts(inventory)
    if not inventory or type(inventory.containers) ~= "table" then return nil end
    local totals, oldest = {}, math.huge
    for bagID = 0, ET.Inventory:BagCount() - 1 do
        local bag = inventory.containers[bagID]
        if not bag or type(bag.items) ~= "table" then return nil end
        oldest = math.min(oldest, bag.updated or 0)
        for _, item in ipairs(bag.items) do
            totals[item.id] = (totals[item.id] or 0) + item.count
        end
    end
    return totals, oldest
end

function Prep:Analyze(snapshot, inventory)
    local result = { gearIssues = {}, supplyIssues = {}, supplyLines = {}, notes = {},
        missingGear = 0, missingEnchants = 0, emptySockets = 0, repairCount = 0,
        unknownEnchants = 0, unknownSockets = 0, unknownDurability = 0,
        configuredSupplies = 0, supplyUnknown = false, bagStale = false }
    for _, index in ipairs(requiredSlots) do
        if not snapshot.gear[index] or snapshot.gear[index] == "" then
            result.missingGear = result.missingGear + 1
            result.gearIssues[#result.gearIssues + 1] = "Missing equipped item: " .. slotName(index)
        end
    end
    local unknownSockets, unknownEnchants, unknownDurability = 0, 0, 0
    for index, link in ipairs(snapshot.gear) do
        if link ~= "" then
            if shouldEnchant(index, link) then
                local enchanted = hasEnchant(link)
                if enchanted == false then
                    result.missingEnchants = result.missingEnchants + 1
                    result.gearIssues[#result.gearIssues + 1] = "Missing enchant: " .. slotName(index)
                elseif enchanted == nil then unknownEnchants = unknownEnchants + 1 end
            end
            local empty = getEmptySockets(link)
            if empty and empty > 0 then
                result.emptySockets = result.emptySockets + empty
                result.gearIssues[#result.gearIssues + 1] = "Empty socket: " .. slotName(index) .. " (" .. empty .. ")"
            elseif empty == nil then unknownSockets = unknownSockets + 1 end
            local durability = snapshot.durability and snapshot.durability[index]
            if type(durability) == "number" and durability >= 0 and durability < 80 then
                result.repairCount = result.repairCount + 1
                result.gearIssues[#result.gearIssues + 1] = "Repair " .. slotName(index) .. " (" .. durability .. "%)"
            elseif durability == nil then unknownDurability = unknownDurability + 1 end
        end
    end
    result.unknownEnchants = unknownEnchants
    result.unknownSockets = unknownSockets
    result.unknownDurability = unknownDurability
    if unknownEnchants > 0 then result.notes[#result.notes + 1] = "Enchant status unavailable for " .. unknownEnchants .. " item(s)." end
    if unknownSockets > 0 then result.notes[#result.notes + 1] = "Socket status unavailable for " .. unknownSockets .. " item(s)." end
    if unknownDurability > 0 then result.notes[#result.notes + 1] = "Durability unavailable for " .. unknownDurability .. " item(s)." end

    local totals, bagUpdated = bagCounts(inventory)
    if totals then
        result.bagUpdated = bagUpdated
        if time() - bagUpdated > 86400 then
            result.bagStale = true
            result.notes[#result.notes + 1] = "Bag snapshot is over 24 hours old."
        end
    end
    for _, category in ipairs(self.categories) do
        local policy = ET.db.prepChecklist[category.key]
        if #policy.ids == 0 then
            result.supplyLines[#result.supplyLines + 1] = category.label .. ": not configured"
        elseif not totals then
            result.configuredSupplies = result.configuredSupplies + 1
            result.supplyUnknown = true
            result.supplyLines[#result.supplyLines + 1] = category.label .. ": bag data unavailable"
        else
            result.configuredSupplies = result.configuredSupplies + 1
            local count = 0
            for _, id in ipairs(policy.ids) do count = count + (totals[id] or 0) end
            result.supplyLines[#result.supplyLines + 1] = category.label .. ": " .. count .. "/" .. policy.minimum
            if count < policy.minimum then
                result.supplyIssues[#result.supplyIssues + 1] = category.label .. " short by " .. (policy.minimum - count)
            end
        end
    end
    if result.supplyUnknown then
        result.notes[#result.notes + 1] = "Supply counts unavailable until a complete bag snapshot is shared."
    end
    return result
end

function Prep:Report(snapshot, inventory)
    local result = self:Analyze(snapshot, inventory)
    local lines = {
        "RAID PREP  /  " .. snapshot.name .. "-" .. snapshot.realm,
        "Gear snapshot: " .. date("%Y-%m-%d %H:%M", snapshot.updated),
        result.bagUpdated and ("Bag snapshot: " .. date("%Y-%m-%d %H:%M", result.bagUpdated))
            or "Bag snapshot: unavailable",
        "",
        "GEAR",
    }
    if #result.gearIssues == 0 then lines[#lines + 1] = "No detected gear issues." end
    for _, issue in ipairs(result.gearIssues) do lines[#lines + 1] = "- " .. issue end
    lines[#lines + 1] = ""
    lines[#lines + 1] = "SUPPLIES"
    for _, line in ipairs(result.supplyLines) do lines[#lines + 1] = "- " .. line end
    for _, issue in ipairs(result.supplyIssues) do lines[#lines + 1] = "- NEEDS: " .. issue end
    lines[#lines + 1] = ""
    lines[#lines + 1] = "NOTES"
    if #result.notes == 0 then lines[#lines + 1] = "None." end
    for _, note in ipairs(result.notes) do lines[#lines + 1] = "- " .. note end
    lines[#lines + 1] = ""
    lines[#lines + 1] = "Enchant checks verify presence, not the best enchant for a spec."
    return table.concat(lines, "\n")
end
