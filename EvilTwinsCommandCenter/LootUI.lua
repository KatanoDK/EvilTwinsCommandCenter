local _, ET = ...
local UI = ET.UI

local function text(parent, value, font)
    local widget = parent:CreateFontString(nil, "OVERLAY", font or "GameFontHighlightSmall")
    widget:SetText(value or "")
    widget:SetJustifyH("LEFT")
    widget:SetWordWrap(false)
    return widget
end

local function dropdown(parent, title, x, y, width, options, selected, pick)
    local widget = CreateFrame("DropdownButton", nil, parent, "WowStyle1DropdownTemplate")
    widget:SetWidth(width)
    widget:SetPoint("TOPLEFT", x, y)
    text(parent, title):SetPoint("BOTTOMLEFT", widget, "TOPLEFT", 0, 3)
    widget:SetupMenu(function(_, root)
        for _, option in ipairs(options() or {}) do
            root:CreateRadio(option.name, selected, pick, option.id)
        end
    end)
    return widget
end

function UI:CreateLootPicker()
    if self.lootFrame then return end
    local frame = CreateFrame("Frame", "EvilTwinsCCLootPicker", UIParent, "BackdropTemplate")
    frame:SetSize(840, 600)
    frame:SetPoint("CENTER")
    frame:SetFrameStrata("DIALOG")
    ET:StyleWindow(frame)
    frame:EnableMouse(true)
    frame:SetMovable(true)
    frame:RegisterForDrag("LeftButton")
    frame:SetScript("OnDragStart", frame.StartMoving)
    frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
    frame:Hide()
    self.lootFrame = frame
    text(frame, "ADD LOOT TO MY WISHLIST", "GameFontNormalLarge"):SetPoint("TOPLEFT", 24, -22)
    local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
    close:SetPoint("TOPRIGHT", -4, -4)
    self.lootKind = "RAID"
    self.lootDifficulty = 15
    self.lootForSpec = true
    self.lootTypeDropdown = dropdown(frame, "Source", 24, -77, 160,
        function() return { { id = "RAID", name = "Raids" }, { id = "DUNGEON", name = "Mythic+ dungeons" } } end,
        function(value) return UI.lootKind == value end,
        function(value) UI.lootKind = value; UI:LoadLootSources() end)
    self.lootSourceDropdown = dropdown(frame, "Raid / dungeon", 198, -77, 360,
        function() return UI.lootSources end,
        function(value) return UI.lootSource and UI.lootSource.id == value end,
        function(value)
            for _, source in ipairs(UI.lootSources or {}) do
                if source.id == value then UI.lootSource = source; break end
            end
            UI:LoadLootBosses()
        end)
    self.lootDifficultyDropdown = dropdown(frame, "Difficulty", 572, -77, 230,
        function()
            return UI.lootKind == "RAID" and { { id = 17, name = "Raid Finder" }, { id = 14, name = "Normal" },
                { id = 15, name = "Heroic" }, { id = 16, name = "Mythic" } } or { { id = 8, name = "Mythic+" } }
        end,
        function(value) return UI.lootDifficulty == value end,
        function(value) UI.lootDifficulty = value; UI:RefreshLoot(true) end)
    self.lootBossDropdown = dropdown(frame, "Boss", 24, -136, 360,
        function() return UI.lootBosses end,
        function(value) return UI.lootBoss == value end,
        function(value) UI.lootBoss = value; UI:RefreshLoot(true) end)
    self.lootClassDropdown = dropdown(frame, "Loot filter", 398, -136, 180,
        function() return { { id = true, name = "My current spec" }, { id = false, name = "All classes" } } end,
        function(value) return UI.lootForSpec == value end,
        function(value) UI.lootForSpec = value; UI:RefreshLoot(true) end)
    self.lootSearch = CreateFrame("EditBox", nil, frame, "InputBoxTemplate")
    self.lootSearch:SetSize(206, 25)
    self.lootSearch:SetPoint("TOPLEFT", 596, -136)
    self.lootSearch:SetAutoFocus(false)
    self.lootSearch:SetScript("OnEscapePressed", self.lootSearch.ClearFocus)
    self.lootSearch:SetScript("OnTextChanged", function() UI:RenderLoot(true) end)
    text(frame, "Search items"):SetPoint("BOTTOMLEFT", self.lootSearch, "TOPLEFT", 0, 3)
    local card = CreateFrame("Frame", nil, frame, "BackdropTemplate")
    card:SetPoint("TOPLEFT", 18, -180)
    card:SetSize(804, 355)
    ET:StylePanel(card)
    local scroll = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    scroll:SetPoint("TOPLEFT", 24, -187)
    scroll:SetSize(760, 338)
    local child = CreateFrame("Frame", nil, scroll)
    child:SetWidth(754)
    child:SetHeight(1)
    scroll:SetScrollChild(child)
    self.lootScroll, self.lootChild, self.lootRows = scroll, child, {}
    scroll:HookScript("OnVerticalScroll", function() UI:RenderLootRows() end)
    self.lootEmpty = text(card, "")
    self.lootEmpty:SetPoint("TOPLEFT", 20, -22)
    self.lootEmpty:SetWidth(730)
    self.lootStatus = text(frame, "")
    self.lootStatus:SetPoint("BOTTOMLEFT", 24, 44)
    self.lootStatus:SetWidth(790)
    text(frame, "Add saves the item and its source. Wishlist entries do not select an item level or upgrade track.")
        :SetPoint("BOTTOMLEFT", 24, 23)
    frame:RegisterEvent("EJ_LOOT_DATA_RECIEVED")
    frame:RegisterEvent("GET_ITEM_INFO_RECEIVED")
    frame:SetScript("OnEvent", function()
        -- Journal queries can emit synchronous loot events while changing or
        -- restoring their context. Those events must not start a query loop.
        if not frame:IsShown() or UI.lootTimer or ET.Loot.querying then return end
        UI.lootTimer = C_Timer.NewTimer(0.2, function()
            UI.lootTimer = nil
            if frame:IsShown() then UI:RefreshLoot() end
        end)
    end)
    frame:SetScript("OnHide", function()
        if UI.lootTimer then UI.lootTimer:Cancel(); UI.lootTimer = nil end
    end)
