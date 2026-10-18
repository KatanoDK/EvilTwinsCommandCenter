local _, ET = ...
local Comm = { prefix = "EvilTwinsCC", queue = {}, leaderJobs = {}, peers = {}, craftingJobs = {}, incoming = {}, pendingSimC = {},
    gearReplies = {}, pendingGearReplies = {} }
ET.Comm = Comm

local CHUNK_SIZE = 190
local MAX_PARTS = 650
local MAX_QUEUE = 1400
local LOCKDOWN = Enum.SendAddonMessageResult and Enum.SendAddonMessageResult.AddOnMessageLockdown or 11
local THROTTLE = Enum.SendAddonMessageResult and Enum.SendAddonMessageResult.AddonMessageThrottle or 3
local CHANNEL_THROTTLE = Enum.SendAddonMessageResult and Enum.SendAddonMessageResult.ChannelThrottle or 8

local function normalize(name)
    return (name or ""):lower():gsub("%s+", "")
end

local function peerKey(name)
    name = normalize(name)
    if not name:find("-", 1, true) then
        name = name .. "-" .. normalize(GetNormalizedRealmName() or GetRealmName())
    end
    return name
end

function Comm:HasAddonPeer(name)
    return self.peers[peerKey(name)] == true
end

function Comm:RefreshPeers()
    local online = {}
    for index = 1, GetNumGuildMembers() do
        local name, _, _, _, _, _, _, _, connected = GetGuildRosterInfo(index)
        if name and connected then online[peerKey(name)] = true end
    end
    for key in pairs(self.peers) do
        if not online[key] then self.peers[key] = nil end
    end
end

function Comm:IsGuildSender(sender)
    return ET:GetGuildRankIndex(sender) ~= nil
end

