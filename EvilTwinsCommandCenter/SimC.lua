local _, ET = ...
local SimC = {}
ET.SimC = SimC

-- Equipped-item option names follow the SimulationCraft collector's public format.
-- Reference: https://github.com/simulationcraft/simc-addon (Unlicense).
local slots = {
    { 1, "head" }, { 2, "neck" }, { 3, "shoulder" }, { 15, "back" },
    { 5, "chest" }, { 4, "shirt" }, { 19, "tabard" }, { 9, "wrist" },
    { 10, "hands" }, { 6, "waist" }, { 7, "legs" }, { 8, "feet" },
    { 11, "finger1" }, { 12, "finger2" }, { 13, "trinket1" }, { 14, "trinket2" },
    { 16, "main_hand" }, { 17, "off_hand" },
}
local specs = {
    [250] = "blood", [251] = "frost", [252] = "unholy",
    [577] = "havoc", [581] = "vengeance", [1480] = "devourer",
    [102] = "balance", [103] = "feral", [104] = "guardian", [105] = "restoration",
    [1467] = "devastation", [1468] = "preservation", [1473] = "augmentation",
    [253] = "beast_mastery", [254] = "marksmanship", [255] = "survival",
    [62] = "arcane", [63] = "fire", [64] = "frost",
    [268] = "brewmaster", [269] = "windwalker", [270] = "mistweaver",
    [65] = "holy", [66] = "protection", [70] = "retribution",
    [256] = "discipline", [257] = "holy", [258] = "shadow",
    [259] = "assassination", [260] = "outlaw", [261] = "subtlety",
    [262] = "elemental", [263] = "enhancement", [264] = "restoration",
    [265] = "affliction", [266] = "demonology", [267] = "destruction",
    [71] = "arms", [72] = "fury", [73] = "protection",
}
local casterSpecs = { [102] = true, [1480] = true, [1467] = true, [1473] = true,
    [62] = true, [63] = true, [64] = true, [256] = true, [258] = true,
    [262] = true, [265] = true, [266] = true, [267] = true }
local professions = { [164] = "blacksmithing", [165] = "leatherworking", [171] = "alchemy",
    [182] = "herbalism", [186] = "mining", [197] = "tailoring", [202] = "engineering",
    [333] = "enchanting", [393] = "skinning", [755] = "jewelcrafting", [773] = "inscription" }
local regions = { [1] = "us", [2] = "kr", [3] = "eu", [4] = "tw", [5] = "cn", [72] = "tr" }
local loaBuffs = { [292359] = "akunda", [292360] = "bwonsamdi", [292362] = "gonk",
    [292363] = "kimbul", [292364] = "kragwa", [292361] = "paku" }
local legacyBelts = { [242664] = true, [245964] = true, [245965] = true, [245966] = true }

local function token(value)
    -- Preserve UTF-8 realm names while removing SimC punctuation and whitespace.
    return (value or ""):lower():gsub("%s+", "_"):gsub("[%z\1-\31\127\"'`:,/\\-]", ""):gsub("_+$", "")
end

function SimC:InstalledCollector()
    local instance = _G.Simulationcraft
    local stub = _G.LibStub
    if type(instance) ~= "table" and stub then
        local ok, ace
        if type(stub) == "table" and type(stub.GetLibrary) == "function" then
            ok, ace = pcall(stub.GetLibrary, stub, "AceAddon-3.0", true)
        elseif type(stub) == "function" then
            ok, ace = pcall(stub, "AceAddon-3.0", true)
        end
        if ok and ace and type(ace.GetAddon) == "function" then
            local found, addon = pcall(ace.GetAddon, ace, "Simulationcraft", true)
            if found then instance = addon end
        end
    end
    if type(instance) ~= "table" then return end
    local api = _G.SimulationcraftAPI
    local method = type(api) == "table" and api.GetSimcProfile or instance.GetSimcProfile
    if type(method) == "function" then return instance, method end
end

function SimC:Source()
    return self:InstalledCollector() and "SimulationCraft (equipped gear)" or "built-in (equipped gear)"
end

