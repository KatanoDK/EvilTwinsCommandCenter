local _, ET = ...
local Comm = { prefix = "EvilTwinsCC", queue = {}, incoming = {}, pendingSimC = {} }
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

function Comm:IsGuildSender(sender)
    if not IsInGuild() then return false end
    local wanted = normalize(sender)
    for index = 1, GetNumGuildMembers() do
        local name = GetGuildRosterInfo(index)
        if name then
            local rosterName = normalize(name)
            if rosterName == wanted then return true end
            -- The roster omits the realm for guildmates on the player's realm.
            if not rosterName:find("-", 1, true) and wanted == rosterName .. "-" .. normalize(GetNormalizedRealmName()) then
                return true
            end
        end
    end
    return false
end

function Comm:Queue(message, channel, target)
    if #self.queue >= MAX_QUEUE then return false end
    self.queue[#self.queue + 1] = { message = message, channel = channel, target = target }
    return true
end

function Comm:QueueTransfer(kind, text, channel, target)
    if type(text) ~= "string" or #text > 90000 then return false end
    text = ET.Protocol:ToBase64(text)
    local total = math.max(1, math.ceil(#text / CHUNK_SIZE))
    if total > MAX_PARTS or #self.queue + total > MAX_QUEUE then return false end
    self.nextID = (self.nextID or 0) + 1
    local id = tostring(time()) .. "-" .. tostring(self.nextID)
    for index = 1, total do
        local chunk = text:sub((index - 1) * CHUNK_SIZE + 1, index * CHUNK_SIZE)
        self:Queue("E|" .. kind .. "|" .. id .. "|" .. index .. "|" .. total .. "|" .. chunk, channel, target)
    end
    return true
end

function Comm:Pump()
    if #self.queue == 0 then return end
    if C_ChatInfo.InChatMessagingLockdown and C_ChatInfo.InChatMessagingLockdown() then return end
    local entry = self.queue[1]
    local result = C_ChatInfo.SendAddonMessage(self.prefix, entry.message, entry.channel, entry.target)
    if result == 0 or result == true then
        table.remove(self.queue, 1)
    elseif result ~= LOCKDOWN and result ~= THROTTLE and result ~= CHANNEL_THROTTLE then
        table.remove(self.queue, 1)
    end
end

function Comm:Publish(snapshot, target)
    if not IsInGuild() then return end
    local data = ET.Protocol:EncodeSnapshot(snapshot)
    local channel = target and "WHISPER" or "GUILD"
    if not self:QueueTransfer("S", data, channel, target) then
        ET:Print("The sharing queue is full; try publishing again shortly.")
    end
end

function Comm:PublishSimC(target)
    if not IsInGuild() or not ET.db.settings.shareProfile or not ET.db.settings.shareSimC then return false end
    local entry = ET:GetCachedSimC(ET.key, UnitGUID("player"))
    if not entry then return false end
    local data = ET.Protocol:EncodeSimC(entry.guid, entry.updated, entry.profile)
    if not target then self:CancelTransfers("P", "GUILD") end
    return self:QueueTransfer("P", data, target and "WHISPER" or "GUILD", target)
end

function Comm:Hello()
    if IsInGuild() and not self.helloSent then
        self.helloSent = true
        self:Queue("H|1", "GUILD")
    end
end

function Comm:InitialSync()
    if not IsInGuild() then return end
    self:Hello()
    if ET.db.settings.shareProfile and not self.publishedOnLogin then
        self.publishedOnLogin = true
        self:Publish(ET:CaptureSelf())
    end
    if ET.db.settings.shareSimC and not self.simcScheduledOnLogin then
        self.simcScheduledOnLogin = true
        ET:ScheduleSimCPublish()
    end
end

function Comm:RequestSimC(sender, guid)
    if not sender or not guid or guid == "" then return false end
    if not self:IsGuildSender(sender) then return false end
    if not self:Queue("Q|" .. guid, "GUILD") then return false end
    self.pendingSimC[normalize(sender)] = { guid = guid, expires = time() + 900 }
    return true
end

function Comm:RevokeSimC()
    self:CancelTransfers("P")
    if IsInGuild() then self:Queue("Y|" .. (UnitGUID("player") or ""), "GUILD") end
end

function Comm:Revoke()
    self:CancelTransfers()
    if IsInGuild() then self:Queue("X|" .. (UnitGUID("player") or ""), "GUILD") end
end

function Comm:CancelTransfers(kind, channel)
    for index = #self.queue, 1, -1 do
        local entry = self.queue[index]
        local message = entry.message
        if message:sub(1, 2) == "E|" and (not kind or message:sub(3, 3) == kind)
            and (not channel or entry.channel == channel) then
            table.remove(self.queue, index)
        end
    end
end

function Comm:ReceiveTransfer(sender, channel, message)
    local kind, id, indexText, totalText, chunk = message:match("^E|([SP])|([^|]+)|(%d+)|(%d+)|(.*)$")
    if not kind then return end
    local index, total = tonumber(indexText), tonumber(totalText)
    if not index or not total or total < 1 or total > MAX_PARTS or index < 1 or index > total then return end
    if #chunk > CHUNK_SIZE then return end
    if kind == "S" then
        if channel ~= "GUILD" and channel ~= "WHISPER" then return end
        if channel == "WHISPER" and not self:IsGuildSender(sender) then return end
    else
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
    if kind == "S" then
        local snapshot = ET.Protocol:DecodeSnapshot(data)
        if not snapshot then return end
        local shortSender = Ambiguate(sender, "short")
        if normalize(snapshot.name) ~= normalize(shortSender) then return end
        local previous = ET.db.characters[sender]
        if not previous or snapshot.updated >= (previous.updated or 0) then
            ET.db.characters[sender] = snapshot
            ET.UI:Refresh()
        end
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

function Comm:OnMessage(prefix, message, channel, sender)
    if prefix ~= self.prefix or sender == ET.key or sender == UnitName("player") or type(message) ~= "string" then return end
    if message:sub(1, 2) == "E|" then
        self:ReceiveTransfer(sender, channel, message)
    elseif channel == "GUILD" and message == "H|1" and ET.db.settings.shareProfile then
        C_Timer.After(math.random(2, 6), function()
            if ET.db.settings.shareProfile then
                self:Publish(ET:CaptureSelf(), sender)
                self:PublishSimC(sender)
            end
        end)
    elseif channel == "GUILD" and message:sub(1, 2) == "Q|" then
        if not ET.db.settings.shareProfile or not ET.db.settings.shareSimC then return end
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
            ET.UI:Refresh()
        end
    elseif channel == "GUILD" and message:sub(1, 2) == "Y|" then
        local entry = ET.db.simcProfiles[sender]
        if entry and entry.guid == message:sub(3) then
            ET.db.simcProfiles[sender] = nil
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
    self.events:SetScript("OnEvent", function(_, event, ...)
        if event == "CHAT_MSG_ADDON" then self:OnMessage(...) end
        if event == "GUILD_ROSTER_UPDATE" and GetNumGuildMembers() > 0 then self:InitialSync() end
    end)
    self.ticker = C_Timer.NewTicker(0.5, function()
        self:Pump()
        for key, transfer in pairs(self.incoming) do
            if time() - transfer.started > 900 then self.incoming[key] = nil end
        end
    end)
    C_Timer.After(5, function() self:InitialSync() end)
end
