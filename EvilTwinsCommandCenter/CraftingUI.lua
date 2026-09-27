local _, ET = ...
local UI = ET.UI

local function text(parent, value, font)
    local widget = parent:CreateFontString(nil, "OVERLAY", font or "GameFontNormal")
    widget:SetJustifyH("LEFT")
    widget:SetText(value or "")
    return widget
end

local function nameOf(key)
    return Ambiguate(key, "short")
end

function UI:CreateCraftingFrame()
    if self.craftingFrame then return end
    local frame = CreateFrame("Frame", "EvilTwinsCCCraftingFrame", UIParent, "BackdropTemplate")
    frame:SetSize(900, 600)
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
    text(frame, "Recipes update when a member opens each profession. Saved scans remain visible offline.",
        "GameFontHighlightSmall"):SetPoint("TOPLEFT", 24, -51)
    text(frame, "Crafters", "GameFontNormal"):SetPoint("TOPLEFT", 24, -85)
    text(frame, "Recipes", "GameFontNormal"):SetPoint("TOPLEFT", 256, -85)
    local search = CreateFrame("EditBox", nil, frame, "InputBoxTemplate")
    search:SetSize(240, 25)
    search:SetPoint("TOPRIGHT", -38, -77)
    search:SetAutoFocus(false)
    text(frame, "Search recipes or professions", "GameFontHighlightSmall"):SetPoint("BOTTOMLEFT", search, "TOPLEFT", 0, 2)
    search:SetScript("OnEscapePressed", search.ClearFocus)
    search:SetScript("OnTextChanged", function() UI:RefreshCrafting() end)
    self.craftingSearch = search
    local left = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    left:SetPoint("TOPLEFT", 23, -115)
    left:SetSize(215, 429)
    local leftChild = CreateFrame("Frame", nil, left)
    leftChild:SetWidth(195)
    leftChild:SetHeight(1)
    left:SetScrollChild(leftChild)
    self.craftingPeople = leftChild
    local right = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    right:SetPoint("TOPLEFT", 254, -115)
    right:SetSize(620, 429)
    local rightChild = CreateFrame("Frame", nil, right)
    rightChild:SetWidth(598)
    rightChild:SetHeight(1)
    right:SetScrollChild(rightChild)
    self.craftingRecipes = rightChild
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

function UI:RefreshCrafting()
    if not self.craftingFrame or not self.craftingFrame:IsShown() then return end
    local keys = {}
    for key, entry in pairs(ET.db.crafting) do
        if type(entry) == "table" and type(entry.professions) == "table"
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
            row:SetSize(190, 29)
            row.label = text(row, "", "GameFontNormalSmall")
            row.label:SetPoint("LEFT", 5, 0)
            row.label:SetWidth(180)
            row:SetScript("OnClick", function(self)
                UI.craftingSelected = self.key
                UI:RefreshCrafting()
            end)
            self.craftingPersonRows[index] = row
        end
        row.key = key
        row:SetPoint("TOPLEFT", 0, -(index - 1) * 30)
        row.label:SetText(key and nameOf(key) or "All guild crafters")
        row.label:SetTextColor(key == selected and 1 or 0.75, key == selected and 0.8 or 0.7, 1)
        row:Show()
    end
    for index = #keys + 2, #self.craftingPersonRows do self.craftingPersonRows[index]:Hide() end
    self.craftingPeople:SetHeight(math.max(1, (#keys + 1) * 30))

    local search = self.craftingSearch:GetText():lower():match("^%s*(.-)%s*$")
    local entries = {}
    local byID = {}
    for _, key in ipairs(keys) do
        if not selected or key == selected then
            for _, profession in pairs(ET.db.crafting[key].professions) do
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
    table.sort(entries, function(a, b)
        if a.name:lower() ~= b.name:lower() then return a.name:lower() < b.name:lower() end
        return a.id < b.id
    end)
    local shown = math.min(#entries, 200)
    for index = 1, shown do
        local row = self.craftingRecipeRows[index]
        if not row then
            row = CreateFrame("Button", nil, self.craftingRecipes)
            row:SetSize(590, 37)
            row.name = text(row, "", "GameFontNormalSmall")
            row.name:SetPoint("TOPLEFT", 5, -2)
            row.name:SetWidth(580)
            row.detail = text(row, "", "GameFontHighlightSmall")
            row.detail:SetPoint("TOPLEFT", 5, -19)
            row.detail:SetWidth(580)
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
            self.craftingRecipeRows[index] = row
        end
        local result = entries[index]
        row.data = result
        row:SetPoint("TOPLEFT", 0, -(index - 1) * 38)
        row.name:SetText(result.name .. "  •  " .. result.profession)
        if selected then
            row.detail:SetText(#result.known > 0 and "Learned" or "Not learned")
            row.detail:SetTextColor(#result.known > 0 and 0.45 or 1, #result.known > 0 and 0.95 or 0.65, 0.45)
        else
            row.detail:SetText(#result.known .. " known crafter(s)" .. (#result.known > 0 and "  •  " .. table.concat(result.known, ", ") or ""))
            row.detail:SetTextColor(0.85, 0.85, 0.85)
        end
        row:Show()
    end
    for index = shown + 1, #self.craftingRecipeRows do self.craftingRecipeRows[index]:Hide() end
    self.craftingRecipes:SetHeight(math.max(1, shown * 38))
    if #keys == 0 then
        self.craftingFooter:SetText("No profession scans yet. Members can open their profession windows to add recipes automatically.")
    elseif #entries > shown then
        self.craftingFooter:SetText("Showing " .. shown .. " of " .. #entries .. " recipes. Search to narrow the list.")
    else
        self.craftingFooter:SetText(#entries .. " recipes from saved profession scans. Learned means known; materials and quality are not checked.")
    end
end
