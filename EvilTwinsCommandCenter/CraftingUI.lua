local _, ET = ...
local UI = ET.UI

-- Base profession skill-line IDs, independent of expansion and localized names.
local gathering = { [182] = true, [186] = true, [393] = true }
local function hasCraftingProfession(professions)
    for id in pairs(professions) do
        if not gathering[tonumber(id)] then return true end
    end
    return false
end

local function text(parent, value, font)
    local widget = parent:CreateFontString(nil, "OVERLAY", font or "GameFontNormal")
    widget:SetJustifyH("LEFT")
    widget:SetWordWrap(false)
    widget:SetText(value or "")
    return widget
end

local function nameOf(key)
    return Ambiguate(key, "short")
end

function UI:CreateCraftingFrame()
    if self.craftingFrame then return end
    local frame = CreateFrame("Frame", "EvilTwinsCCCraftingFrame", UIParent, "BackdropTemplate")
    frame:SetSize(960, 640)
    frame:SetPoint("CENTER")
    frame:SetFrameStrata("DIALOG")
    ET:StyleWindow(frame)
    frame:EnableMouse(true)
    frame:SetMovable(true)
    frame:RegisterForDrag("LeftButton")
    frame:SetScript("OnDragStart", frame.StartMoving)
    frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
    frame:Hide()
    text(frame, "GUILD CRAFTING DIRECTORY", "GameFontNormalLarge"):SetPoint("TOPLEFT", 23, -20)
    local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
    close:SetPoint("TOPRIGHT", -4, -4)
    text(frame, "Open a profession to share recipes. Saved scans remain visible offline.",
        "GameFontHighlightSmall"):SetPoint("TOPLEFT", 24, -51)
    local cards = {}
    for index, bounds in ipairs({ { 18, 220 }, { 254, 688 } }) do
        local card = CreateFrame("Frame", nil, frame, "BackdropTemplate")
        card:SetPoint("TOPLEFT", bounds[1], -96)
        card:SetSize(bounds[2], 490)
        ET:StylePanel(card)
        cards[index] = card
    end
    text(cards[1], "CRAFTERS", "GameFontNormal"):SetPoint("TOPLEFT", 14, -14)
    text(cards[2], "PROFESSIONS & RECIPES", "GameFontNormal"):SetPoint("TOPLEFT", 16, -14)
    local search = CreateFrame("EditBox", nil, frame, "InputBoxTemplate")
    search:SetSize(240, 25)
    search:SetPoint("TOPRIGHT", -38, -59)
    search:SetAutoFocus(false)
    text(frame, "Search recipes or professions", "GameFontHighlightSmall"):SetPoint("BOTTOMLEFT", search, "TOPLEFT", 0, 2)
    search:SetScript("OnEscapePressed", search.ClearFocus)
    search:SetScript("OnTextChanged", function() UI:RefreshCrafting(true) end)
    self.craftingSearch = search
    local left = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    left:SetPoint("TOPLEFT", 26, -137)
    left:SetSize(187, 432)
    local leftChild = CreateFrame("Frame", nil, left)
    leftChild:SetWidth(187)
    leftChild:SetHeight(1)
    left:SetScrollChild(leftChild)
    self.craftingPeople = leftChild
    local right = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    right:SetPoint("TOPLEFT", 264, -137)
    right:SetSize(646, 432)
    local rightChild = CreateFrame("Frame", nil, right)
    rightChild:SetWidth(646)
    rightChild:SetHeight(1)
    right:SetScrollChild(rightChild)
    self.craftingRecipes = rightChild
    self.craftingScroll = right
    right:HookScript("OnVerticalScroll", function() UI:RenderCraftingRecipes() end)
    self.craftingExpanded = {}
    self.craftingMode = "KNOWN"
    self.craftingFilter = CreateFrame("DropdownButton", nil, frame, "WowStyle1DropdownTemplate")
    self.craftingFilter:SetWidth(200)
    self.craftingFilter:SetPoint("TOPLEFT", 254, -59)
    self.craftingFilter:SetupMenu(function(_, root)
        for _, choice in ipairs({ { "Known recipes", "KNOWN" }, { "All scanned recipes", "ALL" } }) do
            root:CreateRadio(choice[1], function(value) return UI.craftingMode == value end,
                function(value) UI.craftingMode = value; UI:RefreshCrafting(true) end, choice[2])
        end
    end)
    self.craftingEmpty = text(cards[2], "", "GameFontHighlightSmall")
    self.craftingEmpty:SetPoint("TOPLEFT", 26, -53)
    self.craftingEmpty:SetWidth(610)
    self.craftingPersonRows = {}
    self.craftingRecipeRows = {}
    local footer = text(frame, "", "GameFontHighlightSmall")
    footer:SetPoint("BOTTOMLEFT", 24, 26)
    footer:SetWidth(840)
    self.craftingFooter = footer
    self.craftingFrame = frame
    frame:SetScript("OnShow", function() UI:RefreshCrafting() end)
