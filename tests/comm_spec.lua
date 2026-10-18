now = 100000
function time() return now end
function IsInGuild() return true end
function UnitGUID() return "Player-self" end
function UnitName() return "Self" end
function GetNormalizedRealmName() return "Realm" end
function GetRealmName() return "Realm" end
function GetGuildInfo() return "Guild", "Member", 8 end
Enum = {}

local roster = {
    { name = "Self-Realm", rank = 8, online = true },
    { name = "Leader-Realm", rank = 1, online = true },
    { name = "OtherLeader-Realm", rank = 5, online = true },
    { name = "Member-Realm", rank = 8, online = true },
}
function GetNumGuildMembers() return #roster end
function GetGuildRosterInfo(index)
    local row = roster[index]
    return row.name, nil, row.rank, nil, nil, nil, nil, nil, row.online
end
local timers, delivered, warnings = {}, {}, {}
local locked, result = false, 0
C_Timer = { After = function(_, callback) timers[#timers + 1] = callback end }
C_ChatInfo = {
    InChatMessagingLockdown = function() return locked end,
    SendAddonMessage = function(_, message, channel, target)
        if result == 0 then delivered[#delivered + 1] = { message = message, channel = channel, target = target } end
        return result
    end,
}
ET = { key = "Self-Realm", slots = {}, db = { upgradeReports = {} } }
function ET:Print(message) warnings[#warnings + 1] = message end
local captures = 0
function ET:CaptureSelf()
    captures = captures + 1
    return { guid = "Player-self", name = "Self", realm = "Realm", class = "MAGE",
        specID = 62, spec = "Arcane", level = 90, itemLevel = 200, updated = now,
        talent = "", wishlist = {}, gear = {} }
end
function ET:GetCachedSimC() return { guid = "Player-self", updated = now, profile = "mage=Self" } end
function ET:ScheduleSimCPublish() end
ET.Inventory = { Get = function() return { guid = "Player-self", containers = {
    [0] = { updated = now, items = {} }, [1] = { updated = now, items = {} },
} } end }
ET.Crafting = { Get = function() end }
ET.Upgrades = { FindOwner = function() return "Member-Realm" end }

local function runTimers()
    local pending = timers
    timers = {}
    for _, callback in ipairs(pending) do callback() end
end

function RunCommChecks()
    local C = ET.Comm
    -- Online rank alone does not mean anyone else has the addon installed.
    for _ = 1, 100 do
        assert(not C:QueueToLeaders("S", string.rep("gear", 1000)))
        assert(not C:QueueToLeaders("P", string.rep("simc", 10000)))
        assert(not C:QueueToLeaders("I", string.rep("bag", 1000), nil, 0))
    end
    assert(not C:PublishUpgrades({ payload = "report" }))
    assert(#C.queue == 0 and #C.leaderJobs == 0 and #warnings == 0)
    C:InitialSync()
    C:InitialSync()
    assert(captures == 1, "roster updates must not recapture repeatedly when no peers are present")
    C.queue = {}

    -- A newcomer discovers an existing leader through the whispered hello reply.
    C:OnMessage(C.prefix, "H|1", "WHISPER", "Leader-Realm")
    C:OnMessage(C.prefix, "H|1", "GUILD", "Leader-Realm")
    C:OnMessage(C.prefix, "H|C", "GUILD", "Leader-Realm")
    assert(C:HasAddonPeer("leader") and not C:HasAddonPeer("OtherLeader-Realm"))
    runTimers()
    assert(captures == 2, "two hello types must not schedule duplicate leader syncs")
    assert(#C.leaderJobs == 4, "discovery must send gear, SimC, and both bags")
    for _, job in ipairs(C.leaderJobs) do assert(job.target == "Leader-Realm") end
    C:OnMessage("Unrelated", "H|1", "GUILD", "OtherLeader-Realm")
    C:OnMessage(C.prefix, "H|1", "GUILD", "Stranger-Realm")
    C:OnMessage(C.prefix, "H|1", "GUILD", "Self")
    assert(not C:HasAddonPeer("OtherLeader-Realm") and not C:HasAddonPeer("Stranger-Realm"))

    -- Every rank replies to discovery even when it has no profession data.
    C.queue = {}
    C:OnMessage(C.prefix, "H|C", "GUILD", "Member-Realm")
    assert(#C.queue == 1 and C.queue[1].message == "H|C" and C.queue[1].channel == "WHISPER")
    roster[1].rank = 1
    now = now + 61
    C:OnMessage(C.prefix, "H|C", "GUILD", "Member-Realm")
    assert(C.queue[2].message == "H|1", "leaders advertise leader availability to newcomers")
    roster[1].rank = 8
    runTimers()

    -- Repeated changes during a long messaging lockdown retain only the latest data.
    C.queue, C.leaderJobs = {}, {}
    locked = true
    for index = 1, 100 do
        assert(C:QueueToLeaders("P", "profile-" .. index))
        assert(C:QueueToLeaders("I", "bag-zero-" .. index, nil, 0))
        assert(C:QueueToLeaders("I", "bag-one-" .. index, nil, 1))
        assert(C:QueueToLeaders("S", "snapshot-" .. index))
        C:Pump()
    end
    assert(#C.leaderJobs <= 4 and #C.queue < 20 and #warnings == 0)
    locked = false
    while #C.queue > 0 or #C.leaderJobs > 0 do C:Pump() end
    local received = {}
    for _, entry in ipairs(delivered) do
        local encoded = entry.message:match("^E|[SPI]|[^|]+|1|1|(.*)$")
        if encoded then received[ET.Protocol:FromBase64(encoded)] = true end
        assert(entry.target == "Leader-Realm")
    end
    assert(received["profile-100"] and received["bag-zero-100"]
        and received["bag-one-100"] and received["snapshot-100"])

    -- A new update must not interrupt a partially delivered multipart profile.
    delivered = {}
    local original = string.rep("original", 4000)
    assert(C:QueueToLeaders("P", original))
    C:Pump()
    assert(#delivered == 1 and #C.queue > 100)
    assert(C:QueueToLeaders("P", "superseded"))
    assert(C:QueueToLeaders("P", "latest"))
    result = 3 -- Blizzard throttling keeps the same chunk pending.
    local queued = #C.queue
    C:Pump()
    assert(#C.queue == queued and #C.leaderJobs == 1)
    result = 0
    while #C.queue > 0 or #C.leaderJobs > 0 do C:Pump() end
    local transfers, order = {}, {}
    for _, entry in ipairs(delivered) do
        local id, index, total, chunk = entry.message:match("^E|P|([^|]+)|(%d+)|(%d+)|(.*)$")
        assert(id)
        if not transfers[id] then transfers[id] = { parts = {}, total = tonumber(total) }; order[#order + 1] = id end
        transfers[id].parts[tonumber(index)] = chunk
    end
    assert(#order == 2, "only the active profile and latest waiting profile should be sent")
    for _, id in ipairs(order) do assert(#transfers[id].parts == transfers[id].total) end
    assert(ET.Protocol:FromBase64(table.concat(transfers[order[1]].parts)) == original)
    assert(ET.Protocol:FromBase64(table.concat(transfers[order[2]].parts)) == "latest")

    -- Large payloads for many addon leaders are expanded into chunks only as space frees up.
    for index = 1, 8 do
        local name = "Extra" .. index .. "-Realm"
        roster[#roster + 1] = { name = name, rank = 1, online = true }
        C.peers[name:lower()] = true
    end
    delivered = {}
    local large = string.rep("x", 89000)
    assert(C:QueueToLeaders("P", large))
    local pumps = 0
    while #C.queue > 0 or #C.leaderJobs > 0 do
        C:Pump()
        assert(#C.queue < 1400 and #warnings == 0)
        pumps = pumps + 1
        assert(pumps < 10000, "all recipients must eventually drain")
    end
    assert(#delivered > 5000)

    -- Offline and demoted recipients lose their queued background updates.
    assert(C:QueueToLeaders("P", large, "Leader-Realm"))
    C:Pump()
    assert(C:QueueToLeaders("P", "newest", "Leader-Realm"))
    roster[2].online = false
    C:RefreshPeers()
    assert(not C:HasAddonPeer("Leader"))
    delivered = {}
    while #C.queue > 0 or #C.leaderJobs > 0 do C:Pump() end
    assert(#delivered == 0)
    C.peers["otherleader-realm"] = true
    assert(C:QueueToLeaders("P", large, "OtherLeader-Realm"))
    C:Pump()
    roster[3].rank = 8
    delivered = {}
    while #C.queue > 0 or #C.leaderJobs > 0 do C:Pump() end
    assert(#delivered == 0)
    print("PASS: solo queue, addon discovery, latest updates, complete multipart delivery, throttling, fan-out, offline peers, and rank changes.")
end
