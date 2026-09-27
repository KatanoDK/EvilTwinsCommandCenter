local _, ET = ...
local Raid = {}
ET.RaidSimC = Raid

local nonDps = { [250] = true, [581] = true, [104] = true, [105] = true, [1468] = true,
    [268] = true, [270] = true, [65] = true, [66] = true, [256] = true, [257] = true,
    [264] = true, [73] = true }
local specs = {
    deathknight = { blood = 250, frost = 251, unholy = 252 },
    demonhunter = { havoc = 577, vengeance = 581, devourer = 1480 },
    druid = { balance = 102, feral = 103, guardian = 104, restoration = 105 },
    evoker = { devastation = 1467, preservation = 1468, augmentation = 1473 },
    hunter = { beast_mastery = 253, marksmanship = 254, survival = 255 },
    mage = { arcane = 62, fire = 63, frost = 64 }, monk = { brewmaster = 268, windwalker = 269, mistweaver = 270 },
    paladin = { holy = 65, protection = 66, retribution = 70 }, priest = { discipline = 256, holy = 257, shadow = 258 },
    rogue = { assassination = 259, outlaw = 260, subtlety = 261 },
    shaman = { elemental = 262, enhancement = 263, restoration = 264 },
    warlock = { affliction = 265, demonology = 266, destruction = 267 }, warrior = { arms = 71, fury = 72, protection = 73 },
}
local function normalize(key) return (key or ""):lower():gsub("%s+", "") end
function Raid:Spec(profile)
    local class, spec
    for line in (profile or ""):gmatch("[^\r\n]+") do
        local key, value = line:match("^%s*([%w_]+)%s*=%s*(.-)%s*$")
        if key then
            local classKey = key:gsub("_", "")
            if specs[classKey] then class = classKey end
            if key == "spec" then spec = value end
        end
    end
    return class and spec and specs[class][spec] or 0
end

function Raid:Members()
    if not ET:CanUseLeaderTools() then return {}, "Leader access is required." end
    if InCombatLockdown() then return {}, "Collect raid profiles after combat." end
    if not IsInRaid() then return {}, "Join a raid group to collect its DPS profiles." end
    local entries = {}
    for index = 1, math.min(40, GetNumGroupMembers()) do
        local unit = "raid" .. index
        local name, realm = UnitFullName(unit)
        local guid = UnitGUID(unit)
        if name and guid then
            realm = realm and realm ~= "" and realm or (GetNormalizedRealmName() or GetRealmName())
            local key = name .. "-" .. realm
            local cached
            for storedKey in pairs(ET.db.simcProfiles) do
                if normalize(storedKey) == normalize(key) then cached = ET:GetCachedSimC(storedKey, guid); break end
            end
            local role = UnitGroupRolesAssigned(unit) or "NONE"
            local spec = cached and self:Spec(cached.profile) or 0
            local state = "Missing profile"
            if role == "TANK" or role == "HEALER" or nonDps[spec] then state = "Skipped: tank/healer"
            elseif ET:GetGuildRankIndex(key) == nil then state = "Missing: not a guild member"
            elseif cached and spec > 0 then state = "Ready"
            elseif cached then state = "Missing: unsupported specialization" end
            entries[#entries + 1] = { key = key, guid = guid, role = role, spec = spec,
                updated = cached and cached.updated or 0, profile = state == "Ready" and cached.profile or "", state = state }
        end
    end
    return entries
end

function Raid:Refresh()
    if not ET:CanUseLeaderTools() then return false, "Leader access is required." end
    if InCombatLockdown() then return false, "Collect raid profiles after combat." end
    if self.lastRequest and time() - self.lastRequest < 30 then return false, "Wait 30 seconds before requesting again." end
    local members, err = self:Members()
    if err then return false, err end
    self.lastRequest = time()
    local count = 0
    for _, member in ipairs(members) do
        if member.role ~= "TANK" and member.role ~= "HEALER" and ET:GetGuildRankIndex(member.key) ~= nil then
            if member.guid == UnitGUID("player") then
                local profile = ET:GetSimCProfile()
                if profile and ET:StoreSimC(ET.key, member.guid, time(), profile) then count = count + 1 end
            elseif ET.Comm:RequestSimC(member.key, member.guid) then count = count + 1 end
        end
    end
    return true, "Requested " .. count .. " raid profiles. Online guildmates with the addon reply automatically."
end