end

function UI:ShowCrafting()
    if not IsInGuild() then ET:Print("Join a guild to use the crafting directory."); return end
    self:CreateCraftingFrame()
    if C_GuildInfo and C_GuildInfo.GuildRoster then C_GuildInfo.GuildRoster() end
    self.craftingFrame:Show()
end

local function recipeMatches(recipe, profession, search)
    if search == "" then return true end
    return recipe.name:lower():find(search, 1, true)
        or tostring(recipe.id):find(search, 1, true)
        or profession:lower():find(search, 1, true)
end

function UI:RefreshCrafting(resetScroll)
    if not self.craftingFrame or not self.craftingFrame:IsShown() then return end
    if not IsInGuild() then self.craftingFrame:Hide(); return end
    local keys = {}
    for key, entry in pairs(ET.db.crafting) do
        if type(entry) == "table" and type(entry.professions) == "table"
            and hasCraftingProfession(entry.professions)
            and (key == ET.key or ET:GetGuildRankIndex(key) ~= nil) then
            keys[#keys + 1] = key
        end
    end
    table.sort(keys, function(a, b) return a:lower() < b:lower() end)
    local selected = self.craftingSelected
    local available = selected == nil
    for _, key in ipairs(keys) do if key == selected then available = true end end
    if not available then selected = nil; self.craftingSelected = nil end
    for index = 1, #keys + 1 do
        local key = keys[index - 1]
        local row = self.craftingPersonRows[index]
        if not row then
            row = CreateFrame("Button", nil, self.craftingPeople)
            row:SetSize(187, 32)
            row.background = row:CreateTexture(nil, "BACKGROUND")
            row.background:SetAllPoints()
            row:SetHighlightTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight")
            row.label = text(row, "", "GameFontNormalSmall")
            row.label:SetPoint("LEFT", 5, 0)
            row.label:SetWidth(175)
            row:SetScript("OnClick", function(self)
                UI.craftingSelected = self.key
                UI:RefreshCrafting(true)
            end)
            self.craftingPersonRows[index] = row
        end
        row.key = key
        row:SetPoint("TOPLEFT", 0, -(index - 1) * 34)
        row.label:SetText(key and nameOf(key) or "All guild crafters")
        row.label:SetTextColor(key == selected and 1 or 0.75, key == selected and 0.8 or 0.7, 1)
        row.background:SetColorTexture(0.35, 0.26, 0.55, key == selected and 0.5 or 0)
        row:Show()
    end
    for index = #keys + 2, #self.craftingPersonRows do self.craftingPersonRows[index]:Hide() end
    self.craftingPeople:SetHeight(math.max(1, (#keys + 1) * 34))

    local search = self.craftingSearch:GetText():lower():match("^%s*(.-)%s*$")
    local entries = {}
    local byID = {}
    for _, key in ipairs(keys) do
        if not selected or key == selected then
            for professionID, profession in pairs(ET.db.crafting[key].professions) do
                if not gathering[tonumber(professionID)] then
                    for _, recipe in ipairs(profession.recipes) do
                        if recipeMatches(recipe, profession.name, search) then
                            local result = byID[recipe.id]
                            if not result then
                                result = { id = recipe.id, name = recipe.name,
                                    profession = profession.name, known = {}, missing = {}, scans = {} }
                                byID[recipe.id] = result
                                entries[#entries + 1] = result
                            end
                            local list = recipe.learned and result.known or result.missing
                            list[#list + 1] = nameOf(key)
                            result.scans[#result.scans + 1] = nameOf(key) .. ": " .. date("%Y-%m-%d %H:%M", profession.updated)
                        end
                    end
                end
            end
        end
    end
    table.sort(entries, function(a, b)
        if a.name:lower() ~= b.name:lower() then return a.name:lower() < b.name:lower() end
        return a.id < b.id
    end)
    local groups, groupNames, total = {}, {}, 0
    for _, entry in ipairs(entries) do
        if self.craftingMode == "ALL" or #entry.known > 0 then
            local group = groups[entry.profession]
            if not group then
                group = { name = entry.profession, recipes = {}, known = 0 }
                groups[entry.profession] = group
                groupNames[#groupNames + 1] = entry.profession
            end
            group.recipes[#group.recipes + 1] = entry
            if #entry.known > 0 then group.known = group.known + 1 end
            total = total + 1
        end
    end
    table.sort(groupNames)
    local display, y = {}, 0
    for _, name in ipairs(groupNames) do
        local group = groups[name]
        local expanded = search ~= "" or self.craftingExpanded[name] == true
        display[#display + 1] = { header = true, group = group, expanded = expanded, y = y, height = 36 }
        y = y + 36
        if expanded then
            for _, entry in ipairs(group.recipes) do
                display[#display + 1] = { data = entry, y = y, height = 44 }
                y = y + 44
            end
        end
    end
    self.craftingDisplay = display
    self.craftingRecipes:SetHeight(math.max(1, y))
    local offset = resetScroll and 0 or self.craftingScroll:GetVerticalScroll()
    self.craftingScroll:SetVerticalScroll(math.min(offset, math.max(0, y - self.craftingScroll:GetHeight())))
    self:RenderCraftingRecipes()
    self.craftingEmpty:SetShown(#display == 0)
    self.craftingEmpty:SetText(#keys == 0 and "No profession scans yet. Open your profession windows to share recipes."
        or "No matching recipes. Try All scanned recipes or a different search.")
    self.craftingFooter:SetText(total .. " recipes • " .. #groupNames .. " professions • Click a profession to expand. Search opens matching groups.")
end

function UI:RenderCraftingRecipes()
    if not self.craftingDisplay then return end
    local offset, height = self.craftingScroll:GetVerticalScroll(), self.craftingScroll:GetHeight()
    local used = 0
    for _, entry in ipairs(self.craftingDisplay) do
        if entry.y + entry.height >= offset and entry.y <= offset + height then
            used = used + 1
            local row = self.craftingRecipeRows[used]
            if not row then
                row = CreateFrame("Button", nil, self.craftingRecipes)
                row:SetWidth(640)
                row.background = row:CreateTexture(nil, "BACKGROUND")
                row.background:SetAllPoints()
                row:SetHighlightTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight")
                row.name = text(row, "", "GameFontNormalSmall")
                row.name:SetPoint("TOPLEFT", 5, -2)
                row.name:SetWidth(612)
                row.detail = text(row, "", "GameFontHighlightSmall")
                row.detail:SetPoint("TOPLEFT", 22, -25)
                row.detail:SetWidth(612)
                row:SetScript("OnClick", function(self)
                    if not self.group then return end
                    if UI.craftingSearch:GetText():match("%S") then return end
                    UI.craftingExpanded[self.group.name] = not UI.craftingExpanded[self.group.name]
                    UI:RefreshCrafting()
                end)
                row:SetScript("OnEnter", function(self)
                    if not self.data then return end
                    GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
                    GameTooltip:AddLine(self.data.name)
                    GameTooltip:AddLine("Known: " .. (#self.data.known > 0 and table.concat(self.data.known, ", ") or "none"), 0.4, 1, 0.4, true)
                    GameTooltip:AddLine("Not learned: " .. (#self.data.missing > 0 and table.concat(self.data.missing, ", ") or "none recorded"), 1, 0.6, 0.4, true)
                    for _, scan in ipairs(self.data.scans) do GameTooltip:AddLine(scan, 0.7, 0.7, 0.7) end
                    GameTooltip:Show()
                end)
                row:SetScript("OnLeave", function() GameTooltip:Hide() end)
                self.craftingRecipeRows[used] = row
            end
            local result = entry.data
            row.data = result
            row.group = entry.group
            row:ClearAllPoints()
            row:SetPoint("TOPLEFT", 0, -entry.y)
            row:SetHeight(entry.height - 2)
            row.name:ClearAllPoints()
            row.name:SetPoint("TOPLEFT", entry.header and 10 or 22, entry.header and -10 or -5)
            row.detail:SetShown(not entry.header)
            row.background:SetColorTexture(0.35, 0.26, 0.55, entry.header and 0.3 or used % 2 == 0 and 0.045 or 0)
            if entry.header then
                row.name:SetText((entry.expanded and "[-] " or "[+] ") .. entry.group.name .. "  •  "
                    .. #entry.group.recipes .. " recipes / " .. entry.group.known .. " known")
            else
                row.name:SetText(result.name)
                if self.craftingSelected then
                    row.detail:SetText(#result.known > 0 and "Learned" or "Not learned")
                    row.detail:SetTextColor(#result.known > 0 and 0.45 or 1, #result.known > 0 and 0.95 or 0.65, 0.45)
                else
                    row.detail:SetText(#result.known .. " known crafter(s)" .. (#result.known > 0 and "  •  " .. table.concat(result.known, ", ") or ""))
                    row.detail:SetTextColor(0.85, 0.85, 0.85)
                end
            end
            row:Show()
        end
    end
    for index = used + 1, #self.craftingRecipeRows do self.craftingRecipeRows[index]:Hide() end
end
