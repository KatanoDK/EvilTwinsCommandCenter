local addonName, ET = ...
ET.name = addonName
ET.version = "0.1.0"
ET.slots = {
    { 1, "Head" }, { 2, "Neck" }, { 3, "Shoulder" }, { 4, "Shirt" },
    { 5, "Chest" }, { 6, "Waist" }, { 7, "Legs" }, { 8, "Feet" },
    { 9, "Wrist" }, { 10, "Hands" }, { 11, "Finger 1" }, { 12, "Finger 2" },
    { 13, "Trinket 1" }, { 14, "Trinket 2" }, { 15, "Back" },
    { 16, "Main hand" }, { 17, "Off hand" }, { 18, "Ranged" }, { 19, "Tabard" },
}

local function trim(value)
    return (value or ""):match("^%s*(.-)%s*$")
end

function ET:Print(message)
    DEFAULT_CHAT_FRAME:AddMessage("|cff9b72ffEvil Twins CC:|r " .. message)
end

function ET:PlayerKey()
    local name, realm = UnitFullName("player")
    if not realm or realm == "" then realm = GetNormalizedRealmName() or GetRealmName() end
    return name .. "-" .. realm
end

function ET:InitDB()
    if type(EvilTwinsCCDB) ~= "table" then EvilTwinsCCDB = {} end
    local db = EvilTwinsCCDB
    if type(db.settings) ~= "table" then db.settings = {} end
    if type(db.characters) ~= "table" then db.characters = {} end
    if type(db.wishlists) ~= "table" then db.wishlists = {} end
    if db.settings.shareProfile == nil then db.settings.shareProfile = false end
    if db.settings.shareSimC == nil then db.settings.shareSimC = false end
    self.db = db
    self.key = self:PlayerKey()
    if type(db.wishlists[self.key]) ~= "table" then db.wishlists[self.key] = {} end
end

function ET:CaptureSelf()
    if not self.db then return end
    local name, realm = UnitFullName("player")
    if not realm or realm == "" then realm = GetNormalizedRealmName() or GetRealmName() end
    local _, classFile = UnitClass("player")
    local specIndex = GetSpecialization()
    local specID, specName = 0, "No specialization"
    if specIndex then specID, specName = GetSpecializationInfo(specIndex) end
    local talentString = ""
    if C_ClassTalents and C_ClassTalents.GetActiveConfigID and C_Traits and C_Traits.GenerateImportString then
        local configID = C_ClassTalents.GetActiveConfigID()
        if configID then
            local ok, result = pcall(C_Traits.GenerateImportString, configID)
            if ok and type(result) == "string" then talentString = result end
        end
    end
    local equipped = select(2, GetAverageItemLevel()) or 0
    local gear = {}
    for index, slot in ipairs(self.slots) do
        gear[index] = GetInventoryItemLink("player", slot[1]) or ""
    end
    local snapshot = {
        guid = UnitGUID("player") or "",
        name = name,
        realm = realm,
        class = classFile or "",
        specID = specID or 0,
        spec = specName or "",
        level = UnitLevel("player") or 0,
        itemLevel = math.floor(equipped + 0.5),
        updated = time(),
        talent = talentString,
        gear = gear,
        wishlist = self.db.wishlists[self.key],
    }
    self.db.characters[self.key] = snapshot
    if self.UI then self.UI:Refresh() end
    return snapshot
end

function ET:SchedulePublish()
    if self.publishTimer then self.publishTimer:Cancel() end
    self.publishTimer = C_Timer.NewTimer(3, function()
        self.publishTimer = nil
        local snapshot = self:CaptureSelf()
        if snapshot and self.db.settings.shareProfile and self.Comm then
            self.Comm:Publish(snapshot)
        end
    end)
end

function ET:AddWish(itemText, note)
    local itemID = tonumber((itemText or ""):match("item:(%d+)")) or tonumber(trim(itemText))
    if not itemID or itemID < 1 or itemID > 99999999 then return false, "Enter an item link or item ID." end
    note = trim(note):sub(1, 100)
    local list = self.db.wishlists[self.key]
    for _, wish in ipairs(list) do
        if wish.id == itemID then return false, "That item is already on your wishlist." end
    end
    if #list >= 20 then return false, "The wishlist is limited to 20 items." end
    list[#list + 1] = { id = itemID, note = note }
    self:SchedulePublish()
    return true
end

function ET:RemoveWish(index)
    local list = self.db.wishlists[self.key]
    if not list[index] then return end
    table.remove(list, index)
    self:SchedulePublish()
end

function ET:GetSimCProfile()
    local api = _G.SimulationcraftAPI
    local simc = _G.Simulationcraft
    if not api or type(api.GetSimcProfile) ~= "function" or not simc then
        return nil, "Install the current SimulationCraft addon to share SimC profiles."
    end
    -- noBags=true keeps private bag and bank contents out of guild transfers.
    local ok, profile, err = pcall(api.GetSimcProfile, simc, false, true, false, nil)
    if not ok then return nil, "SimulationCraft could not produce a profile." end
    if type(profile) ~= "string" or #profile == 0 then
        return nil, type(err) == "string" and err or "SimulationCraft returned no profile."
    end
    return profile
end

local events = CreateFrame("Frame")
events:RegisterEvent("PLAYER_LOGIN")
events:SetScript("OnEvent", function(_, event, ...)
    if event == "PLAYER_LOGIN" then
        ET:InitDB()
        ET:CaptureSelf()
        ET.Comm:Initialize()
        ET:Print("Ready. Type /etcc to open the command center.")
        return
    end
    if event == "PLAYER_EQUIPMENT_CHANGED" or event == "PLAYER_SPECIALIZATION_CHANGED"
        or event == "TRAIT_CONFIG_UPDATED" or event == "PLAYER_LEVEL_UP" then
        ET:SchedulePublish()
    end
end)
events:RegisterEvent("PLAYER_EQUIPMENT_CHANGED")
events:RegisterEvent("PLAYER_SPECIALIZATION_CHANGED")
events:RegisterEvent("TRAIT_CONFIG_UPDATED")
events:RegisterEvent("PLAYER_LEVEL_UP")

SLASH_EVILTWINSCC1 = "/etcc"
SLASH_EVILTWINSCC2 = "/eviltwins"
SlashCmdList.EVILTWINSCC = function(input)
    if input == "test" then
        if not ET.db then ET:Print("Wait until character login finishes."); return end
        local snapshot = ET:CaptureSelf()
        local packed = ET.Protocol:EncodeSnapshot(snapshot)
        local encoded = ET.Protocol:ToBase64(packed)
        local decoded = ET.Protocol:DecodeSnapshot(ET.Protocol:FromBase64(encoded) or "")
        if decoded and decoded.guid == snapshot.guid and decoded.talent == snapshot.talent
            and #decoded.gear == #ET.slots then
            ET:Print("Snapshot and transfer encoding passed. SimulationCraft API: "
                .. ((_G.SimulationcraftAPI and "available") or "not installed"))
        else
            ET:Print("Self-test failed. Please report this with your WoW version.")
        end
        return
    end
    if ET.UI then ET.UI:Toggle() end
end