function SimC:ItemLine(slot, link)
    local payload = type(link) == "string" and link:match("item:([%d:%-]+)")
    if not payload then return nil, "An equipped item link is not ready yet. Try again in a moment." end
    local fields = setmetatable({}, { __index = function() return 0 end })
    for field in (payload .. ":"):gmatch("(.-):") do
        local value = field == "" and 0 or tonumber(field)
        if not value or value % 1 ~= 0 then return nil, "An equipped item link is invalid." end
        fields[#fields + 1] = value
    end
    if fields[1] < 1 then return nil, "An equipped item has no item ID yet." end
    if legacyBelts[fields[1]] then
        return nil, "This legacy DISC belt needs SimulationCraft's special-effect export. Enable SimulationCraft for this character."
    end
    local options = { ",id=" .. fields[1] }
    if fields[2] > 0 then options[#options + 1] = "enchant_id=" .. fields[2] end
    local gems = { fields[3], fields[4], fields[5], fields[6] }
    while #gems > 0 and gems[#gems] == 0 do table.remove(gems) end
    if #gems > 0 then options[#options + 1] = "gem_id=" .. table.concat(gems, "/") end
    local bonuses, count = {}, fields[13]
    if count < 0 or count > 100 then return nil, "An equipped item's bonus list is invalid." end
    for index = 1, count do
        if fields[13 + index] <= 0 then return nil, "An equipped item's bonus data is incomplete." end
        bonuses[index] = fields[13 + index]
    end
    if #bonuses > 0 then options[#options + 1] = "bonus_id=" .. table.concat(bonuses, "/") end
    local offset = 14 + count
    local pairsCount = fields[offset]
    if pairsCount < 0 or pairsCount > 100 then return nil, "An equipped item's modifier list is invalid." end
    local crafted = {}
    for index = 1, pairsCount do
        local position = offset + 1 + (index - 1) * 2
        local kind, value = fields[position], fields[position + 1]
        if kind <= 0 or value < 0 then return nil, "An equipped item's modifiers are incomplete." end
        if kind == 9 then options[#options + 1] = "drop_level=" .. value
        elseif kind == 28 then options[#options + 1] = "content_tuning=" .. value
        elseif kind == 29 or kind == 30 then crafted[#crafted + 1] = value
        elseif kind == 64 then options[#options + 1] = "redirected_base_stats=" .. value end
    end
    if #crafted > 0 then options[#options + 1] = "crafted_stats=" .. table.concat(crafted, "/") end
    local gemOffset = offset + pairsCount * 2 + 2
    local gemCount, gemBonuses = fields[gemOffset], {}
    if gemCount < 0 or gemCount > 100 then return nil, "An equipped item's gem bonus list is invalid." end
    for index = 1, gemCount do gemBonuses[index] = fields[gemOffset + index] end
    if #gemBonuses > 0 then options[#options + 1] = "gem_bonus_id=" .. table.concat(gemBonuses, "/") end
    if C_TradeSkillUI and C_TradeSkillUI.GetItemCraftedQualityByItemInfo then
        local quality = C_TradeSkillUI.GetItemCraftedQualityByItemInfo(link)
        if quality then options[#options + 1] = "crafting_quality=" .. quality end
    end
    return slot .. "=" .. table.concat(options, ",")
end

function SimC:NativeProfile()
    local specAPI = C_SpecializationInfo or {}
    local getSpec = specAPI.GetSpecialization or GetSpecialization
    local getInfo = specAPI.GetSpecializationInfo or GetSpecializationInfo
    local specIndex = getSpec and getSpec()
    local specID, _, _, _, role
    if specIndex and getInfo then specID, _, _, _, role = getInfo(specIndex) end
    if not specs[specID] then return nil, "Choose a supported specialization before generating SimC." end
    if not C_ClassTalents or not C_Traits or not C_Traits.GenerateImportString then
        return nil, "The client's talent export API is unavailable."
    end
    if C_Traits.GetLoadoutSerializationVersion and C_Traits.GetLoadoutSerializationVersion() ~= 2 then
        return nil, "This WoW talent export format needs an updated SimC exporter."
    end
    local config = C_ClassTalents.GetActiveConfigID()
    local talents = config and C_Traits.GenerateImportString(config)
    if type(talents) ~= "string" or talents == "" then
        return nil, "Talent data is still loading. Open your talents, then try Generate SimC again."
    end
    local name = UnitName("player")
    local _, class = UnitClass("player")
    local _, race = UnitRace("player")
    if not name or not class or not race then return nil, "Character data is still loading." end
    race = race == "Scourge" and "undead" or race:gsub("(%l)(%u)", "%1_%2"):lower()
    local region = GetCurrentRegionName and GetCurrentRegionName() or regions[GetCurrentRegion()]
    local lines = {
        "# Evil Twins Command Center " .. ET.version .. " - equipped gear and active talents",
        "# Captured " .. date("%Y-%m-%d %H:%M"), "",
        token(class) .. "=\"" .. name:gsub('[\r\n\"]', "") .. "\"",
        "level=" .. UnitLevel("player"), "race=" .. race,
        "region=" .. token(region), "server=" .. token(GetRealmName()),
        "role=" .. (role == "TANK" and "tank" or casterSpecs[specID] and "spell" or "attack"),
        "spec=" .. specs[specID], "talents=" .. talents,
    }
    if race == "zandalari_troll" and C_UnitAuras and C_UnitAuras.GetBuffDataByIndex then
        for index = 1, 40 do
            local aura = C_UnitAuras.GetBuffDataByIndex("player", index)
            if not aura then break end
            if issecretvalue and issecretvalue(aura.spellId) then break end
            if loaBuffs[aura.spellId] then lines[#lines + 1] = "zandalari_loa=" .. loaBuffs[aura.spellId]; break end
        end
    end
    if GetProfessions and GetProfessionInfo then
        local ids, values = { GetProfessions() }, {}
        for index = 1, 2 do
            if ids[index] then
                local _, _, rank, _, _, _, professionID = GetProfessionInfo(ids[index])
                if professions[professionID] then values[#values + 1] = professions[professionID] .. "=" .. (rank or 0) end
            end
        end
        if #values > 0 then lines[#lines + 1] = "professions=" .. table.concat(values, "/") end
    end
    -- Midnight's extra player-power trait system uses the same SimC entry:rank format.
    if C_Traits.GetConfigIDBySystemID then
        local extra = C_Traits.GetConfigIDBySystemID(48)
        if extra then
            local info = C_Traits.GetConfigInfo(extra)
            if not info or not info.treeIDs then return nil, "Extra talent data is still loading. Try again shortly." end
            local entries = {}
            for _, treeID in ipairs(info.treeIDs) do
                local nodes = C_Traits.GetTreeNodes(treeID)
                if not nodes then return nil, "Extra talent data is still loading. Try again shortly." end
                for _, nodeID in ipairs(nodes) do
                    local node = C_Traits.GetNodeInfo(extra, nodeID)
                    if node and node.ranksPurchased and node.ranksPurchased > 0 and node.activeEntry then
                        entries[#entries + 1] = node.activeEntry.entryID .. ":" .. node.activeEntry.rank
                    end
                end
            end
            if #entries > 0 then lines[#lines + 1] = "omnium_talents=" .. table.concat(entries, "/") end
        end
    end
    lines[#lines + 1] = ""
    for _, slot in ipairs(slots) do
        local link = GetInventoryItemLink("player", slot[1])
        if link then
            local line, err = self:ItemLine(slot[2], link)
            if not line then return nil, err end
            lines[#lines + 1] = line
        elseif GetInventoryItemID and GetInventoryItemID("player", slot[1]) then
            return nil, "Equipped item data is still loading. Try again shortly."
        end
    end
    return table.concat(lines, "\n") .. "\n"
end

function ET:GetSimCProfile()
    if InCombatLockdown and InCombatLockdown() then return nil, "Generate SimC after leaving combat." end
    local instance, method = self.SimC:InstalledCollector()
    if instance then
        -- The collector's function is an unbound method; supply its AceAddon instance.
        -- noBags=true also excludes bank items in the upstream exporter.
        local ok, profile, err = pcall(method, instance, false, true, false, nil)
        if ok and type(profile) == "string" and #profile > 0 and not err then return profile end
    end
    local ok, profile, err = pcall(self.SimC.NativeProfile, self.SimC)
    if not ok then return nil, "Character export data is not ready. Open your talents and try again shortly." end
    return profile, err
end
