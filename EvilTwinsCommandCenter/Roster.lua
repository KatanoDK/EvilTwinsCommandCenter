local _, ET = ...
local Roster = {}
ET.Roster = Roster

local function normalize(name)
    return (name or ""):lower():gsub("%s+", "")
end

function Roster:Presence()
    local presence = {}
    if IsInGuild() then
        local realm = GetNormalizedRealmName() or GetRealmName()
        for index = 1, GetNumGuildMembers() do
            local name, _, _, _, _, _, _, _, online, _, _, _, _, mobile = GetGuildRosterInfo(index)
            if name then
                local key = name:find("-", 1, true) and name or (name .. "-" .. realm)
                presence[normalize(key)] = online == true and mobile ~= true
            end
        end
    end
    presence[normalize(ET.key)] = true
    return presence
end

function Roster:Build(search, visibility, class, sort)
    search = (search or ""):lower():match("^%s*(.-)%s*$")
    local entries, classes, classSeen, total = {}, {}, {}, 0
    local presence = self:Presence()
    local leader = ET:CanUseLeaderTools()
    for key, snapshot in pairs(ET.db.characters) do
        if type(snapshot) == "table" and (leader or key == ET.key) then
            total = total + 1
            local classFile = snapshot.class or ""
            local className = (LOCALIZED_CLASS_NAMES_MALE or {})[classFile] or classFile
            if classFile ~= "" and not classSeen[classFile] then
                classSeen[classFile] = true
                classes[#classes + 1] = { value = classFile, text = className }
            end
            local online = presence[normalize(key)]
            local searchable = table.concat({ key, snapshot.spec or "", classFile, className }, " "):lower()
            if searchable:find(search, 1, true)
                and (not class or class == "ALL" or classFile == class)
                and (visibility ~= "ONLINE" or online == true)
                and (visibility ~= "WISHLIST" or #(snapshot.wishlist or {}) > 0) then
                entries[#entries + 1] = { key = key, snapshot = snapshot, online = online }
            end
        end
    end
    table.sort(classes, function(a, b) return a.text < b.text end)
    table.sort(entries, function(a, b)
        if sort == "ITEM_LEVEL" then
            local av, bv = a.snapshot.itemLevel or 0, b.snapshot.itemLevel or 0
            if av ~= bv then return av > bv end
        elseif sort == "RECENT" then
            local av, bv = a.snapshot.updated or 0, b.snapshot.updated or 0
            if av ~= bv then return av > bv end
        end
        return a.key:lower() < b.key:lower()
    end)
    return entries, total, classes
end

-- Only create widgets for the visible rows, even with hundreds of saved toons.
function Roster:VisibleRange(count, offset, height, rowHeight)
    local maxOffset = math.max(0, count * rowHeight - height)
    local first = math.floor(math.max(0, math.min(offset, maxOffset)) / rowHeight) + 1
    return first, math.min(count, first + math.ceil(height / rowHeight))
end
