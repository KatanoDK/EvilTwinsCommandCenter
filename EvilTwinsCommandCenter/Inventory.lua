local _, ET = ...
local Inventory = {}
ET.Inventory = Inventory

local function bagCount()
    local constants = Constants and Constants.InventoryConstants
    if constants and constants.NumBagSlots then
        return 1 + constants.NumBagSlots + (constants.NumReagentBagSlots or 0)
    end
    return 1 + (NUM_TOTAL_EQUIPPED_BAG_SLOTS or 5)
end

function Inventory:BagCount()
    return bagCount()
end

local function sameItems(a, b)
    if not a or #a ~= #b then return false end
    for index, item in ipairs(a) do
        local other = b[index]
        if item.slot ~= other.slot or item.id ~= other.id or item.count ~= other.count
            or item.link ~= other.link then return false end
    end
    return true
end

function Inventory:Get(key, guid)
    local entry = ET.db.inventory[key]
    if type(entry) == "table" and entry.guid == guid and type(entry.containers) == "table" then
        return entry
    end
end

function Inventory:Store(key, guid, bagID, updated, items)
    if type(key) ~= "string" or type(guid) ~= "string" or #guid == 0 or #guid > 64
        or type(bagID) ~= "number" or bagID < 0 or bagID >= bagCount()
        or type(updated) ~= "number" or updated <= 0 or updated > time() + 300
        or type(items) ~= "table" then return false end
    local snapshot = ET.db.characters[key]
    if snapshot and snapshot.guid ~= guid then return false end
    local entry = self:Get(key, guid)
    if not entry then
        entry = { guid = guid, containers = {} }
        ET.db.inventory[key] = entry
    end
    local previous = entry.containers[bagID]
    if previous and previous.updated > updated then return false end
    entry.containers[bagID] = { updated = updated, items = items }
    if ET.UI then ET.UI:Refresh() end
    return true
end

function Inventory:ScanBag(bagID, publish)
    local slots = C_Container.GetContainerNumSlots(bagID) or 0
    local guid = UnitGUID("player")
    local entry = self:Get(ET.key, guid)
    local previous = entry and entry.containers[bagID]
    local items = {}
    for slot = 1, slots do
        local itemID = C_Container.GetContainerItemID(bagID, slot)
        local info = C_Container.GetContainerItemInfo(bagID, slot)
        if type(itemID) == "number" and itemID > 0 then
            local link = info and type(info.hyperlink) == "string" and info.hyperlink
                or C_Container.GetContainerItemLink(bagID, slot) or ""
            if #link > 500 then link = "" end
            items[#items + 1] = {
                slot = slot, id = itemID, count = info and info.stackCount or 1,
                link = link,
            }
        end
    end
    if previous and sameItems(previous.items, items) then return end
    if self:Store(ET.key, guid, bagID, time(), items) and publish then
        ET.Comm:PublishBag(bagID)
    end
end

function Inventory:CaptureBags(publish)
    if InCombatLockdown and InCombatLockdown() then return end
    for bagID = 0, bagCount() - 1 do self:ScanBag(bagID, publish) end
end

function Inventory:ScheduleScan()
    if self.scanTimer then self.scanTimer:Cancel() end
    self.scanTimer = C_Timer.NewTimer(2, function()
        self.scanTimer = nil
        self:CaptureBags(true)
    end)
end

function Inventory:Format(key, guid)
    local entry = self:Get(key, guid)
    if not entry then return nil end
    local lines = { "Last shared bags: " .. key, "" }
    for bagID = 0, bagCount() - 1 do
        local bag = entry.containers[bagID]
        if bag then
            lines[#lines + 1] = "Bag " .. bagID .. "  (" .. date("%Y-%m-%d %H:%M", bag.updated) .. ")"
            if #bag.items == 0 then lines[#lines + 1] = "  Empty" end
            for _, item in ipairs(bag.items) do
                local name = (item.link or ""):match("%[(.-)%]") or GetItemInfo(item.id) or ("Item #" .. item.id)
                lines[#lines + 1] = "  " .. name .. " x" .. item.count .. "  [item:" .. item.id .. "]"
            end
            lines[#lines + 1] = ""
        end
    end
    return table.concat(lines, "\n")
end

function Inventory:Initialize()
    self.events = CreateFrame("Frame")
    self.events:RegisterEvent("BAG_UPDATE_DELAYED")
    self.events:RegisterEvent("PLAYER_REGEN_ENABLED")
    self.events:SetScript("OnEvent", function() self:ScheduleScan() end)
    C_Timer.After(2, function() self:CaptureBags(true) end)
end
