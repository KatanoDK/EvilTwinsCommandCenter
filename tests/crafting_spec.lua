local P, C, Comm = ET.Protocol, ET.Crafting, ET.Comm
local learned = { { id = 100, name = "Recipe: | test", learned = true } }
local missing = { { id = 101, name = "Unknown recipe", learned = false } }
local function page(recipes, index, total, updated)
    return P:EncodeCrafting("Player-guildie", 171, "Alchemy", updated or now, index or 1, total or 1, recipes)
end

local guid, professionID, name, updated, index, total, recipes = P:DecodeCrafting(page(learned))
assert(guid == "Player-guildie" and professionID == 171 and name == "Alchemy")
assert(recipes[1].learned and recipes[1].name == learned[1].name)
assert(not P:DecodeCrafting(P:Pack({ "1", guid, 171, "Alchemy", now, 0, 1, 1, 100, 1, "Recipe" })))
assert(not P:DecodeCrafting(P:Pack({ "1", guid, 171, "Alchemy", now, 1, 1, 1, 100, 2, "Recipe" })))

-- A complete snapshot replaces the profession only after every page arrives.
C:ReceivePage("Guildie-Realm", page(missing, 2, 2))
assert(not C:Get("Guildie-Realm"))
C:ReceivePage("Guildie-Realm", page(learned, 1, 2))
local stored = C:Get("Guildie-Realm").professions[171]
assert(#stored.recipes == 2 and stored.recipes[1].learned and not stored.recipes[2].learned)
C:ReceivePage("Guildie-Realm", page(missing, 1, 1, now - 100))
assert(#C:Get("Guildie-Realm").professions[171].recipes == 2)
-- Duplicate IDs spanning pages are rejected without replacing the saved scan.
C:ReceivePage("Guildie-Realm", page(learned, 1, 2, now + 1))
C:ReceivePage("Guildie-Realm", page(learned, 2, 2, now + 1))
assert(C:Get("Guildie-Realm").professions[171].updated == now)

-- All guild ranks can receive crafts; gear still requires leader access.
Comm:QueueTransfer("C", page(learned), "WHISPER", "Self-Realm")
local messages = Comm.queue
Comm.queue = {}
for _, entry in ipairs(messages) do Comm:ReceiveTransfer("Other-Realm", "WHISPER", entry.message) end
assert(C:Get("Other-Realm"))
for _, entry in ipairs(messages) do Comm:ReceiveTransfer("Stranger-Realm", "WHISPER", entry.message) end
assert(not C:Get("Stranger-Realm"))
for _, entry in ipairs(messages) do
    Comm:ReceiveTransfer("Blocked-Realm", "WHISPER", entry.message:gsub("^E|C|", "E|S|"))
end
assert(not ET.db.characters["Blocked-Realm"])
assert(next(Comm.incoming) == nil)

-- Scans capture both statuses without changing profession filters.
C_TradeSkillUI = {
    GetBaseProfessionInfo = function() return { professionID = 171, professionName = "Alchemy" } end,
    GetAllRecipeIDs = function() return { 100, 101 } end,
    GetRecipeInfo = function(id) return { name = id == 100 and "Known" or "Missing", learned = id == 100 } end,
}
C:Scan()
assert(#C:Get(ET.key).professions[171].recipes == 2)
assert(#Comm.craftingJobs == 1)
C:Scan()
assert(#Comm.craftingJobs == 1, "unchanged scans must not republish")
C_TradeSkillUI.IsTradeSkillLinked = function() return true end
C_TradeSkillUI.GetBaseProfessionInfo = function() return { professionID = 164, professionName = "Blacksmithing" } end
C:Scan()
assert(not C:Get(ET.key).professions[164], "linked professions must not be attributed to the player")

-- Large catalogs fill the paced queue incrementally, then reassemble fully.
local large = {}
for i = 1, 4000 do large[i] = { id = 10000 + i, name = "Recipe " .. i, learned = i % 2 == 0 } end
C:Store(ET.key, "Player-self", 164, "Blacksmithing", now, large)
Comm.queue, Comm.craftingJobs = {}, {}
assert(Comm:PublishCrafting(164))
local delivered = 0
while #Comm.craftingJobs > 0 or #Comm.queue > 0 do
    Comm:FillCraftingQueue()
    assert(#Comm.queue < 1400)
    local entry = table.remove(Comm.queue, 1)
    if entry then
        assert(#entry.message <= 255 and entry.channel == "GUILD")
        Comm:ReceiveTransfer("Large-Realm", entry.channel, entry.message)
        delivered = delivered + 1
    end
end
assert(delivered > 40)
assert(#C:Get("Large-Realm").professions[164].recipes == 4000)
