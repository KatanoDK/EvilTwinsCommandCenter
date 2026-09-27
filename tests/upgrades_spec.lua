now = 1790490000
function time() return now end
function IsInGuild() return true end
function GetCurrentRegion() return 1 end
function GetNormalizedRealmName() return "TestRealm" end
function GetNumGuildMembers() return 2 end
function GetGuildRosterInfo(index) return index == 1 and "Testtoon-TestRealm" or "Leader-TestRealm", nil, index == 1 and 8 or 1 end
function UnitName() return "Leader" end
function GetRealmName() return "Test Realm" end
Enum = {}
local leader = true
ET = { key = "Leader-TestRealm", slots = { { 1, "Head" }, { 16, "Main hand" } }, db = { upgradeReports = {} } }
function ET:CanUseLeaderTools() return leader end
function ET:CanSenderUseLeaderTools(name) return name == "Leader-TestRealm" end
function ET:GetGuildRankIndex(name) return name == "Leader-TestRealm" and 1 or name == "Testtoon-TestRealm" and 8 or nil end
ET.UI = { Refresh = function() end }

function RunUpgradeChecks(code)
    local U, P = ET.Upgrades, ET.Protocol
    local raw = P:FromBase64(code:sub(6))
    local report = U:Decode(raw)
    assert(report and #report.items == 3, "C# import code must decode in Lua")
    assert(report.items[1].dropID == 268265 and math.abs(report.items[1].percent - 0.684969193) < 0.000001)
    assert(report.items[3].dropID == 268246 and report.items[3].simulatedID == 271535 and report.items[3].itemLevel == 298)
    assert(U:FindOwner(report) == "Testtoon-TestRealm", "Realm slug must match the normalized in-game realm")
    local previousComm = ET.Comm
    local published
    ET.Comm = { PublishUpgrades = function(_, entry) published = entry end }
    assert(U:Import(code) and published and published.owner == "Testtoon-TestRealm")
    assert(not U:Import(code), "Re-importing must not duplicate or downgrade a saved report")
    assert(#U:Get("Testtoon-TestRealm", 1480) == 1 and #U:Get("Testtoon-TestRealm", 252) == 0)
    local fields = P:Unpack(raw, 1500)
    fields[7] = tostring(now + 1000)
    assert(not U:Decode(P:Pack(fields)), "Future timestamps must be rejected")
    fields[7] = "1700000000"
    fields[17] = "999"
    assert(not U:Decode(P:Pack(fields)), "Unbounded item lists must be rejected")
    assert(not U:Import("ETD1:!!!!") and not U:Import("loadstring('anything')"))
    local fresh = U:Decode(raw)
    fresh.updated = now
    local link = "item:12345:8001:6:2:0:0:0:0:0:0:0:0:2:9:3"
    local snapshot = { specID = 1480, talent = "TEST_TALENTS", gear = { link, "item:54321:0:0:0:0:0:0:0:0:0:0:0:2:5:8" }, updated = now }
    assert(U:Freshness(fresh, snapshot) == "Matches saved gear and talents")
    snapshot.gear[1] = link:gsub("12345", "12346")
    assert(U:Freshness(fresh, snapshot):find("Gear", 1, true))
    snapshot.gear[1] = link; snapshot.talent = "CHANGED"
    assert(U:Freshness(fresh, snapshot):find("Talents changed", 1, true))
    snapshot.talent = "TEST_TALENTS"; snapshot.specID = 252
    assert(U:Freshness(fresh, snapshot):find("Different specialization", 1, true))
    snapshot.specID = 1480
    assert(U:Freshness(report, snapshot):find("week old", 1, true))
    assert(not U:Receive("Testtoon-TestRealm", raw), "Only a current leader can publish another character's report")
    leader = false
    assert(not U:Import(code) and not U:Receive("Leader-TestRealm", raw), "Lower ranks must not import or receive upgrade data")
    leader = true
    ET.Comm = previousComm
    ET.leaderRankCutoff = 5
    ET.Comm.queue = {}
    assert(ET.Comm:PublishUpgrades(report, "Leader-TestRealm"))
    local messages = ET.Comm.queue
    ET.db.upgradeReports = {}
    for _, message in ipairs(messages) do ET.Comm:ReceiveTransfer("Leader-TestRealm", "WHISPER", message.message) end
    assert(#U:Get("Testtoon-TestRealm", 1480) == 1, "Chunked transfer must save the report under its actual character")
    ET.db.upgradeReports = {}
    for _, message in ipairs(messages) do ET.Comm:ReceiveTransfer("Testtoon-TestRealm", "WHISPER", message.message) end
    assert(#U:Get("Testtoon-TestRealm", 1480) == 0, "A lower rank cannot bypass the leader-only transfer gate")
    for _, message in ipairs(messages) do ET.Comm:ReceiveTransfer("Leader-TestRealm", "GUILD", message.message) end
    assert(#U:Get("Testtoon-TestRealm", 1480) == 0, "Reports are only accepted over leader whispers")
    print("PASS: cross-language Droptimizer import, guild/region matching, bounds, freshness, duplicate reports, and leader-only chunked sharing.")
end

function RunTrashUpgradeChecks(code)
    local U, P = ET.Upgrades, ET.Protocol
    local raw = P:FromBase64(code:sub(6))
    local report = U:Decode(raw)
    assert(report and #report.items == 2, "Trash loot must not cause the entire raid report to be discarded")
    assert(report.items[1].bossID == 2895 and report.items[2].bossID == -97
        and report.items[2].boss == "Trash Drop" and report.items[2].dropID == 271436)
    ET.db.upgradeReports = {}
    assert(U:Import(code), "A leader must be able to import another character's report with trash loot")
    assert(#U:Get("Testtoon-TestRealm", 1480) == 1, "Imported upgrades must belong to the simulated character")
    ET.Comm.queue = {}
    assert(ET.Comm:PublishUpgrades(report, "Leader-TestRealm"))
    local messages = ET.Comm.queue
    ET.db.upgradeReports = {}
    for _, message in ipairs(messages) do ET.Comm:ReceiveTransfer("Leader-TestRealm", "WHISPER", message.message) end
    local saved = U:Get("Testtoon-TestRealm", 1480)[1]
    assert(saved and #saved.items == 2 and saved.items[2].bossID == -97, "Leader sharing must preserve trash loot")
    local fields = P:Unpack(raw, 1500)
    local bossIndex = 18 + tonumber(fields[16]) * 2 + 14 + 5
    for _, invalid in ipairs({ "-100001", "100001", "-97.5", "not-an-id" }) do
        fields[bossIndex] = invalid
        assert(not U:Decode(P:Pack(fields)), "Signed encounter IDs must still be bounded integers")
    end
    print("PASS: raid trash loot decodes, imports for another character, shares with leaders, and retains encounter ID validation.")
end
