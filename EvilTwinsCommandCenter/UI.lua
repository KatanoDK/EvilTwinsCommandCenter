local _, ET = ...
local UI = { rosterRows = {}, gearRows = {}, wishRows = {} }
ET.UI = UI

function ET:StyleWindow(frame)
    -- The dialog background texture contains transparency; a solid texture keeps
    -- text readable when multiple addon windows overlap.
    frame:SetBackdrop({
        bgFile = "Interface\\Buttons\\WHITE8X8",
        edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
        edgeSize = 24,
        insets = { left = 6, right = 6, top = 6, bottom = 6 },
    })
    frame:SetBackdropColor(0.035, 0.04, 0.055, 1)
    frame:SetBackdropBorderColor(0.5, 0.45, 0.65, 1)
    frame:SetAlpha(1)
    frame:SetClampedToScreen(true)
    frame:SetToplevel(true)
    frame:HookScript("OnShow", function(self) self:Raise() end)
end

local function label(parent, text, size, color)
    local widget = parent:CreateFontString(nil, "OVERLAY", size or "GameFontNormal")
    widget:SetJustifyH("LEFT")
    widget:SetWordWrap(false)
    widget:SetText(text or "")
    if color then widget:SetTextColor(unpack(color)) end
    return widget
end

local function button(parent, text, width, height, onClick)
    local widget = CreateFrame("Button", nil, parent, "UIPanelButtonTemplate")
    widget:SetSize(width, height or 25)
    widget:SetText(text)
    widget:SetScript("OnClick", onClick)
    return widget
end

local function panel(parent, width, height)
    local widget = CreateFrame("Frame", nil, parent, "BackdropTemplate")
    widget:SetSize(width, height)
    widget:SetBackdrop({ bgFile = "Interface\\Buttons\\WHITE8X8", edgeFile = "Interface\\Buttons\\WHITE8X8", edgeSize = 1 })
    widget:SetBackdropColor(0.065, 0.075, 0.095, 1)
    widget:SetBackdropBorderColor(0.18, 0.2, 0.26, 1)
    return widget
end

function ET:StylePanel(widget)
    widget:SetBackdrop({ bgFile = "Interface\\Buttons\\WHITE8X8", edgeFile = "Interface\\Buttons\\WHITE8X8", edgeSize = 1 })
    widget:SetBackdropColor(0.065, 0.075, 0.095, 1)
    widget:SetBackdropBorderColor(0.18, 0.2, 0.26, 1)
end

local function navigationTab(parent, title, onClick)
    local tab = CreateFrame("Button", nil, parent, "BackdropTemplate")
    tab:SetSize(158, 30)
    tab:SetFrameLevel(parent:GetFrameLevel() + 5)
    ET:StylePanel(tab)
    tab.title = label(tab, title, "GameFontNormal")
    tab.title:SetPoint("CENTER")
    tab.accent = tab:CreateTexture(nil, "ARTWORK")
    tab.accent:SetColorTexture(0.65, 0.48, 0.9, 1)
    tab.accent:SetPoint("TOPLEFT", 1, -1)
    tab.accent:SetSize(156, 3)
    tab.seam = tab:CreateTexture(nil, "OVERLAY")
    tab.seam:SetColorTexture(0.065, 0.075, 0.095, 1)
    tab.seam:SetPoint("BOTTOMLEFT", 1, -1)
    tab.seam:SetSize(156, 3)
    tab:SetHighlightTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight")
    tab:SetScript("OnClick", onClick)
    return tab
end

local function dropdown(parent, width, choices, setting, default)
    local widget = CreateFrame("DropdownButton", nil, parent, "WowStyle1DropdownTemplate")
    widget:SetWidth(width)
    widget:SetupMenu(function(_, root)
        local values = type(choices) == "function" and choices() or choices
        for _, choice in ipairs(values) do
            root:CreateRadio(choice.text,
                function(value) return (ET.db.settings[setting] or default) == value end,
                function(value)
                    ET.db.settings[setting] = value
                    UI:RefreshRoster(true)
                end, choice.value)
        end
    end)
    return widget
end