end

function UI:ShowLootPicker()
    if not ET:CanUseLeaderTools() or self.selected ~= ET.key then return end
    self:CreateLootPicker()
    self.lootFrame:Show()
    self:LoadLootSources()
end

function UI:LoadLootSources()
    self.lootDifficulty = self.lootKind == "RAID" and 15 or 8
    local sources, err = ET.Loot:Sources(self.lootKind)
    self.lootSources = sources or {}
    self.lootSource = self.lootSources[1]
    self.lootSourceDropdown:GenerateMenu()
    self.lootDifficultyDropdown:GenerateMenu()
    self:LoadLootBosses(err)
end

function UI:LoadLootBosses(err)
    local bosses, bossErr
    if self.lootSource then bosses, bossErr = ET.Loot:Bosses(self.lootSource) end
    self.lootBosses, self.lootBoss = bosses or {}, 0
    self.lootBossDropdown:GenerateMenu()
    self:RefreshLoot(true, err or bossErr)
end

function UI:RefreshLoot(resetScroll, errorMessage)
    if not self.lootFrame or not self.lootFrame:IsShown() then return end
    if not ET:CanUseLeaderTools() then self.lootFrame:Hide(); return end
    local items, err
    if self.lootSource then
        items, err = ET.Loot:Items(self.lootSource, self.lootBoss, self.lootDifficulty, self.lootForSpec)
    end
    self.lootItems = items or {}
    self.lootError = errorMessage or err or self.lootItems.error
    self:RenderLoot(resetScroll)
end

