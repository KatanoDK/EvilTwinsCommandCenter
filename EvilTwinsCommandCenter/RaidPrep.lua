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
    -- Supply policy is automatic; older officer-specific checklists no longer apply.
    ET.db.prepChecklist = nil
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
    if link == "" then return false end
    if not C_Item or not C_Item.GetItemInfoInstant then return nil end
    local ok, _, _, _, equipType = pcall(C_Item.GetItemInfoInstant, link)
    if not ok or type(equipType) ~= "string" or equipType == "" then return nil end
    return weaponTypes[equipType] == true
end

local requestedItems = {}
local function itemName(id, link)
    local name = link and link:match("%[(.-)%]") or GetItemInfo(id)
    if not name and not requestedItems[id] and C_Item and C_Item.RequestLoadItemDataByID then
        requestedItems[id] = true
        C_Item.RequestLoadItemDataByID(id)
    end
    return name or ("Item #" .. id)
end

local function readable(value, kind)
    return not (issecretvalue and issecretvalue(value)) and type(value) == kind
end

local function itemDataReady(link)
    local id = tonumber(link:match("item:(%d+)"))
    if not id or not C_Item or not C_Item.IsItemDataCachedByID then return true end
    local ok, cached = pcall(C_Item.IsItemDataCachedByID, id)
    if not ok or not readable(cached, "boolean") then return false end
    if not cached and not requestedItems[id] and C_Item.RequestLoadItemDataByID then
        requestedItems[id] = true
        C_Item.RequestLoadItemDataByID(id)
    end
    return cached
end