function Raid:Export()
    local members, err = self:Members()
    if err then return nil, err end
    local regions = { [1] = "us", [2] = "kr", [3] = "eu", [4] = "tw", [5] = "cn" }
    local region = GetCurrentRegion and regions[GetCurrentRegion()]
    if not region then return nil, "This region is not supported." end
    local fields = { "1", tostring(time()), region, tostring(#members) }
    for _, member in ipairs(members) do
        -- Droptimizer uses the equipped baseline. Omit comment-only bag alternatives
        -- and collector diagnostics to keep a raid-sized clipboard export small.
        local lines = {}
        for line in member.profile:gmatch("[^\r\n]+") do
            if not line:match("^%s*#") and not line:match("^%s*$") then lines[#lines + 1] = line end
        end
        local profile = #lines > 0 and (table.concat(lines, "\n") .. "\n") or ""
        for _, value in ipairs({ member.key, member.guid, member.role, tostring(member.spec), tostring(member.updated), profile, member.state }) do
            fields[#fields + 1] = value
        end
    end
    local packed = ET.Protocol:Pack(fields)
    if #packed > 3600000 then return nil, "This raid export exceeds the clipboard limit." end
    return "ETR1:" .. ET.Protocol:ToBase64(packed)
end

function ET.UI:ShowRaidSimC()
    if not ET:CanUseLeaderTools() then return end
    if not self.raidSimCFrame then
        local frame = CreateFrame("Frame", "EvilTwinsCCRaidSimC", UIParent, "BackdropTemplate")
        frame:SetSize(680, 600); frame:SetPoint("CENTER"); frame:SetFrameStrata("DIALOG"); ET:StyleWindow(frame)
        frame:EnableMouse(true); frame:SetMovable(true); frame:RegisterForDrag("LeftButton")
        frame:SetScript("OnDragStart", frame.StartMoving); frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
        local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton"); close:SetPoint("TOPRIGHT", -4, -4)
        local title = frame:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
        title:SetPoint("TOPLEFT", 22, -22); title:SetText("RAID DPS EXPORT")
        local help = frame:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
        help:SetPoint("TOPLEFT", 22, -55); help:SetWidth(620); help:SetJustifyH("LEFT")
        help:SetText("Refresh profiles, then copy the raid into the desktop app's Raid upgrades page.\nTanks and healers are skipped. Cached profiles show their last capture time.")
        local scroll = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
        scroll:SetPoint("TOPLEFT", 22, -100); scroll:SetPoint("BOTTOMRIGHT", -40, 108)
        local list = CreateFrame("EditBox", nil, scroll); list:SetMultiLine(true); list:SetAutoFocus(false)
        list:SetFontObject(ChatFontNormal); list:SetWidth(600); list:SetHeight(380)
        list:SetScript("OnEscapePressed", function() frame:Hide() end); scroll:SetScrollChild(list)
        local status = frame:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
        status:SetPoint("BOTTOMLEFT", 22, 65); status:SetWidth(620); status:SetJustifyH("LEFT")
        local function action(text, x, callback)
            local b = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
            b:SetSize(190, 28); b:SetPoint("BOTTOMLEFT", x, 26); b:SetText(text); b:SetScript("OnClick", callback)
        end
        action("Refresh raid SimC", 22, function() local _, message = Raid:Refresh(); status:SetText(message) end)
        action("Copy raid export", 224, function()
            local code, err = Raid:Export()
            if code then self:ShowCopy("Raid DPS export — paste into the desktop app", code) else status:SetText(err) end
        end)
        frame:SetScript("OnUpdate", function(_, elapsed)
            frame.elapsed = (frame.elapsed or 0) + elapsed
            if frame.elapsed < 1 then return end
            frame.elapsed = 0
            if not ET:CanUseLeaderTools() then frame:Hide(); return end
            local members, err = Raid:Members(); local lines = {}
            for _, member in ipairs(members) do
                lines[#lines + 1] = member.key .. " — " .. member.state
                    .. (member.updated > 0 and (" (" .. date("%m/%d %H:%M", member.updated) .. ")") or "")
            end
            local text = err or table.concat(lines, "\n")
            if list:GetText() ~= text then list:SetText(text); list:SetHeight(math.max(380, #lines * 20)) end
        end)
        self.raidSimCFrame = frame
        status:SetText("Missing profiles need the addon enabled. Refresh outside combat and wait for replies.")
    end
    self.raidSimCFrame.elapsed = 1
    self.raidSimCFrame:Show()
end
