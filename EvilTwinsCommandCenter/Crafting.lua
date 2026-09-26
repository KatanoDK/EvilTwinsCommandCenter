local _, ET = ...
local Crafting = { incoming = {} }
ET.Crafting = Crafting

local PAGE_SIZE = 100
local MAX_RECIPES = 4000

function Crafting:Get(key, guid)
    local entry = ET.db.crafting[key]
    if type(entry) == "table" and (not guid or entry.guid == guid)
        and type(entry.professions) == "table" then return entry end
end

function Crafting:Store(key, guid, professionID, name, updated, recipes)
    if type(key) ~= "string" or type(guid) ~= "string" or #guid == 0 or #guid > 64
        or type(professionID) ~= "number" or professionID < 1
        or type(name) ~= "string" or #name == 0 or #name > 100
        or type(updated) ~= "number" or updated < 1 or updated > time() + 300
        or type(recipes) ~= "table" or #recipes > MAX_RECIPES then return false end
    local entry = self:Get(key, guid)
    if not entry then
        entry = { guid = guid, professions = {} }
        ET.db.crafting[key] = entry
    end
    local previous = entry.professions[professionID]
    if previous and previous.updated > updated then return false end
    entry.professions[professionID] = { name = name, updated = updated, recipes = recipes }
    if ET.UI then ET.UI:RefreshCrafting() end
    return true
end

local function sameRecipes(a, b)
    if not a or #a ~= #b then return false end
    for index, recipe in ipairs(a) do
        local other = b[index]
        if recipe.id ~= other.id or recipe.name ~= other.name
            or recipe.learned ~= other.learned then return false end
    end
    return true
end

function Crafting:Scan()
    if not ET.db or not C_TradeSkillUI or not C_TradeSkillUI.GetAllRecipeIDs
        or not C_TradeSkillUI.GetBaseProfessionInfo then return end
    -- Linked, guild, and NPC profession windows do not describe this character.
    if (C_TradeSkillUI.IsTradeSkillLinked and C_TradeSkillUI.IsTradeSkillLinked())
        or (C_TradeSkillUI.IsTradeSkillGuild and C_TradeSkillUI.IsTradeSkillGuild())
        or (C_TradeSkillUI.IsNPCCrafting and C_TradeSkillUI.IsNPCCrafting()) then return end
    local base = C_TradeSkillUI.GetBaseProfessionInfo()
    if not base or not base.professionID or not base.professionName then return end
    local ids = C_TradeSkillUI.GetAllRecipeIDs()
    if type(ids) ~= "table" or #ids == 0 or #ids > MAX_RECIPES then return end
    local recipes = {}
    for _, id in ipairs(ids) do
        local info = C_TradeSkillUI.GetRecipeInfo(id)
        if info and type(info.name) == "string" and #info.name > 0 then
            recipes[#recipes + 1] = {
                id = id, name = info.name:sub(1, 100), learned = info.learned == true,
            }
        end
    end
    if #recipes == 0 then return end
    table.sort(recipes, function(a, b) return a.id < b.id end)
    local guid = UnitGUID("player")
    local entry = self:Get(ET.key, guid)
    local old = entry and entry.professions[base.professionID]
    if old and old.name == base.professionName and sameRecipes(old.recipes, recipes) then return end
    if self:Store(ET.key, guid, base.professionID, base.professionName, time(), recipes) then
        ET.Comm:PublishCrafting(base.professionID)
    end
end

function Crafting:ScheduleScan()
    if self.timer then self.timer:Cancel() end
    self.timer = C_Timer.NewTimer(1, function()
        self.timer = nil
        self:Scan()
    end)
end

function Crafting:ReceivePage(sender, data)
    local guid, professionID, name, updated, page, total, recipes = ET.Protocol:DecodeCrafting(data)
    if not guid then return end
    local key = sender .. ":" .. guid .. ":" .. professionID .. ":" .. updated
    local transfer = self.incoming[key]
    if not transfer then
        transfer = { guid = guid, professionID = professionID, name = name, updated = updated,
            total = total, pages = {}, count = 0, started = time() }
        self.incoming[key] = transfer
    end
    if transfer.total ~= total or transfer.name ~= name then self.incoming[key] = nil; return end
    if transfer.pages[page] then return end
    transfer.pages[page] = recipes
    transfer.count = transfer.count + 1
    if transfer.count ~= total then return end
    self.incoming[key] = nil
    local combined, seen = {}, {}
    for index = 1, total do
        for _, recipe in ipairs(transfer.pages[index]) do
            if seen[recipe.id] then return end
            seen[recipe.id] = true
            combined[#combined + 1] = recipe
            if #combined > MAX_RECIPES then return end
        end
    end
    self:Store(sender, guid, professionID, name, updated, combined)
end

function Crafting:Initialize()
    self.events = CreateFrame("Frame")
    self.events:RegisterEvent("TRADE_SKILL_SHOW")
    self.events:RegisterEvent("TRADE_SKILL_LIST_UPDATE")
    self.events:SetScript("OnEvent", function() self:ScheduleScan() end)
end

Crafting.pageSize = PAGE_SIZE
