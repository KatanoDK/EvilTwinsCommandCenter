ET = { slots = {}, db = { prepChecklist = { flask = { minimum = 99, ids = { 123 } } } } }
for index = 1, 19 do ET.slots[index] = { index, "Slot " .. index } end
ET.Inventory = { BagCount = function() return 2 end }
function time() return 100000 end
function date() return "Today" end
function GetItemInfo(id) return id == 777 and "Test Ruby" or nil end
Enum = { TooltipDataLineType = { ItemEnchantmentPermanent = 15 } }
local sockets = { [100] = 2, [200] = 0, [300] = 1, [400] = 0 }
C_Item = {
    GetItemNumSockets = function(link) return sockets[tonumber(link:match("item:(%d+)"))] end,
    GetItemGemID = function(link, index) return link:find("item:100:", 1, true) and index == 1 and 777 or nil end,
    GetItemInfoInstant = function(link) return 1, nil, nil, link:find("item:400:", 1, true) and "INVTYPE_SHIELD" or "INVTYPE_WEAPON" end,
}
C_TooltipInfo = { GetHyperlink = function() return { lines = { { type = 15, leftText = "Enchanted: Test enchant" } } } end }

function RunPrepChecks()
    local Prep = ET.RaidPrep
    Prep:InitDB()
    assert(ET.db.prepChecklist == nil, "Manual policies must no longer affect automatic checks")
    local snapshot = { name = "Self", realm = "Realm", updated = time(), gear = {}, durability = {} }
    for index = 1, 19 do snapshot.gear[index] = "" end
    snapshot.gear[1] = "item:100:1234:777:0"
    snapshot.gear[2] = "item:300:0:0"
    snapshot.gear[5] = "item:200:0"
    snapshot.gear[17] = "item:400:0"
    local rows = Prep:GearDetails(snapshot)
    assert(rows[1].enchantID == 1234 and rows[1].enchantText == "Enchanted: Test enchant")
    assert(rows[1].gemText:find("Test Ruby", 1, true) and rows[1].gemText:find("Empty socket", 1, true))
    assert(rows[1].gemStatus == "missing" and rows[1].enchantStatus == "ok")
    assert(rows[2].index == 2 and rows[2].enchantStatus == "none" and rows[2].gemStatus == "missing")
    for _, row in ipairs(rows) do assert(row.index ~= 17, "A shield with no sockets must not require a weapon enchant") end
    snapshot.gear[17] = "item:200:0"
    rows = Prep:GearDetails(snapshot)
    local offhand
    for _, row in ipairs(rows) do if row.index == 17 then offhand = row end end
    assert(offhand and offhand.enchantStatus == "missing")
    snapshot.gear[1] = "item:999:1234"
    C_TooltipInfo.GetHyperlink = function() error("Item data not ready") end
    rows = Prep:GearDetails(snapshot)
    assert(rows[1].gemStatus == "unknown" and rows[1].enchantText == "Enchant #1234", "Missing cache data must not look like an empty socket")
    snapshot.gear[1] = "item:200:"
    assert(Prep:GearDetails(snapshot)[1].enchantStatus == "missing", "An empty enchant field means no enchant")
    local requests = 0
    C_Item.IsItemDataCachedByID = function() return false end
    C_Item.RequestLoadItemDataByID = function() requests = requests + 1 end
    Prep:GearDetails(snapshot)
    rows = Prep:GearDetails(snapshot)
    assert(rows[1].gemStatus == "unknown" and requests == 2, "Uncached gear must remain unknown and be requested only once per item")
    C_Item.IsItemDataCachedByID = nil
    C_Item.GetItemInfoInstant = function() return nil end
    rows = Prep:GearDetails(snapshot)
    for _, row in ipairs(rows) do if row.index == 17 then offhand = row end end
    assert(offhand.enchantStatus == "unknown", "Unknown off-hand type must not silently pass enchant checks")
    C_Item.GetItemInfoInstant = function() return 1, nil, nil, "INVTYPE_WEAPON" end
    local inventory = { containers = {
        [0] = { updated = time(), items = {
            { id = 241320, count = 1, link = "[Midnight Flask]" },
            { id = 241309, count = 1, link = "[Light Potion]" },
            { id = 271884, count = 1, link = "[Season 2 Health Potion]" },
        } },
        [1] = { updated = time(), items = {
            { id = 275269, count = 1, link = "[Season 2 Feast]" },
            { id = 259085, count = 1, link = "[Midnight Rune]" },
            { id = 237367, count = 1, link = "[Midnight Weightstone]" },
        } },
    } }
    local result = Prep:Analyze(snapshot, inventory)
    assert(result.configuredSupplies == 6 and #result.supplyIssues == 0)
    for _, supply in ipairs(result.supplies) do
        assert(supply.count == 1 and supply.minimum == 1 and supply.status == "ok" and #supply.items == 1)
    end
    inventory.containers[0].items[1].id = 212269 -- Older expansion flask.
    result = Prep:Analyze(snapshot, inventory)
    assert(#result.supplyIssues == 1 and result.supplies[1].status == "missing")
    inventory.containers[0].items[1].id = 245928 -- Cauldron flask, either quality accepted.
    assert(#Prep:Analyze(snapshot, inventory).supplyIssues == 0)
    inventory.containers[1] = nil
    result = Prep:Analyze(snapshot, inventory)
    assert(result.supplyUnknown and #result.supplyIssues == 0, "Incomplete bags must not create missing-supply claims")
    inventory.containers[1] = { updated = 1, items = {} }
    result = Prep:Analyze(snapshot, inventory)
    assert(result.bagStale and result.supplies[1].status == "stale")
    result = Prep:Analyze(snapshot, nil)
    assert(result.supplyUnknown and result.supplies[1].status == "unknown")
end