function Prep:GearDetails(snapshot)
    local rows = {}
    for index, slot in ipairs(ET.slots) do
        local link = snapshot.gear[index] or ""
        local enchantable = shouldEnchant(index, link)
        local count, gemIDs, gemNames, empty = nil, {}, {}, 0
        if link ~= "" and itemDataReady(link) and C_Item and C_Item.GetItemNumSockets then
            local ok, value = pcall(C_Item.GetItemNumSockets, link)
            if ok and readable(value, "number") then count = value end
        end
        if count then
            for gemIndex = 1, math.min(count, 8) do
                local ok, id = false, nil
                if C_Item and C_Item.GetItemGemID then ok, id = pcall(C_Item.GetItemGemID, link, gemIndex) end
                if not ok or (issecretvalue and issecretvalue(id)) or (id ~= nil and not readable(id, "number")) then
                    count = nil
                    break
                elseif not id or id == 0 then
                    empty = empty + 1
                    gemNames[#gemNames + 1] = "Empty socket"
                else
                    gemIDs[#gemIDs + 1] = id
                    gemNames[#gemNames + 1] = itemName(id)
                end
            end
        end
        if enchantable or enchantable == nil or (count and count > 0) or (link ~= "" and count == nil) then
            local enchantField = link:match("item:%d+:([^:]*)")
            local enchantID = enchantField ~= nil and (tonumber(enchantField) or 0) or nil
            local enchantText = "—"
            local enchantStatus = "none"
            if enchantable == nil then enchantText, enchantStatus = "Enchantability unknown", "unknown"
            elseif enchantable then
                if link == "" then enchantText, enchantStatus = "Missing item", "missing"
                elseif enchantID == nil then enchantText, enchantStatus = "Unknown", "unknown"
                elseif enchantID == 0 then enchantText, enchantStatus = "Missing enchant", "missing"
                else
                    enchantText, enchantStatus = "Enchant #" .. enchantID, "ok"
                    if C_TooltipInfo and C_TooltipInfo.GetHyperlink then
                        local ok, data = pcall(C_TooltipInfo.GetHyperlink, link)
                        local kind = Enum and Enum.TooltipDataLineType and Enum.TooltipDataLineType.ItemEnchantmentPermanent
                        if ok and type(data) == "table" and type(data.lines) == "table" and kind then
                            for _, line in ipairs(data.lines) do
                                if readable(line.type, "number") and line.type == kind and readable(line.leftText, "string") then
                                    enchantText = line.leftText
                                    break
                                end
                            end
                        end
                    end
                end
            end
            rows[#rows + 1] = { index = index, slot = slot[2], link = link, enchantID = enchantID,
                enchantText = enchantText, enchantStatus = enchantStatus, gemIDs = gemIDs,
                gemText = link == "" and "—" or count == nil and "Sockets unknown"
                    or count == 0 and "No sockets" or table.concat(gemNames, ", "),
                gemStatus = link == "" and "none" or count == nil and "unknown"
                    or empty > 0 and "missing" or count > 0 and "ok" or "none" }
        end
    end
    return rows
end

local function getEmptySockets(link)
    if not C_Item or not C_Item.GetItemNumSockets or not C_Item.GetItemGemID then return nil end
    if not itemDataReady(link) then return nil end
    local ok, count = pcall(C_Item.GetItemNumSockets, link)
    if not ok or not readable(count, "number") then return nil end
    local empty = 0
    for index = 1, math.min(count, 8) do
        local gemOK, gemID = pcall(C_Item.GetItemGemID, link, index)
        if not gemOK or (issecretvalue and issecretvalue(gemID)) then return nil end
        if not gemID or gemID == 0 then empty = empty + 1 end
    end
    return empty
end

local function bagCounts(inventory)
    if not inventory or type(inventory.containers) ~= "table" then return nil end
    local totals, oldest, links = {}, math.huge, {}
    for bagID = 0, ET.Inventory:BagCount() - 1 do
        local bag = inventory.containers[bagID]
        if not bag or type(bag.items) ~= "table" then return nil end
        oldest = math.min(oldest, bag.updated or 0)
        for _, item in ipairs(bag.items) do
            totals[item.id] = (totals[item.id] or 0) + item.count
            links[item.id] = item.link
        end
    end
    return totals, oldest, links
end

function Prep:Analyze(snapshot, inventory)
    local result = { gearIssues = {}, supplyIssues = {}, supplyLines = {}, notes = {},
        missingGear = 0, missingEnchants = 0, emptySockets = 0, repairCount = 0,
        unknownEnchants = 0, unknownSockets = 0, unknownDurability = 0,
        configuredSupplies = #self.categories, supplyUnknown = false, bagStale = false, supplies = {} }
    for _, index in ipairs(requiredSlots) do
        if not snapshot.gear[index] or snapshot.gear[index] == "" then
            result.missingGear = result.missingGear + 1
            result.gearIssues[#result.gearIssues + 1] = "Missing equipped item: " .. slotName(index)
        end
    end
    local unknownSockets, unknownEnchants, unknownDurability = 0, 0, 0
    for index, link in ipairs(snapshot.gear) do
        if link ~= "" then
            local enchantable = shouldEnchant(index, link)
            if enchantable == nil then unknownEnchants = unknownEnchants + 1
            elseif enchantable then
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

    local totals, bagUpdated, links = bagCounts(inventory)
    if totals then
        result.bagUpdated = bagUpdated
        if time() - bagUpdated > 86400 then
            result.bagStale = true
            result.notes[#result.notes + 1] = "Bag snapshot is over 24 hours old."
        end
    end
    for _, category in ipairs(self.categories) do
        local supply = { key = category.key, label = category.label, minimum = 1, items = {}, count = 0 }
        result.supplies[#result.supplies + 1] = supply
        if not totals then
            result.supplyUnknown = true
            supply.status = "unknown"
            result.supplyLines[#result.supplyLines + 1] = category.label .. ": bag data unavailable"
        else
            local seen = {}
            for _, id in ipairs(ET.Consumables.categories[category.key]) do
                if not seen[id] and (totals[id] or 0) > 0 then
                    seen[id] = true
                    supply.count = supply.count + totals[id]
                    supply.items[#supply.items + 1] = { id = id, count = totals[id], name = itemName(id, links[id]) }
                end
            end
            supply.status = result.bagStale and "stale" or supply.count >= 1 and "ok" or "missing"
            result.supplyLines[#result.supplyLines + 1] = category.label .. ": " .. supply.count .. "/1"
            if supply.count < 1 then
                result.supplyIssues[#result.supplyIssues + 1] = category.label .. " missing"
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
