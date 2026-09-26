local _, ET = ...
local Protocol = {}
ET.Protocol = Protocol

local MAX_FIELD = 130000
local alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
local alphabetIndex = {}
for index = 1, #alphabet do alphabetIndex[alphabet:sub(index, index)] = index - 1 end

function Protocol:ToBase64(input)
    local output = {}
    for index = 1, #input, 3 do
        local a, b, c = input:byte(index, index + 2)
        local value = a * 65536 + (b or 0) * 256 + (c or 0)
        local one = math.floor(value / 262144) % 64 + 1
        local two = math.floor(value / 4096) % 64 + 1
        local three = math.floor(value / 64) % 64 + 1
        local four = value % 64 + 1
        output[#output + 1] = alphabet:sub(one, one) .. alphabet:sub(two, two)
            .. (b and alphabet:sub(three, three) or "=")
            .. (c and alphabet:sub(four, four) or "=")
    end
    return table.concat(output)
end

function Protocol:FromBase64(input)
    if #input % 4 ~= 0 or input:find("[^A-Za-z0-9+/=]") then return nil end
    local output = {}
    for index = 1, #input, 4 do
        local a, b, c, d = input:sub(index, index + 3):match("(.)(.)(.)(.)")
        if not alphabetIndex[a] or not alphabetIndex[b] then return nil end
        if c == "=" and d ~= "=" then return nil end
        if (c == "=" or d == "=") and index + 3 ~= #input then return nil end
        if c ~= "=" and not alphabetIndex[c] then return nil end
        if d ~= "=" and not alphabetIndex[d] then return nil end
        local value = alphabetIndex[a] * 262144 + alphabetIndex[b] * 4096
            + (alphabetIndex[c] or 0) * 64 + (alphabetIndex[d] or 0)
        output[#output + 1] = string.char(math.floor(value / 65536) % 256)
        if c ~= "=" then output[#output + 1] = string.char(math.floor(value / 256) % 256) end
        if d ~= "=" then output[#output + 1] = string.char(value % 256) end
    end
    return table.concat(output)
end

function Protocol:Pack(fields)
    local output = {}
    for _, value in ipairs(fields) do
        value = tostring(value or "")
        output[#output + 1] = #value .. ":" .. value
    end
    return table.concat(output)
end

function Protocol:Unpack(data, maxFields)
    if type(data) ~= "string" or #data > MAX_FIELD then return nil end
    local fields, position = {}, 1
    while position <= #data do
        if #fields >= maxFields then return nil end
        local separator = data:find(":", position, true)
        if not separator or separator - position > 6 then return nil end
        local length = tonumber(data:sub(position, separator - 1))
        if not length or length < 0 or length > MAX_FIELD then return nil end
        local finish = separator + length
        if finish > #data then return nil end
        fields[#fields + 1] = data:sub(separator + 1, finish)
        position = finish + 1
    end
    return fields
end

function Protocol:EncodeSnapshot(snapshot)
    local fields = {
        "1", snapshot.guid, snapshot.name, snapshot.realm, snapshot.class,
        snapshot.specID, snapshot.spec, snapshot.level, snapshot.itemLevel,
        snapshot.updated, snapshot.talent, #snapshot.wishlist,
    }
    for _, wish in ipairs(snapshot.wishlist) do
        fields[#fields + 1] = wish.id
        fields[#fields + 1] = wish.note
    end
    for index = 1, #ET.slots do
        fields[#fields + 1] = snapshot.gear[index] or ""
    end
    return self:Pack(fields)
end

function Protocol:DecodeSnapshot(data)
    local fields = self:Unpack(data, 12 + 40 + #ET.slots)
    if not fields or fields[1] ~= "1" then return nil end
    local wishCount = tonumber(fields[12])
    if not wishCount or wishCount < 0 or wishCount > 20 then return nil end
    if #fields ~= 12 + wishCount * 2 + #ET.slots then return nil end
    local snapshot = {
        guid = fields[2], name = fields[3], realm = fields[4], class = fields[5],
        specID = tonumber(fields[6]) or 0, spec = fields[7],
        level = tonumber(fields[8]) or 0, itemLevel = tonumber(fields[9]) or 0,
        updated = tonumber(fields[10]) or 0, talent = fields[11],
        wishlist = {}, gear = {},
    }
    if #snapshot.guid > 64 or #snapshot.name > 64 or #snapshot.realm > 64
        or #snapshot.spec > 80 or #snapshot.talent > 3000 then return nil end
    local position = 13
    for _ = 1, wishCount do
        local id = tonumber(fields[position])
        local note = fields[position + 1]
        if not id or id < 1 or id > 99999999 or #note > 100 then return nil end
        snapshot.wishlist[#snapshot.wishlist + 1] = { id = id, note = note }
        position = position + 2
    end
    for index = 1, #ET.slots do
        local link = fields[position] or ""
        if #link > 1000 then return nil end
        snapshot.gear[index] = link
        position = position + 1
    end
    return snapshot
end

function Protocol:EncodeSimC(guid, updated, profile)
    return self:Pack({ "1", guid, updated, profile })
end

function Protocol:DecodeSimC(data)
    local fields = self:Unpack(data, 4)
    if not fields or #fields ~= 4 or fields[1] ~= "1" then return nil end
    local updated = tonumber(fields[3])
    if #fields[2] == 0 or #fields[2] > 64 or not updated or updated <= 0
        or #fields[4] == 0 or #fields[4] > 89000 then return nil end
    if not fields[4]:find("^#") and not fields[4]:find("^%a+=[^\n]+") then return nil end
    return fields[2], updated, fields[4]
end

function Protocol:EncodeBag(guid, bagID, updated, items)
    local fields = { "1", guid, bagID, updated, #items }
    for _, item in ipairs(items) do
        fields[#fields + 1] = item.slot
        fields[#fields + 1] = item.id
        fields[#fields + 1] = item.count
        fields[#fields + 1] = item.link or ""
    end
    return self:Pack(fields)
end

function Protocol:DecodeBag(data)
    local fields = self:Unpack(data, 5 + 150 * 4)
    if not fields or #fields < 5 or fields[1] ~= "1" then return nil end
    local guid, bagID, updated, count = fields[2], tonumber(fields[3]), tonumber(fields[4]), tonumber(fields[5])
    if #guid == 0 or #guid > 64 or not bagID or bagID % 1 ~= 0 or bagID < 0 or bagID > 20
        or not updated or updated <= 0 or not count or count % 1 ~= 0 or count < 0 or count > 150
        or #fields ~= 5 + count * 4 then return nil end
    local items, previousSlot = {}, 0
    for index = 1, count do
        local position = 6 + (index - 1) * 4
        local slot, id, quantity = tonumber(fields[position]), tonumber(fields[position + 1]), tonumber(fields[position + 2])
        local link = fields[position + 3]
        if not slot or slot % 1 ~= 0 or slot <= previousSlot or slot > 150
            or not id or id % 1 ~= 0 or id < 1 or id > 99999999
            or not quantity or quantity % 1 ~= 0 or quantity < 1 or quantity > 1000000
            or #link > 500 then return nil end
        items[index] = { slot = slot, id = id, count = quantity, link = link }
        previousSlot = slot
    end
    return guid, bagID, updated, items
end