function UI:CreateCopyFrame()
    if self.copyFrame then return end
    local frame = CreateFrame("Frame", "EvilTwinsCCCopyFrame", UIParent, "BackdropTemplate")
    frame:SetSize(760, 520)
    frame:SetPoint("CENTER")
    frame:SetFrameStrata("DIALOG")
    ET:StyleWindow(frame)
    frame:EnableMouse(true)
    frame:SetMovable(true)
    frame:RegisterForDrag("LeftButton")
    frame:SetScript("OnDragStart", frame.StartMoving)
    frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
    frame:Hide()
    local title = label(frame, "Copy", "GameFontNormalLarge")
    title:SetPoint("TOPLEFT", 22, -20)
    frame.title = title
    local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
    close:SetPoint("TOPRIGHT", -4, -4)
    local help = label(frame, "Press Ctrl+A, then Ctrl+C to copy.", "GameFontHighlightSmall")
    help:SetPoint("TOPLEFT", 22, -47)
    local scroll = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    scroll:SetPoint("TOPLEFT", 22, -72)
    scroll:SetPoint("BOTTOMRIGHT", -38, 24)
    local edit = CreateFrame("EditBox", nil, scroll)
    edit:SetMultiLine(true)
    edit:SetAutoFocus(false)
    edit:SetFontObject(ChatFontNormal)
    edit:SetWidth(680)
    edit:SetHeight(400)
    edit:SetScript("OnEscapePressed", function() frame:Hide() end)
    edit:SetScript("OnTextChanged", function(self)
        local height = self.GetStringHeight and self:GetStringHeight()
        self:SetHeight(math.max(400, (height or math.ceil(#self:GetText() / 80) * 16) + 20))
    end)
    scroll:SetScrollChild(edit)
    frame.edit = edit
    self.copyFrame = frame
end

function UI:ShowCopy(title, text)
    self:CreateCopyFrame()
    self.copyFrame.title:SetText(title)
    self.copyFrame.edit:SetText(text or "")
    self.copyFrame:Show()
    self.copyFrame.edit:SetFocus()
    self.copyFrame.edit:HighlightText()
end

local function prepStatus(count, unknown)
    if count > 0 then return tostring(count) .. " missing" end
    return unknown > 0 and "Unknown" or "OK"
end

local function setPrepStatus(widget, value)
    widget:SetText(value)
    if value == "OK" then
        widget:SetTextColor(0.4, 1, 0.5)
    elseif value == "Unknown" or value == "Stale" or value == "No data" then
        widget:SetTextColor(1, 0.8, 0.35)
    elseif value == "Not set" or value == "—" then
        widget:SetTextColor(0.65, 0.65, 0.65)
    else
        widget:SetTextColor(1, 0.4, 0.4)
    end
end

function UI:CreateOverviewFrame()
    if self.overviewFrame then return end
    local frame = CreateFrame("Frame", "EvilTwinsCCOverviewFrame", UIParent, "BackdropTemplate")
    frame:SetSize(900, 620)
    frame:SetPoint("CENTER")
    frame:SetFrameStrata("DIALOG")
    ET:StyleWindow(frame)
    frame:EnableMouse(true)
    frame:SetMovable(true)
    frame:RegisterForDrag("LeftButton")
    frame:SetScript("OnDragStart", frame.StartMoving)
    frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
    frame:Hide()
    self.overviewFrame = frame
    label(frame, "GUILD RAID PREP", "GameFontNormalLarge"):SetPoint("TOPLEFT", 24, -22)
    local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
    close:SetPoint("TOPRIGHT", -4, -4)
    local refresh = button(frame, "Refresh guild gear", 160, 25, function()
        local ok, err = ET.Comm:RequestGuildGear()
        if not ok then ET:Print(err); return end
        UI.overviewRequestedAt = time()
        UI:RefreshOverview()
        ET:Print("Refreshing gear from online guildmates with the addon.")
    end)
    refresh:SetPoint("TOPRIGHT", -40, -18)
    label(frame, "Saved gear & bags. Supplies: any Midnight option, minimum 1. Click a character for details.",
        "GameFontHighlightSmall"):SetPoint("TOPLEFT", 25, -55)
    self.overviewRefreshStatus = label(frame, "", "GameFontHighlightSmall")
    self.overviewRefreshStatus:SetPoint("TOPLEFT", 25, -74)
    local columns = {
        { "Character", 25 }, { "Gear", 200 }, { "Enchants", 278 },
        { "Gems", 373 }, { "Repairs", 453 }, { "Supplies", 543 },
        { "Captured", 673 },
    }
    for _, column in ipairs(columns) do
        label(frame, column[1], "GameFontNormal"):SetPoint("TOPLEFT", column[2], -91)
    end
    local scroll = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    scroll:SetPoint("TOPLEFT", 23, -119)
    scroll:SetPoint("BOTTOMRIGHT", -39, 26)
    local child = CreateFrame("Frame", nil, scroll)
    child:SetWidth(835)
    child:SetHeight(1)
    scroll:SetScrollChild(child)
    self.overviewChild = child
    self.overviewRows = {}
    frame:RegisterEvent("GUILD_ROSTER_UPDATE")
    frame:SetScript("OnEvent", function() UI:RefreshOverview() end)
end

function UI:RefreshOverview()
    if not self.overviewFrame or not self.overviewFrame:IsShown() then return end
    if not ET:CanUseLeaderTools() then self.overviewFrame:Hide(); return end
    local entries, seen, lookup = {}, {}, {}
    for key, snapshot in pairs(ET.db.characters) do
        lookup[key:lower():gsub("%s+", "")] = { key = key, snapshot = snapshot }
    end
    local guildCount = IsInGuild() and GetNumGuildMembers() or 0
    if guildCount > 0 then
        local realm = GetNormalizedRealmName() or GetRealmName()
        for index = 1, guildCount do
            local name = GetGuildRosterInfo(index)
            if name then
                local fullName = name:find("-", 1, true) and name or (name .. "-" .. realm)
                local normalized = fullName:lower():gsub("%s+", "")
                if not seen[normalized] then
                    local saved = lookup[normalized]
                    entries[#entries + 1] = saved or { key = fullName }
                    seen[normalized] = true
                end
            end
        end
    else
        for _, saved in pairs(lookup) do entries[#entries + 1] = saved end
    end
    table.sort(entries, function(a, b) return a.key:lower() < b.key:lower() end)
    if self.overviewRequestedAt then
        local refreshed = 0
        for _, entry in ipairs(entries) do
            if entry.snapshot and entry.snapshot.updated >= self.overviewRequestedAt then
                refreshed = refreshed + 1
            end
        end
        self.overviewRefreshStatus:SetText("Refresh requested " .. date("%H:%M:%S", self.overviewRequestedAt)
            .. "  •  " .. refreshed .. " fresh snapshot(s) received")
    else
        self.overviewRefreshStatus:SetText("Online members with the addon respond automatically to refresh.")
    end
    for index, entry in ipairs(entries) do
        local row = self.overviewRows[index]
        if not row then
            row = CreateFrame("Button", nil, self.overviewChild)
            row:SetSize(830, 30)
            row.labels = {}
            for column, x in ipairs({ 2, 177, 255, 350, 430, 520, 650 }) do
                row.labels[column] = label(row, "", "GameFontHighlightSmall")
                row.labels[column]:SetPoint("LEFT", x, 0)
                row.labels[column]:SetWidth(({ 165, 70, 87, 72, 82, 120, 170 })[column])
            end
            row:SetScript("OnClick", function(self)
                if not self.snapshot then return end
                local inventory = ET.Inventory:Get(self.key, self.snapshot.guid)
                UI:ShowCopy("Raid prep: " .. self.key, ET.RaidPrep:Report(self.snapshot, inventory))
            end)
            self.overviewRows[index] = row
        end
        row.key, row.snapshot = entry.key, entry.snapshot
        row:SetPoint("TOPLEFT", 0, -(index - 1) * 31)
        row.labels[1]:SetText(entry.key)
        if entry.snapshot then
            local inventory = ET.Inventory:Get(entry.key, entry.snapshot.guid)
            local result = ET.RaidPrep:Analyze(entry.snapshot, inventory)
            setPrepStatus(row.labels[2], prepStatus(result.missingGear, 0))
            setPrepStatus(row.labels[3], prepStatus(result.missingEnchants, result.unknownEnchants))
            setPrepStatus(row.labels[4], prepStatus(result.emptySockets, result.unknownSockets))
            setPrepStatus(row.labels[5], prepStatus(result.repairCount, result.unknownDurability))
            local supplies = result.supplyUnknown and "Unknown"
                or result.bagStale and result.configuredSupplies > 0 and "Stale"
                or #result.supplyIssues > 0 and (#result.supplyIssues .. " short")
                or "OK"
            setPrepStatus(row.labels[6], supplies)
            row.labels[7]:SetText(date("%m/%d %H:%M", entry.snapshot.updated))
        else
            setPrepStatus(row.labels[2], "No data")
            for column = 3, 7 do setPrepStatus(row.labels[column], "—") end
        end
        row:Show()
    end
    for index = #entries + 1, #self.overviewRows do self.overviewRows[index]:Hide() end
    self.overviewChild:SetHeight(math.max(1, #entries * 31))
end

function UI:ShowOverview()
    if not ET:CanUseLeaderTools() then
        ET:Print("Your guild rank does not have access to leader tools.")
        return
    end
    self:CreateOverviewFrame()
    if C_GuildInfo and C_GuildInfo.GuildRoster then C_GuildInfo.GuildRoster() end
    self.overviewFrame:Show()
    self:RefreshOverview()
end

function UI:Create()
    if self.frame then return end
    local frame = CreateFrame("Frame", "EvilTwinsCCFrame", UIParent, "BackdropTemplate")
    frame:SetSize(1050, 720)
    frame:SetPoint("CENTER")
    frame:SetFrameStrata("HIGH")
    ET:StyleWindow(frame)
    frame:EnableMouse(true)
    frame:SetMovable(true)
    frame:RegisterForDrag("LeftButton")
    frame:SetScript("OnDragStart", frame.StartMoving)
    frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
    frame:Hide()
    self.frame = frame

    local title = label(frame, "EVIL TWINS  /  COMMAND CENTER", "GameFontNormalLarge")
    title:SetPoint("TOPLEFT", 23, -20)
    local overview = button(frame, "Guild raid prep", 150, 25, function() UI:ShowOverview() end)
    self.overviewButton = overview
    local crafting = button(frame, "Crafting directory", 160, 25, function() UI:ShowCrafting() end)
    crafting:SetPoint("TOPRIGHT", -44, -18)
    overview:SetPoint("RIGHT", crafting, "LEFT", -10, 0)
    self.craftingButton = crafting
    local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
    close:SetPoint("TOPRIGHT", -4, -4)

    local sidebar = panel(frame, 266, 610)
    sidebar:SetPoint("TOPLEFT", 18, -62)
    self.sidebar = sidebar
    label(sidebar, "CHARACTERS", "GameFontNormal"):SetPoint("TOPLEFT", 14, -14)
    local search = CreateFrame("EditBox", nil, sidebar, "InputBoxTemplate")
    search:SetSize(228, 26)
    search:SetPoint("TOPLEFT", 21, -60)
    search:SetAutoFocus(false)
    search:SetScript("OnEscapePressed", search.ClearFocus)
    search:SetScript("OnTextChanged", function() UI:RefreshRoster(true) end)
    self.search = search
    local searchHint = label(sidebar, "Search name, class or spec", "GameFontHighlightSmall")
    searchHint:SetPoint("BOTTOMLEFT", search, "TOPLEFT", 0, 2)
    self.searchHint = searchHint

    self.visibilityFilter = dropdown(sidebar, 116, {
        { text = "All characters", value = "ALL" }, { text = "Online now", value = "ONLINE" },
        { text = "With wishlist", value = "WISHLIST" },
    }, "rosterVisibility", "ALL")
    self.visibilityFilter:SetPoint("TOPLEFT", 14, -96)
    self.classFilter = dropdown(sidebar, 116, function()
        local choices = { { text = "Any class", value = "ALL" } }
        for _, choice in ipairs(UI.rosterClasses or {}) do choices[#choices + 1] = choice end
        return choices
    end, "rosterClass", "ALL")
    self.classFilter:SetPoint("TOPLEFT", 138, -96)
    label(sidebar, "Sort", "GameFontHighlightSmall"):SetPoint("TOPLEFT", 14, -137)
    self.sortFilter = dropdown(sidebar, 200, {
        { text = "Name A–Z", value = "NAME" }, { text = "Highest item level", value = "ITEM_LEVEL" },
        { text = "Latest snapshot", value = "RECENT" },
    }, "rosterSort", "NAME")
    self.sortFilter:SetPoint("TOPLEFT", 54, -129)
    self.rosterCount = label(sidebar, "", "GameFontHighlightSmall", { 0.65, 0.7, 0.8 })
    self.rosterCount:SetPoint("TOPLEFT", 14, -166)
    self.rosterCount:SetWidth(236)

    local rosterScroll = CreateFrame("ScrollFrame", nil, sidebar, "UIPanelScrollFrameTemplate")
    rosterScroll:SetPoint("TOPLEFT", 8, -190)
    rosterScroll:SetPoint("BOTTOMRIGHT", -28, 14)
    local rosterChild = CreateFrame("Frame", nil, rosterScroll)
    rosterChild:SetWidth(230)
    rosterChild:SetHeight(1)
    rosterScroll:SetScrollChild(rosterChild)
    self.rosterScroll = rosterScroll
    self.rosterChild = rosterChild
    rosterScroll:HookScript("OnVerticalScroll", function() UI:RenderRoster() end)
    self.rosterEmpty = label(sidebar, "No matching characters.", "GameFontHighlightSmall")
    self.rosterEmpty:SetPoint("TOPLEFT", 18, -205)
    self.rosterEmpty:SetWidth(222)

    local detailScroll = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    detailScroll:SetPoint("TOPLEFT", 302, -202)
    detailScroll:SetPoint("BOTTOMRIGHT", -38, 48)
    self.detailScroll = detailScroll
    local detail = CreateFrame("Frame", nil, detailScroll)
    detail:SetWidth(686)
    detail:SetHeight(700)
    detailScroll:SetScrollChild(detail)
    self.detail = detail

    local profilePanel = panel(frame, 686, 88)
    profilePanel:SetPoint("TOPLEFT", 302, -62)
    self.profilePanel = profilePanel

    local heading = label(profilePanel, "Select a character", "GameFontNormalLarge")
    heading:SetPoint("TOPLEFT", 16, -14)
    heading:SetWidth(530)
    self.heading = heading
    self.presence = label(profilePanel, "", "GameFontHighlightSmall")
    self.presence:SetPoint("TOPRIGHT", -16, -18)
    local summary = label(profilePanel, "", "GameFontHighlight")
    summary:SetPoint("TOPLEFT", 16, -44)
    self.summary = summary
    local timestamp = label(profilePanel, "", "GameFontHighlightSmall", { 0.65, 0.7, 0.8 })
    timestamp:SetPoint("TOPLEFT", 16, -65)
    timestamp:SetWidth(650)
    self.timestamp = timestamp

    self.tabPanels, self.tabButtons = {}, {}
    self.activeTab = "prep"
    local tabs = { { "prep", "Raid prep" }, { "build", "Build & SimC" }, { "wishlist", "Wishlist & upgrades" } }
    for index, entry in ipairs(tabs) do
        local key = entry[1]
        local tab = navigationTab(frame, entry[2], function() UI:SetTab(key) end)
        tab:SetPoint("TOPLEFT", 302 + (index - 1) * 162, -173)
        self.tabButtons[key] = tab
    end
    local buildPanel = panel(detail, 686, 196)
    buildPanel:SetPoint("TOPLEFT")
    self.tabPanels.build = buildPanel
    label(buildPanel, "TALENTS & SIMULATIONCRAFT", "GameFontNormal"):SetPoint("TOPLEFT", 16, -14)
    label(buildPanel, "Export talents, equipped gear, and carried gear for a Top Gear comparison.", "GameFontHighlightSmall")
        :SetPoint("TOPLEFT", 16, -42)
    self.buildStatus = label(buildPanel, "", "GameFontHighlightSmall", { 0.65, 0.7, 0.8 })
    self.buildStatus:SetPoint("TOPLEFT", 16, -115)
    self.buildStatus:SetWidth(650)
    label(buildPanel, "Paste the export into Raidbots Top Gear to compare alternatives.", "GameFontHighlightSmall",
        { 0.65, 0.7, 0.8 }):SetPoint("TOPLEFT", 16, -140)
    label(buildPanel, "An item's level alone does not tell us whether it is an upgrade.", "GameFontHighlightSmall",
        { 0.65, 0.7, 0.8 }):SetPoint("TOPLEFT", 16, -160)

    local talentButton = button(buildPanel, "Copy talent build", 160, 26, function()
        local snapshot = ET.db.characters[UI.selected]
        if snapshot and snapshot.talent ~= "" then UI:ShowCopy("Talent build: " .. snapshot.name, snapshot.talent) end
    end)
    talentButton:SetPoint("TOPLEFT", 16, -72)
    self.talentButton = talentButton
    local simcButton = button(buildPanel, "View last SimC", 150, 26, function()
        local snapshot = ET.db.characters[UI.selected]
        if not snapshot then return end
        local entry = ET:GetCachedSimC(UI.selected, snapshot.guid)
        if entry then UI:ShowCopy("SimC: " .. UI.selected .. " (" .. date("%Y-%m-%d %H:%M", entry.updated) .. ")", entry.profile) end
    end)
    simcButton:SetPoint("LEFT", talentButton, "RIGHT", 8, 0)
    self.simcButton = simcButton

    local refreshSimc = button(buildPanel, "Request fresh", 150, 26, function()
        local snapshot = ET.db.characters[UI.selected]
        if not snapshot then return end
        if UI.selected == ET.key then
            local profile, err = ET:GetSimCProfile()
            if profile then
                local saved = ET:StoreSimC(ET.key, snapshot.guid, time(), profile)
                UI:ShowCopy("My SimC profile", profile)
                if saved then ET.Comm:PublishSimC() end
                if not saved then ET:Print("This SimC profile is too large to cache or share.") end
            else ET:Print(err) end
        elseif ET.Comm:RequestSimC(UI.selected, snapshot.guid) then
            ET:Print("Requested a SimC profile from " .. UI.selected .. ". They must be online with Evil Twins Command Center enabled.")
        else
            ET:Print("That character is not currently available for requests.")
        end
    end)
    refreshSimc:SetPoint("LEFT", simcButton, "RIGHT", 8, 0)
    self.refreshSimcButton = refreshSimc

    local prepPanel = panel(detail, 686, 500)
    prepPanel:SetPoint("TOPLEFT")
    self.tabPanels.prep = prepPanel
    local prepButton = button(prepPanel, "Copy report", 130, 26, function()
        local snapshot = ET.db.characters[UI.selected]
        if not snapshot then return end
        local inventory = ET.Inventory:Get(UI.selected, snapshot.guid)
        UI:ShowCopy("Raid prep: " .. UI.selected, ET.RaidPrep:Report(snapshot, inventory))
    end)
    prepButton:SetPoint("TOPRIGHT", -16, -10)
    self.prepButton = prepButton

    label(prepPanel, "ENCHANTS & GEMS", "GameFontNormal"):SetPoint("TOPLEFT", 16, -14)
    self.prepSummary = label(prepPanel, "", "GameFontHighlightSmall")
    self.prepSummary:SetPoint("TOPLEFT", 16, -44)
    self.prepSummary:SetWidth(650)
    for _, column in ipairs({ { "Slot", 16 }, { "Current enchant", 100 }, { "Current gems", 374 } }) do
        label(prepPanel, column[1], "GameFontNormalSmall"):SetPoint("TOPLEFT", column[2], -72)
    end
    self.gearRows = {}
    self.supplyHeading = label(prepPanel, "SUPPLIES", "GameFontNormal")
    self.supplyPolicy = label(prepPanel, "Any Midnight option • minimum 1 in carried bags", "GameFontHighlightSmall",
        { 0.65, 0.7, 0.8 })
    self.supplyRows = {}
    for index, category in ipairs(ET.RaidPrep.categories) do
        local row = CreateFrame("Button", nil, prepPanel)
        row:SetSize(650, 34)
        row.title = label(row, category.label, "GameFontHighlightSmall")
        row.title:SetPoint("LEFT", 0, 0)
        row.title:SetWidth(148)
        row.items = label(row, "", "GameFontHighlightSmall")
        row.items:SetPoint("LEFT", 154, 0)
        row.items:SetWidth(420)
        row.status = label(row, "", "GameFontHighlightSmall")
        row.status:SetPoint("RIGHT", 0, 0)
        row.status:SetWidth(65)
        row:SetScript("OnEnter", function(self)
            if not self.supply then return end
            GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
            GameTooltip:AddLine(self.supply.label .. " — minimum 1")
            for _, item in ipairs(self.supply.items) do GameTooltip:AddLine(item.name .. " x" .. item.count) end
            GameTooltip:Show()
        end)
        row:SetScript("OnLeave", function() GameTooltip:Hide() end)
        self.supplyRows[index] = row
    end

    local wishPanel = panel(detail, 686, 130)
    wishPanel:SetPoint("TOPLEFT")
    self.tabPanels.wishlist = wishPanel
    self.wishPanel = wishPanel
    local wishHeading = label(wishPanel, "GEAR WISHLIST", "GameFontNormal")
    wishHeading:SetPoint("TOPLEFT", 16, -14)
    self.wishHeading = wishHeading
    self.browseLootButton = button(wishPanel, "Browse boss / dungeon loot", 225, 25, function() UI:ShowLootPicker() end)
    self.browseLootButton:SetPoint("TOPRIGHT", -16, -8)
    local emptyWish = label(wishPanel, "No items added yet.", "GameFontHighlightSmall", { 0.65, 0.7, 0.8 })
    emptyWish:SetPoint("TOPLEFT", 16, -98)
    emptyWish:SetWidth(650)
    self.emptyWish = emptyWish

    local itemInput = CreateFrame("EditBox", nil, wishPanel, "InputBoxTemplate")
    itemInput:SetSize(209, 24)
    itemInput:SetPoint("TOPLEFT", 24, -61)
    itemInput:SetAutoFocus(false)
    itemInput:SetScript("OnEscapePressed", itemInput.ClearFocus)
    self.itemInput = itemInput
    local noteInput = CreateFrame("EditBox", nil, wishPanel, "InputBoxTemplate")
    noteInput:SetSize(272, 24)
    noteInput:SetPoint("LEFT", itemInput, "RIGHT", 18, 0)
    noteInput:SetAutoFocus(false)
    noteInput:SetScript("OnEscapePressed", noteInput.ClearFocus)
    self.noteInput = noteInput
    local add = button(wishPanel, "Add item", 105, 25, function()
        local ok, err = ET:AddWish(itemInput:GetText(), noteInput:GetText())
        if not ok then ET:Print(err); return end
        itemInput:SetText("")
        noteInput:SetText("")
        UI:Refresh()
    end)
    add:SetPoint("LEFT", noteInput, "RIGHT", 12, 0)
    self.addButton = add
    local inputHint = label(wishPanel, "Item link or ID", "GameFontHighlightSmall")
    inputHint:SetPoint("BOTTOMLEFT", itemInput, "TOPLEFT", 0, 1)
    self.inputHint = inputHint
    local noteHint = label(wishPanel, "Note (optional)", "GameFontHighlightSmall")
    noteHint:SetPoint("BOTTOMLEFT", noteInput, "TOPLEFT", 0, 1)
    self.noteHint = noteHint

    for index = 1, 20 do
        local row = CreateFrame("Button", nil, wishPanel)
        row:SetSize(650, 28)
        row.item = label(row, "", "GameFontNormalSmall")
        row.item:SetPoint("LEFT", 0, 0)
        row.item:SetWidth(250)
        row.note = label(row, "", "GameFontHighlightSmall")
        row.note:SetPoint("LEFT", 255, 0)
        row.note:SetWidth(310)
        row:SetScript("OnEnter", function(self)
            if not self.itemID then return end
            GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
            GameTooltip:SetHyperlink("item:" .. self.itemID)
            GameTooltip:Show()
        end)
        row:SetScript("OnLeave", function() GameTooltip:Hide() end)
        row:SetScript("OnClick", function(self)
            if self.itemID and IsModifiedClick("CHATLINK") then
                ChatEdit_InsertLink("item:" .. self.itemID)
            end
        end)
        row.remove = button(row, "Remove", 70, 20, function()
            ET:RemoveWish(index)
            UI:Refresh()
        end)
        row.remove:SetPoint("RIGHT", 0, 0)
        self.wishRows[index] = row
    end

    if self.CreateUpgradesPanel then self:CreateUpgradesPanel() end

    local footer = label(frame, "Gear and carried bags sync automatically while the addon is installed.", "GameFontHighlightSmall")
    footer:SetPoint("BOTTOMLEFT", 23, 20)
    self.footer = footer
    local memberMessage = label(frame,
        "Gear, carried bags, and SimC profiles sync automatically to online leaders.",
        "GameFontHighlightSmall")
    memberMessage:SetPoint("TOPLEFT", 24, -64)
    local memberRank = label(frame, "Crafting directory is open to all guild ranks. Gear and raid prep are for ranks 0–5.", "GameFontHighlightSmall")
    memberRank:SetPoint("TOPLEFT", 24, -88)
    self.memberRank = memberRank
    self.memberMessage = memberMessage
    frame:SetScript("OnShow", function() UI:Refresh() end)
    frame:RegisterEvent("GET_ITEM_INFO_RECEIVED")
    frame:SetScript("OnEvent", function()
        if frame:IsShown() and ET:CanUseLeaderTools() then UI:RefreshDetail() end
    end)
end

function UI:SetTab(key)
    if not ET:CanUseLeaderTools() or not self.tabPanels[key] then return end
    self.activeTab = key
    self.detailScroll:SetVerticalScroll(0)
    self:RefreshDetail()
end

function UI:RefreshTabs()
    for key, widget in pairs(self.tabPanels) do
        widget:SetShown(key == self.activeTab)
        local tab, selected = self.tabButtons[key], key == self.activeTab
        tab.selected = selected
        tab.accent:SetShown(selected)
        tab.seam:SetShown(selected)
        tab:SetBackdropColor(selected and 0.065 or 0.035, selected and 0.075 or 0.04, selected and 0.095 or 0.055, 1)
        tab.title:SetTextColor(selected and 1 or 0.65, selected and 0.82 or 0.7, selected and 0.4 or 0.8)
    end
    self.detail:SetHeight(self.tabPanels[self.activeTab]:GetHeight() + 12)
end

local statusColors = { ok = { 0.4, 1, 0.5 }, missing = { 1, 0.4, 0.4 },
    unknown = { 1, 0.8, 0.35 }, stale = { 1, 0.8, 0.35 }, none = { 0.65, 0.7, 0.8 } }
local function readinessText(widget, text, status)
    widget:SetText(text)
    widget:SetTextColor(unpack(statusColors[status] or statusColors.unknown))
end

function UI:RefreshPrep(snapshot, inventory)
    local result = ET.RaidPrep:Analyze(snapshot, inventory)
    self.prepSummary:SetText("Enchants: " .. prepStatus(result.missingEnchants, result.unknownEnchants)
        .. "  •  Gems: " .. prepStatus(result.emptySockets, result.unknownSockets)
        .. "  •  Repairs: " .. prepStatus(result.repairCount, result.unknownDurability)
        .. "  •  Gear: " .. prepStatus(result.missingGear, 0))
    local entries = ET.RaidPrep:GearDetails(snapshot)
    for index, entry in ipairs(entries) do
        local row = self.gearRows[index]
        if not row then
            row = CreateFrame("Button", nil, self.tabPanels.prep)
            row:SetSize(650, 36)
            local background = row:CreateTexture(nil, "BACKGROUND")
            background:SetAllPoints()
            background:SetColorTexture(1, 1, 1, index % 2 == 0 and 0.035 or 0)
            row.slot = label(row, "", "GameFontHighlightSmall")
            row.slot:SetPoint("LEFT", 0, 0)
            row.slot:SetWidth(78)
            row.enchant = label(row, "", "GameFontHighlightSmall")
            row.enchant:SetPoint("LEFT", 84, 0)
            row.enchant:SetWidth(264)
            row.gems = label(row, "", "GameFontHighlightSmall")
            row.gems:SetPoint("LEFT", 358, 0)
            row.gems:SetWidth(292)
            for _, cell in ipairs({ row.enchant, row.gems }) do
                cell:SetWordWrap(true)
                cell:SetMaxLines(2)
                cell:SetHeight(32)
            end
            row:SetScript("OnEnter", function(self)
                if not self.entry then return end
                GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
                if self.entry.link ~= "" then GameTooltip:SetHyperlink(self.entry.link)
                else GameTooltip:AddLine(self.entry.slot) end
                GameTooltip:AddLine("Enchant: " .. self.entry.enchantText, 0.8, 0.85, 0.95, true)
                GameTooltip:AddLine("Gems: " .. self.entry.gemText, 0.8, 0.85, 0.95, true)
                GameTooltip:Show()
            end)
            row:SetScript("OnLeave", function() GameTooltip:Hide() end)
            row:SetScript("OnClick", function(self)
                if self.entry.link ~= "" and IsModifiedClick("CHATLINK") then ChatEdit_InsertLink(self.entry.link) end
            end)
            self.gearRows[index] = row
        end
        row.entry = entry
        row:ClearAllPoints()
        row:SetPoint("TOPLEFT", 16, -94 - (index - 1) * 38)
        row.slot:SetText(entry.slot)
        readinessText(row.enchant, entry.enchantText, entry.enchantStatus)
        readinessText(row.gems, entry.gemText, entry.gemStatus)
        row:Show()
    end
    for index = #entries + 1, #self.gearRows do self.gearRows[index]:Hide() end
    local supplyY = 110 + #entries * 38
    self.supplyHeading:ClearAllPoints()
    self.supplyHeading:SetPoint("TOPLEFT", 16, -supplyY)
    self.supplyPolicy:ClearAllPoints()
    self.supplyPolicy:SetPoint("TOPLEFT", 16, -supplyY - 25)
    for index, supply in ipairs(result.supplies) do
        local row, names = self.supplyRows[index], {}
        row.supply = supply
        for _, item in ipairs(supply.items) do names[#names + 1] = item.name .. " x" .. item.count end
        row:ClearAllPoints()
        row:SetPoint("TOPLEFT", 16, -supplyY - 50 - (index - 1) * 36)
        row.items:SetText(#names > 0 and table.concat(names, ", ")
            or supply.status == "unknown" and "Bag data unavailable" or "None in saved bags")
        local text = supply.status == "unknown" and "Unknown" or supply.status == "stale" and "Stale"
            or supply.count > 0 and (supply.count .. "/1") or "Missing"
        readinessText(row.status, text, supply.status)
    end
    self.tabPanels.prep:SetHeight(supplyY + 50 + #result.supplies * 36 + 16)
end

function UI:RenderRoster()
    if not self.rosterEntries then return end
    local entries, rowHeight = self.rosterEntries, 46
    local first, last = ET.Roster:VisibleRange(#entries, self.rosterScroll:GetVerticalScroll(),
        self.rosterScroll:GetHeight(), rowHeight)
    local used = 0
    for index = first, last do
        used = used + 1
        local row = self.rosterRows[used]
        if not row then
            row = CreateFrame("Button", nil, self.rosterChild)
            row:SetSize(230, 44)
            row.background = row:CreateTexture(nil, "BACKGROUND")
            row.background:SetAllPoints()
            row:SetHighlightTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight")
            row.name = label(row, "", "GameFontNormal")
            row.name:SetPoint("TOPLEFT", 10, -7)
            row.name:SetWidth(198)
            row.dot = row:CreateTexture(nil, "ARTWORK")
            row.dot:SetSize(5, 5)
            row.dot:SetPoint("TOPRIGHT", -8, -11)
            row.description = label(row, "", "GameFontHighlightSmall", { 0.65, 0.7, 0.8 })
            row.description:SetPoint("TOPLEFT", 10, -25)
            row.description:SetWidth(210)
            row:SetScript("OnClick", function(self)
                if UI.selected ~= self.key then UI.detailScroll:SetVerticalScroll(0) end
                UI.selected = self.key
                UI:Refresh()
            end)
            row:SetScript("OnEnter", function(self)
                GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
                GameTooltip:AddLine(self.key)
                GameTooltip:AddLine(self.online == true and "Online now"
                    or self.online == false and "Offline — showing saved data" or "Online status unavailable", 0.7, 0.75, 0.85)
                GameTooltip:Show()
            end)
            row:SetScript("OnLeave", function() GameTooltip:Hide() end)
            self.rosterRows[used] = row
        end
        local entry = entries[index]
        local key, snapshot = entry.key, entry.snapshot
        row.key, row.online = key, entry.online
        row:ClearAllPoints()
        row:SetPoint("TOPLEFT", 0, -((index - 1) * rowHeight))
        row.name:SetText(snapshot.name .. (key == ET.key and " (you)" or ""))
        row.description:SetText((snapshot.spec or "") .. "  •  " .. tostring(snapshot.itemLevel or 0) .. " ilvl")
        local color = RAID_CLASS_COLORS and RAID_CLASS_COLORS[snapshot.class]
        row.name:SetTextColor(color and color.r or 0.8, color and color.g or 0.75, color and color.b or 1)
        row.background:SetColorTexture(0.35, 0.26, 0.55, key == self.selected and 0.65 or (index % 2 == 0 and 0.08 or 0))
        if entry.online == true then row.dot:SetColorTexture(0.35, 0.9, 0.45, 1)
        elseif entry.online == false then row.dot:SetColorTexture(0.4, 0.45, 0.52, 1)
        else row.dot:SetColorTexture(0.85, 0.65, 0.3, 1) end
        row:Show()
    end
    for index = used + 1, #self.rosterRows do self.rosterRows[index]:Hide() end
end

function UI:RefreshRoster(resetScroll)
    if not self.frame then return end
    local settings = ET.db.settings
    local entries, total, classes = ET.Roster:Build(self.search:GetText(), settings.rosterVisibility,
        settings.rosterClass, settings.rosterSort)
    self.rosterEntries, self.rosterClasses = entries, classes
    self.classFilter:GenerateMenu()
    self.rosterCount:SetText(#entries .. " matching  /  " .. total .. " shared characters")
    self.rosterEmpty:SetShown(#entries == 0)
    if not self.selected or not ET.db.characters[self.selected] then self.selected = ET.key end
    local height = math.max(1, #entries * 46)
    self.rosterChild:SetHeight(height)
    local offset = resetScroll and 0 or self.rosterScroll:GetVerticalScroll()
    self.rosterScroll:SetVerticalScroll(math.min(offset, math.max(0, height - self.rosterScroll:GetHeight())))
    self:RenderRoster()
end

function UI:RefreshDetail()
    if not self.frame then return end
    if self.selected ~= ET.key and not ET:CanUseLeaderTools() then self.selected = ET.key end
    local snapshot = ET.db.characters[self.selected]
    if not snapshot then return end
    local isSelf = self.selected == ET.key
    self.heading:SetText(snapshot.name .. "-" .. snapshot.realm)
    local online = ET.Roster:Presence()[self.selected:lower():gsub("%s+", "")]
    self.presence:SetText(online == true and "Online" or online == false and "Offline" or "Status unknown")
    self.presence:SetTextColor(online == true and 0.35 or 0.65, online == true and 0.9 or 0.7, online == true and 0.45 or 0.8)
    self.summary:SetText((snapshot.spec or "") .. "  •  Level " .. snapshot.level .. "  •  " .. snapshot.itemLevel .. " equipped ilvl")
    self.talentButton:SetEnabled(snapshot.talent ~= "")
    local cached = ET:GetCachedSimC(self.selected, snapshot.guid)
    self.simcButton:SetEnabled(cached ~= nil)
    self.timestamp:SetText("Snapshot: " .. date("%Y-%m-%d %H:%M", snapshot.updated)
        .. (cached and ("    SimC: " .. date("%Y-%m-%d %H:%M", cached.updated)) or "    SimC: none cached"))
    self.refreshSimcButton:SetText(isSelf and "Generate SimC" or "Request fresh")
    self.buildStatus:SetText(isSelf and "Generate a new export including gear in your current bags."
        or "Request fresh needs this character online with the addon enabled.")
    local inventory = ET.Inventory:Get(self.selected, snapshot.guid)
    self:RefreshPrep(snapshot, inventory)
    self.browseLootButton:SetShown(isSelf)
    self.itemInput:SetShown(isSelf)
    self.noteInput:SetShown(isSelf)
    self.addButton:SetShown(isSelf)
    self.inputHint:SetShown(isSelf)
    self.noteHint:SetShown(isSelf)
    local offset = self.RefreshUpgrades and self:RefreshUpgrades(snapshot) or 0
    self.wishHeading:ClearAllPoints()
    self.wishHeading:SetPoint("TOPLEFT", 16, -offset - 14)
    self.browseLootButton:ClearAllPoints()
    self.browseLootButton:SetPoint("TOPRIGHT", -16, -offset - 8)
    self.itemInput:ClearAllPoints()
    self.itemInput:SetPoint("TOPLEFT", 24, -offset - 61)
    local wishlist = snapshot.wishlist or {}
    self.emptyWish:ClearAllPoints()
    self.emptyWish:SetPoint("TOPLEFT", 16, -offset - (isSelf and 98 or 46))
    self.emptyWish:SetShown(#wishlist == 0)
    for index, row in ipairs(self.wishRows) do
        local wish = wishlist[index]
        if wish then
            row.itemID = wish.id
            local itemName = GetItemInfo(wish.id)
            row.item:SetText(itemName or ("Item #" .. wish.id))
            row.note:SetText(wish.note or "")
            row.remove:SetShown(isSelf)
            row:ClearAllPoints()
            row:SetPoint("TOPLEFT", 16, -offset - (isSelf and 98 or 46) - (index - 1) * 30)
            row:Show()
        else
            row:Hide()
        end
    end
    local wishHeight = offset + (isSelf and 98 or 46) + math.max(1, #wishlist) * 30 + 16
    self.wishPanel:SetHeight(wishHeight)
    self:RefreshTabs()
end

function UI:Refresh()
    if not self.frame then return end
    local isLeader = ET:CanUseLeaderTools()
    self.overviewButton:SetShown(isLeader)
    self.frame:SetWidth(isLeader and 1050 or 650)
    self.frame:SetHeight(isLeader and 720 or 135)
    self.sidebar:SetShown(isLeader)
    self.profilePanel:SetShown(isLeader)
    for _, tab in pairs(self.tabButtons) do tab:SetShown(isLeader) end
    self.detailScroll:SetShown(isLeader)
    self.footer:SetShown(isLeader)
    self.memberMessage:SetShown(not isLeader)
    self.memberRank:SetShown(not isLeader)
    if not isLeader then
        if self.overviewFrame then self.overviewFrame:Hide() end
        if self.lootFrame then self.lootFrame:Hide() end
        if self.upgradeImport then self.upgradeImport:Hide() end
        if self.selected ~= ET.key and self.copyFrame then self.copyFrame:Hide() end
    end
    self:RefreshRoster()
    self:RefreshDetail()
    self:RefreshOverview()
    self:RefreshCrafting()
end

function UI:Toggle()
    self:Create()
    if self.frame:IsShown() then self.frame:Hide() else self.frame:Show() end
end
