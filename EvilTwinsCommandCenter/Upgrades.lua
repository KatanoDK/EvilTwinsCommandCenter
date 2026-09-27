local _, ET = ...
local Upgrades = {}
ET.Upgrades = Upgrades

local slots = { head = 1, neck = 2, shoulder = 3, shirt = 4, chest = 5, waist = 6,
    legs = 7, feet = 8, wrist = 9, hands = 10, finger1 = 11, finger2 = 12,
    trinket1 = 13, trinket2 = 14, back = 15, main_hand = 16, off_hand = 17, tabard = 19 }
local function number(value, minimum, maximum, integer)
    local n = tonumber(value)
    if not n or n ~= n or n < minimum or n > maximum or (integer and n % 1 ~= 0) then return nil end
    return n
end
local function text(value, maximum)
    return type(value) == "string" and #value <= maximum and not value:find("[%z\1-\31\127|]")
end
local function normalized(value) return (value or ""):lower():gsub("[%s%-_']", "") end

function Upgrades:FindOwner(report)
    if not IsInGuild() then return end
    local regions = { [1] = "us", [2] = "kr", [3] = "eu", [4] = "tw", [5] = "cn" }
    local region = GetCurrentRegion and regions[GetCurrentRegion()]
    if region and region ~= report.region then return end
    for index = 1, GetNumGuildMembers() do
        local name = GetGuildRosterInfo(index)
        if name then
            local character, realm = name:match("^([^-]+)%-(.+)$")
            character, realm = character or name, realm or (GetNormalizedRealmName() or GetRealmName())
            if normalized(character) == normalized(report.name) and normalized(realm) == normalized(report.realm) then
                return character .. "-" .. realm
            end
        end
    end
    -- The local player's roster row may still be loading.
    local name, realm = ET.key:match("^([^-]+)%-(.+)$")
    if name and normalized(name) == normalized(report.name) and normalized(realm) == normalized(report.realm)
        and ET:GetGuildRankIndex(ET.key) ~= nil then return ET.key end
end

function Upgrades:Decode(data)
    if type(data) ~= "string" or #data > 90000 then return end
    local f = ET.Protocol:Unpack(data, 1500)
    if not f or f[1] ~= "1" or #f < 17 then return end
    for _, entry in ipairs({ {2, 60}, {3, 100}, {4, 2}, {6, 40}, {7, 20}, {8, 600},
        {9, 100}, {13, 2000}, {14, 4096}, {15, 600} }) do
        if not text(f[entry[1]], entry[2]) then return end
    end
    if f[2] == "" or f[3] == "" or not f[6]:match("^[A-Za-z0-9]+$") or #f[6] < 10
        or not ({ us = true, eu = true, kr = true, tw = true, cn = true })[f[4]] then return end
    local spec = number(f[5], 1, 10000, true)
    local timestamp = number(f[7], 1, time() + 300, true)
    local duration, enemies = number(f[10], 0, 36000, true), number(f[11], 0, 1000, true)
    local baseline = number(f[12], 0.0001, 10000000000)
    local gearCount, itemCount = number(f[16], 0, 19, true), number(f[17], 0, 100, true)
    if not spec or not timestamp or not duration or not enemies or not baseline or not gearCount or not itemCount
        or #f ~= 17 + gearCount * 2 + itemCount * 14 then return end
    local report = { name = f[2], realm = f[3], region = f[4], specID = spec, id = f[6], updated = timestamp,
        title = f[8], fightStyle = f[9], duration = duration, enemies = enemies, baseline = baseline,
        scope = f[13], talents = f[14], assumptions = f[15], gear = {}, items = {}, payload = data }
    local p = 18
    for _ = 1, gearCount do
        if not slots[f[p]] or report.gear[f[p]] or not text(f[p + 1], 2000)
            or f[p + 1]:find("[^%d;/]") then return end
        report.gear[f[p]] = f[p + 1]
        p = p + 2
    end
    for _ = 1, itemCount do
        local drop, simulated = number(f[p], 1, 10000000, true), number(f[p + 1], 1, 10000000, true)
        local instance, boss = number(f[p + 4], 0, 100000, true), number(f[p + 5], 0, 100000, true)
        local level = number(f[p + 8], 1, 2000, true)
        local gain, percent = number(f[p + 10], 0.000000001, 10000000000), number(f[p + 11], 0.000000001, 1000000)
        if not drop or not simulated or not instance or not boss or not level or not gain or not percent
            or not slots[f[p + 9]] or not text(f[p + 2], 600) or not text(f[p + 3], 600)
            or not text(f[p + 6], 600) or not text(f[p + 7], 40) or not text(f[p + 13], 1000)
            or f[p + 13]:find("[^%d/]") or (f[p + 12] ~= "0" and f[p + 12] ~= "1")
            or math.abs(percent - gain / baseline * 100) > 0.00001 then return end
        report.items[#report.items + 1] = { dropID = drop, simulatedID = simulated, name = f[p + 2],
            instance = f[p + 3], instanceID = instance, bossID = boss, boss = f[p + 6], difficulty = f[p + 7],
            itemLevel = level, slot = f[p + 9], gain = gain, percent = percent, withinError = f[p + 12] == "1",
            bonusIDs = f[p + 13] }
        p = p + 14
    end
    table.sort(report.items, function(a, b) return a.gain > b.gain end)
    return report
