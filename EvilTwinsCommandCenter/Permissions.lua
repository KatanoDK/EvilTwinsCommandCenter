local _, ET = ...

-- Guild rank indices start at 0 for the Guild Master; larger indices are lower ranks.
ET.leaderRankCutoff = 5

local function normalize(name)
    return (name or ""):lower():gsub("%s+", "")
end

function ET:GetGuildRankIndex(name)
    if not IsInGuild() or type(name) ~= "string" then return nil end
    local realm = GetNormalizedRealmName() or GetRealmName()
    local wanted = normalize(name)
    if not wanted:find("-", 1, true) then wanted = wanted .. "-" .. normalize(realm) end
    for index = 1, GetNumGuildMembers() do
        local rosterName, _, rankIndex = GetGuildRosterInfo(index)
        if rosterName and type(rankIndex) == "number" then
            local fullName = normalize(rosterName)
            if not fullName:find("-", 1, true) then fullName = fullName .. "-" .. normalize(realm) end
            if fullName == wanted then return rankIndex end
        end
    end
    if wanted == normalize(self.key) then
        local guildName, _, rankIndex = GetGuildInfo("player")
        if guildName and type(rankIndex) == "number" then return rankIndex end
    end
    return nil
end

function ET:CanUseLeaderTools()
    local rankIndex = self:GetGuildRankIndex(self.key)
    return rankIndex ~= nil and rankIndex <= self.leaderRankCutoff
end

function ET:CanSenderUseLeaderTools(sender)
    local rankIndex = self:GetGuildRankIndex(sender)
    return rankIndex ~= nil and rankIndex <= self.leaderRankCutoff
end

function ET:EnforceGuildAccess()
    local rankIndex = self:GetGuildRankIndex(self.key)
    if IsInGuild() and rankIndex == nil then return end
    if rankIndex and rankIndex <= self.leaderRankCutoff then return end
    for key in pairs(self.db.characters) do
        if key ~= self.key then self.db.characters[key] = nil end
    end
    for key in pairs(self.db.simcProfiles) do
        if key ~= self.key then self.db.simcProfiles[key] = nil end
    end
    for key in pairs(self.db.inventory) do
        if key ~= self.key then self.db.inventory[key] = nil end
    end
    self.db.upgradeReports = {}
    if self.UI then self.UI:Refresh() end
end