function Comm:Queue(message, channel, target, priority)
    if #self.queue >= MAX_QUEUE then return false end
    local entry = { message = message, channel = channel, target = target }
    if priority then table.insert(self.queue, 1, entry)
    else self.queue[#self.queue + 1] = entry end
    return true
end

function Comm:QueueTransfer(kind, text, channel, target, transferKey)
    if type(text) ~= "string" or #text > 90000 then return false end
    text = ET.Protocol:ToBase64(text)
    local total = math.max(1, math.ceil(#text / CHUNK_SIZE))
    local limit = kind == "P" and MAX_QUEUE - 100 or MAX_QUEUE
    if total > MAX_PARTS or #self.queue + total > limit then return false end
    self.nextID = (self.nextID or 0) + 1
    local id = tostring(time()) .. "-" .. tostring(self.nextID)
    for index = 1, total do
        local chunk = text:sub((index - 1) * CHUNK_SIZE + 1, index * CHUNK_SIZE)
        self:Queue("E|" .. kind .. "|" .. id .. "|" .. index .. "|" .. total .. "|" .. chunk,
            channel, target, kind == "S")
        local entry = self.queue[kind == "S" and 1 or #self.queue]
        entry.transferKey = transferKey
    end
    return true
end

function Comm:ScheduleLeaderTransfer(kind, data, target, identity)
    if type(data) ~= "string" or #data > 90000 then return false end
    local key = kind .. ":" .. peerKey(target) .. ":" .. tostring(identity or "")
    -- Keep only the latest waiting update for each recipient and bag/profile.
    for _, job in ipairs(self.leaderJobs) do
        if job.key == key then job.data = data; return true end
    end
    if #self.leaderJobs >= 1000 then return false end
    local job = { kind = kind, data = data, target = target, key = key }
    if kind == "S" then table.insert(self.leaderJobs, 1, job)
    else self.leaderJobs[#self.leaderJobs + 1] = job end
    return true
end

function Comm:FillLeaderQueue()
    if #self.queue > 200 then return end
    for index, job in ipairs(self.leaderJobs) do
        if not self:HasAddonPeer(job.target) or not ET:CanSenderUseLeaderTools(job.target) then
            table.remove(self.leaderJobs, index)
            return
        end
        local active = false
        for _, entry in ipairs(self.queue) do
            if entry.transferKey == job.key then active = true; break end
        end
        if not active then
            if self:QueueTransfer(job.kind, job.data, "WHISPER", job.target, job.key) then
                table.remove(self.leaderJobs, index)
            end
            return
        end
    end
end

function Comm:QueueToLeaders(kind, data, target, identity)
    if target then
        if not ET:CanSenderUseLeaderTools(target) then return false end
        if kind == "D" then return self:QueueTransfer(kind, data, "WHISPER", target) end
        return self:ScheduleLeaderTransfer(kind, data, target, identity)
    end
    local sent = false
    for index = 1, GetNumGuildMembers() do
        local name, _, rankIndex, _, _, _, _, _, online = GetGuildRosterInfo(index)
        local fullName = name and (name:find("-", 1, true) and name
            or (name .. "-" .. (GetNormalizedRealmName() or GetRealmName())))
        if name and online and type(rankIndex) == "number"
            and rankIndex <= ET.leaderRankCutoff and normalize(fullName) ~= normalize(ET.key)
            and self:HasAddonPeer(name) then
            if self:ScheduleLeaderTransfer(kind, data, name, identity) then
                sent = true
            else
                ET:Print("The sharing queue is full; some leaders may need to request a fresh copy.")
                break
            end
        end
    end
    return sent
end

function Comm:Pump()
    self:FillLeaderQueue()
    self:FillCraftingQueue()
    self:FillUpgradesQueue()
    if #self.queue == 0 then return end
    if C_ChatInfo.InChatMessagingLockdown and C_ChatInfo.InChatMessagingLockdown() then return end
    local entry = self.queue[1]
    if entry.transferKey and not self:HasAddonPeer(entry.target) then
        table.remove(self.queue, 1)
        return
    end
    if entry.channel == "WHISPER" and entry.message:match("^E|[SPICD]|") then
        local rankIndex = ET:GetGuildRankIndex(entry.target)
        if rankIndex == nil then
            if not entry.rosterWaitUntil then
                entry.rosterWaitUntil = time() + 15
                if C_GuildInfo and C_GuildInfo.GuildRoster then C_GuildInfo.GuildRoster() end
            end
            if time() < entry.rosterWaitUntil then return end
        end
        if rankIndex == nil or (not entry.message:match("^E|C|") and rankIndex > ET.leaderRankCutoff) then
            table.remove(self.queue, 1)
            return
        end
    end
    local result = C_ChatInfo.SendAddonMessage(self.prefix, entry.message, entry.channel, entry.target)
    if result == 0 or result == true then
        table.remove(self.queue, 1)
    elseif result ~= LOCKDOWN and result ~= THROTTLE and result ~= CHANNEL_THROTTLE then
        table.remove(self.queue, 1)
    end
end

function Comm:Publish(snapshot, target)
    if not IsInGuild() then return false end
    local data = ET.Protocol:EncodeSnapshot(snapshot)
    local sent = self:QueueToLeaders("S", data, target)
    if target and not sent then
        ET:Print("Could not send the profile to that leader right now.")
    end
    return sent
end

function Comm:PublishUpgrades(report, target)
    if not IsInGuild() or not ET:CanUseLeaderTools() then return false end
    -- Persisted reports can be large. Schedule each recipient so bursts do not fill the message queue.
    if target then return self:QueueToLeaders("D", report.payload, target) end
    self.upgradeJobs = self.upgradeJobs or {}
    local sent = false
    for index = 1, GetNumGuildMembers() do
        local name, _, rank, _, _, _, _, _, online = GetGuildRosterInfo(index)
        local fullName = name and (name:find("-", 1, true) and name or name .. "-" .. GetNormalizedRealmName())
        if online and rank and rank <= ET.leaderRankCutoff and normalize(fullName) ~= normalize(ET.key)
            and self:HasAddonPeer(name) then
            if #self.upgradeJobs >= 1000 then break end
            self.upgradeJobs[#self.upgradeJobs + 1] = { report = report, target = name }
            sent = true
        end
    end
    return sent
end

function Comm:SyncUpgrades(target)
    if not ET:CanUseLeaderTools() or not ET:CanSenderUseLeaderTools(target) then return end
    self.upgradeJobs = self.upgradeJobs or {}
    self.upgradeSyncTimes = self.upgradeSyncTimes or {}
    if self.upgradeSyncTimes[target] and time() - self.upgradeSyncTimes[target] < 60 then return end
    self.upgradeSyncTimes[target] = time()
    for _, report in pairs(ET.db.upgradeReports or {}) do
        if ET.Upgrades:FindOwner(report) then
            self.upgradeJobs[#self.upgradeJobs + 1] = { report = report, target = target }
            if #self.upgradeJobs >= 1000 then break end
        end
    end
end

function Comm:FillUpgradesQueue()
    local jobs = self.upgradeJobs
    if not jobs or #jobs == 0 or #self.queue > 200 then return end
    local job = jobs[1]
    if not ET:CanUseLeaderTools() or not self:HasAddonPeer(job.target)
        or not ET:CanSenderUseLeaderTools(job.target) or not ET.Upgrades:FindOwner(job.report) then
        table.remove(jobs, 1)
    elseif self:PublishUpgrades(job.report, job.target) then table.remove(jobs, 1) end
end

function Comm:PublishSimC(target)
    if not IsInGuild() then return false end
    local entry = ET:GetCachedSimC(ET.key, UnitGUID("player"))
    if not entry then return false end
    local data = ET.Protocol:EncodeSimC(entry.guid, entry.updated, entry.profile)
    return self:QueueToLeaders("P", data, target)
end

function Comm:PublishBag(bagID, target)
    if not IsInGuild() then return false end
    local entry = ET.Inventory:Get(ET.key, UnitGUID("player"))
    local bag = entry and entry.containers[bagID]
    if not bag then return false end
    local data = ET.Protocol:EncodeBag(entry.guid, bagID, bag.updated, bag.items)
    return self:QueueToLeaders("I", data, target, bagID)
end

function Comm:PublishBags(target)
    local entry = ET.Inventory:Get(ET.key, UnitGUID("player"))
    if not entry then return end
    local bagIDs = {}
    for bagID in pairs(entry.containers) do bagIDs[#bagIDs + 1] = bagID end
    table.sort(bagIDs)
    for _, bagID in ipairs(bagIDs) do self:PublishBag(bagID, target) end
end

function Comm:PublishCrafting(professionID, target)
    if not IsInGuild() or (target and not self:IsGuildSender(target)) then return false end
    local entry = ET.Crafting:Get(ET.key, UnitGUID("player"))
    local profession = entry and entry.professions[professionID]
    if not profession or #profession.recipes == 0 then return false end
    local total = math.ceil(#profession.recipes / ET.Crafting.pageSize)
    if total > 40 then return false end
    if #self.craftingJobs >= 50 then return false end
    self.craftingJobs[#self.craftingJobs + 1] = {
        guid = entry.guid, professionID = professionID, profession = profession,
        target = target, page = 1, total = total,
    }
    return true
end

function Comm:FillCraftingQueue()
    if #self.queue > 600 or #self.craftingJobs == 0 then return end
    local job = self.craftingJobs[1]
    local recipes = {}
    for index = (job.page - 1) * ET.Crafting.pageSize + 1,
        math.min(job.page * ET.Crafting.pageSize, #job.profession.recipes) do
        recipes[#recipes + 1] = job.profession.recipes[index]
    end
    local data = ET.Protocol:EncodeCrafting(job.guid, job.professionID, job.profession.name,
        job.profession.updated, job.page, job.total, recipes)
    if self:QueueTransfer("C", data, job.target and "WHISPER" or "GUILD", job.target) then
        job.page = job.page + 1
        if job.page > job.total then table.remove(self.craftingJobs, 1) end
    end
end

function Comm:PublishAllCrafting(target)
    local entry = ET.Crafting:Get(ET.key, UnitGUID("player"))
    if not entry then return end
    local ids = {}
    for id in pairs(entry.professions) do ids[#ids + 1] = id end
    table.sort(ids)
    for _, id in ipairs(ids) do
        if not self:PublishCrafting(id, target) then break end
    end
end

function Comm:Hello()
    if IsInGuild() and ET:CanUseLeaderTools() and not self.helloSent then
        self.helloSent = true
        self:Queue("H|1", "GUILD")
    end
end

function Comm:InitialSync()
    if not IsInGuild() then return end
    self:Hello()
    if not self.craftingHelloSent then
        self.craftingHelloSent = self:Queue("H|C", "GUILD")
    end
    if not self.craftingPublishedOnLogin then
        self.craftingPublishedOnLogin = true
        self:PublishAllCrafting()
    end
    if not self.publishedOnLogin then
        self.publishedOnLogin = true
        self:Publish(ET:CaptureSelf())
    end
    if not self.simcScheduledOnLogin then
        self.simcScheduledOnLogin = true
        ET:ScheduleSimCPublish()
    end
    if not self.bagsPublishedOnLogin then
        self.bagsPublishedOnLogin = true
        self:PublishBags()
    end
end

function Comm:RequestSimC(sender, guid)
    if not sender or not guid or guid == "" then return false end
    if not ET:CanUseLeaderTools() then return false end
    if not self:IsGuildSender(sender) then return false end
    if not self:Queue("Q|" .. guid, "GUILD") then return false end
    self.pendingSimC[normalize(sender)] = { guid = guid, expires = time() + 900 }
    return true
end

function Comm:RequestGuildGear()
    if not IsInGuild() or not ET:CanUseLeaderTools() then return false, "Leader access is required." end
    if self.lastGearRequest and time() - self.lastGearRequest < 60 then
        return false, "Wait a minute before refreshing guild gear again."
    end
    if not self:Queue("R|1", "GUILD", nil, true) then return false, "The sharing queue is full." end
    self.lastGearRequest = time()
    return true
end

function Comm:ReplyWithCurrentGear(sender)
    if not ET:CanSenderUseLeaderTools(sender) then return end
    if InCombatLockdown and InCombatLockdown() then
        self.pendingGearReplies[sender] = true
        return
    end
    self:Publish(ET:CaptureSelf(), sender)
end

function Comm:ReceiveTransfer(sender, channel, message)
    local kind, id, indexText, totalText, chunk = message:match("^E|([SPICD])|([^|]+)|(%d+)|(%d+)|(.*)$")
    if not kind then return end
    if kind ~= "C" and not ET:CanUseLeaderTools() then return end
    if not self:IsGuildSender(sender) then return end
    if kind == "D" and (channel ~= "WHISPER" or not ET:CanSenderUseLeaderTools(sender)) then return end
    local index, total = tonumber(indexText), tonumber(totalText)
    if not index or not total or total < 1 or total > MAX_PARTS or index < 1 or index > total then return end
    if #chunk > CHUNK_SIZE then return end
    if kind == "S" or kind == "I" or kind == "C" then
        if channel ~= "GUILD" and channel ~= "WHISPER" then return end
        if channel == "WHISPER" and not self:IsGuildSender(sender) then return end
    elseif kind == "P" then
        if channel ~= "GUILD" and channel ~= "WHISPER" then return end
        if channel == "WHISPER" then
            if not self:IsGuildSender(sender) then return end
        end
    end
    local key = sender .. ":" .. kind .. ":" .. id
    local transfer = self.incoming[key]
    if not transfer then
        transfer = { parts = {}, count = 0, total = total, started = time() }
        self.incoming[key] = transfer
    end
    if transfer.total ~= total then self.incoming[key] = nil; return end
    if not transfer.parts[index] then
        transfer.parts[index] = chunk
        transfer.count = transfer.count + 1
    end
    if transfer.count ~= total then return end
    self.incoming[key] = nil
    local encoded = table.concat(transfer.parts)
    if #encoded > 120000 then return end
    local data = ET.Protocol:FromBase64(encoded)
    if not data then return end
    if kind == "D" then
        ET.Upgrades:Receive(sender, data)
    elseif kind == "C" then
        ET.Crafting:ReceivePage(sender, data)
    elseif kind == "S" then
        local snapshot = ET.Protocol:DecodeSnapshot(data)
        if not snapshot then return end
        local shortSender = Ambiguate(sender, "short")
        if normalize(snapshot.name) ~= normalize(shortSender) then return end
        local previous = ET.db.characters[sender]
        if not previous or snapshot.updated >= (previous.updated or 0) then
            ET.db.characters[sender] = snapshot
            ET.UI:Refresh()
        end
    elseif kind == "I" then
        local guid, bagID, updated, items = ET.Protocol:DecodeBag(data)
        if not guid then return end
        ET.Inventory:Store(sender, guid, bagID, updated, items)
    else
        local guid, updated, profile = ET.Protocol:DecodeSimC(data)
        if not guid then return end
        local snapshot = ET.db.characters[sender]
        if snapshot and snapshot.guid ~= guid then return end
        local pending = self.pendingSimC[normalize(sender)]
        if pending and pending.expires >= time() and pending.guid ~= guid then return end
        local wasRequested = pending and pending.guid == guid and pending.expires >= time()
        if wasRequested then self.pendingSimC[normalize(sender)] = nil end
        if ET:StoreSimC(sender, guid, updated, profile) then
            if wasRequested then ET:Print("Saved a SimC profile from " .. sender .. ".") end
        end
    end
end

function Comm:ShareWithLeader(sender)
    if not ET:CanSenderUseLeaderTools(sender) then return end
    self.leaderReplies = self.leaderReplies or {}
    local key = peerKey(sender)
    if self.leaderReplies[key] and time() - self.leaderReplies[key] < 60 then return end
    self.leaderReplies[key] = time()
    C_Timer.After(math.random(2, 6), function()
        if self:HasAddonPeer(sender) and ET:CanSenderUseLeaderTools(sender) then
            self:Publish(ET:CaptureSelf(), sender)
            self:PublishSimC(sender)
            self:PublishBags(sender)
            if ET:CanUseLeaderTools() then self:SyncUpgrades(sender) end
        end
    end)
end

function Comm:OnMessage(prefix, message, channel, sender)
    if prefix ~= self.prefix or type(message) ~= "string" or not sender
        or peerKey(sender) == peerKey(ET.key) or not self:IsGuildSender(sender) then return end
    if channel ~= "GUILD" and channel ~= "WHISPER" then return end
    local key = peerKey(sender)
    local discovered = not self.peers[key]
    self.peers[key] = true
    if discovered then self:ShareWithLeader(sender) end
    if message:sub(1, 2) == "E|" then
        self:ReceiveTransfer(sender, channel, message)
    elseif channel == "GUILD" and message == "H|C" and self:IsGuildSender(sender) then
        local requester = normalize(sender)
        if self.craftingReplies and self.craftingReplies[requester]
            and time() - self.craftingReplies[requester] < 60 then return end
        self.craftingReplies = self.craftingReplies or {}
        self.craftingReplies[requester] = time()
        -- Reply even with no saved recipes, so a newly logged-in member can discover us.
        self:Queue(ET:CanUseLeaderTools() and "H|1" or "H|C", "WHISPER", sender)
        C_Timer.After(math.random(2, 6), function()
            if self:IsGuildSender(sender) then self:PublishAllCrafting(sender) end
        end)
    elseif message == "H|1" then
        self:ShareWithLeader(sender)
    elseif channel == "GUILD" and message:sub(1, 2) == "Q|" then
        if not ET:CanSenderUseLeaderTools(sender) then return end
        if message:sub(3) ~= UnitGUID("player") then return end
        local profile, err = ET:GetSimCProfile()
        if profile then
            if #profile > 89000 then
                self:Queue("N|Profile is too large to share.", "WHISPER", sender)
                return
            end
            local guid, updated = UnitGUID("player"), time()
            ET:StoreSimC(ET.key, guid, updated, profile)
            local packed = ET.Protocol:EncodeSimC(guid, updated, profile)
            if not self:QueueTransfer("P", packed, "WHISPER", sender) then
                self:Queue("N|Profile is too large or the transfer queue is busy.", "WHISPER", sender)
            end
        else
            self:Queue("N|" .. err, "WHISPER", sender)
        end
    elseif channel == "GUILD" and message == "R|1" then
        if not ET:CanSenderUseLeaderTools(sender) then return end
        local requester = normalize(sender)
        if self.gearReplies[requester] and time() - self.gearReplies[requester] < 60 then return end
        self.gearReplies[requester] = time()
        C_Timer.After(math.random(1, 4), function() self:ReplyWithCurrentGear(sender) end)
    elseif channel == "WHISPER" and message:sub(1, 2) == "N|" then
        local pending = self.pendingSimC[normalize(sender)]
        if pending and pending.expires >= time() then
            self.pendingSimC[normalize(sender)] = nil
            ET:Print(sender .. ": " .. message:sub(3))
        end
    elseif channel == "GUILD" and message:sub(1, 2) == "X|" then
        local snapshot = ET.db.characters[sender]
        if snapshot and snapshot.guid == message:sub(3) then
            ET.db.characters[sender] = nil
            ET.db.simcProfiles[sender] = nil
            ET.db.inventory[sender] = nil
            ET.UI:Refresh()
        end
    elseif channel == "GUILD" and message:sub(1, 2) == "Y|" then
        local entry = ET.db.simcProfiles[sender]
        if entry and entry.guid == message:sub(3) then
            ET.db.simcProfiles[sender] = nil
            ET.UI:Refresh()
        end
    elseif channel == "GUILD" and message:sub(1, 2) == "Z|" then
        local entry = ET.db.inventory[sender]
        if entry and entry.guid == message:sub(3) then
            ET.db.inventory[sender] = nil
            ET.UI:Refresh()
        end
    end
end

function Comm:Initialize()
    C_ChatInfo.RegisterAddonMessagePrefix(self.prefix)
    if C_GuildInfo and C_GuildInfo.GuildRoster then C_GuildInfo.GuildRoster() end
    self.events = CreateFrame("Frame")
    self.events:RegisterEvent("CHAT_MSG_ADDON")
    self.events:RegisterEvent("GUILD_ROSTER_UPDATE")
    self.events:RegisterEvent("PLAYER_GUILD_UPDATE")
    self.events:RegisterEvent("PLAYER_REGEN_ENABLED")
    self.events:SetScript("OnEvent", function(_, event, ...)
        if event == "CHAT_MSG_ADDON" then self:OnMessage(...) end
        if event == "GUILD_ROSTER_UPDATE" or event == "PLAYER_GUILD_UPDATE" then
            self:RefreshPeers()
            ET:EnforceGuildAccess()
            if GetNumGuildMembers() > 0 then self:InitialSync() end
            if ET.UI then ET.UI:Refresh() end
        end
        if event == "PLAYER_REGEN_ENABLED" then
            local pending = self.pendingGearReplies
            self.pendingGearReplies = {}
            for sender in pairs(pending) do self:ReplyWithCurrentGear(sender) end
        end
    end)
    self.ticker = C_Timer.NewTicker(0.5, function()
        self:Pump()
        for key, transfer in pairs(self.incoming) do
            if time() - transfer.started > 900 then self.incoming[key] = nil end
        end
        for key, transfer in pairs(ET.Crafting.incoming) do
            if time() - transfer.started > 3600 then ET.Crafting.incoming[key] = nil end
        end
    end)
    C_Timer.After(5, function() self:InitialSync() end)
end