end

function Upgrades:Store(report)
    if not ET:CanUseLeaderTools() then return false, "Leader access is required." end
    local owner = self:FindOwner(report)
    if not owner then return false, "This report's character, realm, and region must match a current guild member. Refresh your guild roster and try again." end
    ET.db.upgradeReports = ET.db.upgradeReports or {}
    local id = owner .. ":" .. report.specID .. ":" .. report.scope
    local previous = ET.db.upgradeReports[id]
    if previous and previous.updated >= report.updated then return false, "An equal or newer report is already saved for this character and source." end
    report.owner = owner
    ET.db.upgradeReports[id] = report
    local entries = {}
    for key, entry in pairs(ET.db.upgradeReports) do entries[#entries + 1] = { key = key, updated = entry.updated } end
    table.sort(entries, function(a, b) return a.updated > b.updated end)
    for index = 101, #entries do ET.db.upgradeReports[entries[index].key] = nil end
    if ET.UI then ET.UI:Refresh() end
    return true
end

function Upgrades:Import(code)
    if not ET:CanUseLeaderTools() then return false, "Leader access is required." end
    if type(code) ~= "string" then return false, "Paste an import code from the updater." end
    code = code:gsub("%s", "")
    if #code > 120005 or code:sub(1, 5) ~= "ETD1:" then return false, "Paste an ETD1 import code from the updater." end
    local data = ET.Protocol:FromBase64(code:sub(6))
    local report = data and self:Decode(data)
    if not report then return false, "Invalid or unsupported Droptimizer import code." end
    local ok, err = self:Store(report)
    if ok and ET.Comm then ET.Comm:PublishUpgrades(report) end
    return ok, err, ok and report or nil
end

function Upgrades:Receive(sender, data)
    if not ET:CanUseLeaderTools() or not ET:CanSenderUseLeaderTools(sender) then return false end
    local report = self:Decode(data)
    if not report then return false end
    return self:Store(report)
end

function Upgrades:Get(owner, specID)
    local reports = {}
    for _, report in pairs(ET.db.upgradeReports or {}) do
        if report.owner == owner and report.specID == specID then reports[#reports + 1] = report end
    end
    table.sort(reports, function(a, b) return a.updated > b.updated end)
    return reports
end

function Upgrades:GearSignature(encoded)
    local options, signature = {}, {}
    for key, value in encoded:gmatch(",([%w_]+)=([^,]*)") do options[key] = value end
    for _, key in ipairs({ "id", "enchant_id", "gem_id", "bonus_id", "crafted_stats" }) do
        local values = {}
        for value in (options[key] or ""):gmatch("%d+") do
            value = tonumber(value)
            if value > 0 then values[#values + 1] = value end
        end
        table.sort(values)
        signature[#signature + 1] = table.concat(values, "/")
    end
    return table.concat(signature, ";")
end

function Upgrades:Freshness(report, snapshot)
    if not snapshot or snapshot.specID ~= report.specID then return "Different specialization — resim" end
    if report.talents ~= "" and snapshot.talent ~= "" and report.talents ~= snapshot.talent then return "Talents changed — resim" end
    local checked, unknown = 0, false
    for slot, signature in pairs(report.gear) do
        local link
        for index, definition in ipairs(ET.slots) do
            if definition[1] == slots[slot] then link = snapshot.gear[index]; break end
        end
        local encoded = link and link ~= "" and ET.SimC:ItemLine(slot, link)
        if not encoded then unknown = true
        elseif self:GearSignature(encoded) ~= signature then return "Gear, enchants or gems changed — resim"
        else checked = checked + 1 end
    end
    if unknown or checked == 0 or report.talents == "" or snapshot.talent == "" then return "Build match not verified" end
    if time() - report.updated > 7 * 86400 then return "Report is over a week old — consider resimming" end
    if (snapshot.updated or 0) < report.updated then return "Saved build matches; refresh gear to confirm" end
    return "Matches saved gear and talents"
end