function UI:RenderLoot(resetScroll)
    if not self.lootChild then return end
    local search = self.lootSearch:GetText():lower():match("^%s*(.-)%s*$")
    self.lootDisplay = {}
    for _, item in ipairs(self.lootItems or {}) do
        if search == "" or item.name:lower():find(search, 1, true)
            or (item.slot or ""):lower():find(search, 1, true) then
            self.lootDisplay[#self.lootDisplay + 1] = item
        end
    end
    self.lootChild:SetHeight(math.max(1, #self.lootDisplay * 46))
    self.lootScroll:SetVerticalScroll(resetScroll and 0 or math.min(self.lootScroll:GetVerticalScroll(),
        math.max(0, #self.lootDisplay * 46 - self.lootScroll:GetHeight())))
    self:RenderLootRows()
    self.lootEmpty:SetShown(#self.lootDisplay == 0)
    self.lootEmpty:SetText(self.lootError or (not self.lootSource and "No sources available yet. Reopen this picker after game data loads."
        or search ~= "" and "No matching items." or "No loot loaded for this selection. Try All classes or another difficulty."))
    self.lootStatus:SetText(self.lootError or (#self.lootDisplay .. " items • " .. #(ET.db.wishlists[ET.key] or {})
        .. "/20 on your wishlist" .. (self.lootItems.pending and " • Loading item names…" or "")))
end

function UI:RenderLootRows()
    if not self.lootDisplay then return end
    local first, last = ET.Roster:VisibleRange(#self.lootDisplay, self.lootScroll:GetVerticalScroll(), self.lootScroll:GetHeight(), 46)
    local used = 0
    for index = first, last do
        used = used + 1
        local row = self.lootRows[used]
        if not row then
            row = CreateFrame("Button", nil, self.lootChild)
            row:SetSize(754, 44)
            row.background = row:CreateTexture(nil, "BACKGROUND")
            row.background:SetAllPoints()
            row.icon = row:CreateTexture(nil, "ARTWORK")
            row.icon:SetSize(32, 32)
            row.icon:SetPoint("LEFT", 6, 0)
            row.name = text(row, "", "GameFontNormalSmall")
            row.name:SetPoint("TOPLEFT", 48, -7)
            row.name:SetWidth(594)
            row.detail = text(row, "")
            row.detail:SetPoint("TOPLEFT", 48, -25)
            row.detail:SetWidth(594)
            row.add = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
            row.add:SetSize(86, 24)
            row.add:SetPoint("RIGHT", -8, 0)
            row.add:SetScript("OnClick", function()
                local ok, err = ET.Loot:Add(row.item, UI.lootSource)
                if not ok then ET:Print(err); return end
                UI:Refresh()
                UI:RenderLoot()
            end)
            row:SetScript("OnEnter", function(self)
                GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
                GameTooltip:SetHyperlink(self.item.link or ("item:" .. self.item.id))
                GameTooltip:Show()
            end)
            row:SetScript("OnLeave", function() GameTooltip:Hide() end)
            self.lootRows[used] = row
        end
        row.item = self.lootDisplay[index]
        row:ClearAllPoints()
        row:SetPoint("TOPLEFT", 0, -(index - 1) * 46)
        row.icon:SetTexture(row.item.icon or 134400)
        row.name:SetText(row.item.name)
        row.detail:SetText((row.item.slot or "") .. (row.item.boss ~= "" and (" • " .. row.item.boss) or ""))
        local added = ET.Loot:HasWish(row.item.id)
        row.add:SetText(added and "Added" or "Add")
        row.add:SetEnabled(not added and #(ET.db.wishlists[ET.key] or {}) < 20 and ET:CanUseLeaderTools())
        row.background:SetColorTexture(1, 1, 1, index % 2 == 0 and 0.04 or 0)
        row:Show()
    end
    for index = used + 1, #self.lootRows do self.lootRows[index]:Hide() end
end
