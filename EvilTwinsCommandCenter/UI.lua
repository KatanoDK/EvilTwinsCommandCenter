local _, ET = ...
local UI = { rosterRows = {}, gearRows = {}, wishRows = {} }
ET.UI = UI

local function label(parent, text, size, color)
    local widget = parent:CreateFontString(nil, "OVERLAY", size or "GameFontNormal")
    widget:SetJustifyH("LEFT")
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

function UI:CreateCopyFrame()
    if self.copyFrame then return end
    local frame = CreateFrame("Frame", "EvilTwinsCCCopyFrame", UIParent, "BackdropTemplate")
    frame:SetSize(760, 520)
    frame:SetPoint("CENTER")
    frame:SetFrameStrata("DIALOG")
    frame:SetBackdrop({
        bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
        edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
        edgeSize = 24,
        insets = { left = 6, right = 6, top = 6, bottom = 6 },
    })
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

function UI:CreatePrepFrame()
    if self.prepFrame then return end
    local frame = CreateFrame("Frame", "EvilTwinsCCPrepFrame", UIParent, "BackdropTemplate")
    frame:SetSize(850, 510)
    frame:SetPoint("CENTER")
    frame:SetFrameStrata("DIALOG")
    frame:SetBackdrop({
        bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
        edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
        edgeSize = 24,
        insets = { left = 6, right = 6, top = 6, bottom = 6 },
    })
    frame:EnableMouse(true)
    frame:SetMovable(true)
    frame:RegisterForDrag("LeftButton")
    frame:SetScript("OnDragStart", frame.StartMoving)
    frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
    frame:Hide()
    label(frame, "RAID PREP SUPPLY SETTINGS", "GameFontNormalLarge"):SetPoint("TOPLEFT", 24, -22)
    local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
    close:SetPoint("TOPRIGHT", -4, -4)
    label(frame, "Saved on this officer's installation.", "GameFontHighlightSmall"):SetPoint("TOPLEFT", 25, -52)
    label(frame, "Acceptable item IDs are alternatives; the minimum counts all matching items in carried bags.",
        "GameFontHighlightSmall"):SetPoint("TOPLEFT", 25, -78)
    label(frame, "Supply", "GameFontNormal"):SetPoint("TOPLEFT", 25, -109)
    label(frame, "Acceptable item IDs, separated by commas", "GameFontNormal"):SetPoint("TOPLEFT", 225, -109)
    label(frame, "Minimum", "GameFontNormal"):SetPoint("TOPLEFT", 725, -109)
    self.prepFields = {}
    for index, category in ipairs(ET.RaidPrep.categories) do
        local y = -138 - (index - 1) * 52
        label(frame, category.label, "GameFontHighlight"):SetPoint("TOPLEFT", 25, y - 6)
        local ids = CreateFrame("EditBox", nil, frame, "InputBoxTemplate")
        ids:SetSize(460, 25)
        ids:SetPoint("TOPLEFT", 233, y)
        ids:SetAutoFocus(false)
        ids:SetScript("OnEscapePressed", ids.ClearFocus)
        local minimum = CreateFrame("EditBox", nil, frame, "InputBoxTemplate")
        minimum:SetSize(65, 25)
        minimum:SetPoint("TOPLEFT", 734, y)
        minimum:SetAutoFocus(false)
        minimum:SetScript("OnEscapePressed", minimum.ClearFocus)
        self.prepFields[category.key] = { ids = ids, minimum = minimum }
    end
    local save = button(frame, "Save settings", 150, 27, function()
        local values = {}
        for _, category in ipairs(ET.RaidPrep.categories) do
            local row = UI.prepFields[category.key]
            values[category.key] = { ids = row.ids:GetText(), minimum = row.minimum:GetText() }
        end
        local ok, err = ET.RaidPrep:SaveChecklist(values)
        if not ok then ET:Print(err); return end
        frame:Hide()
        ET:Print("Raid prep supply settings saved.")
    end)
    save:SetPoint("BOTTOMRIGHT", -25, 24)
    self.prepFrame = frame
end

function UI:ShowPrepSettings()
    if not ET:CanUseLeaderTools() then return end
    self:CreatePrepFrame()
    for _, category in ipairs(ET.RaidPrep.categories) do
        local entry = ET.db.prepChecklist[category.key]
        local row = self.prepFields[category.key]
        row.ids:SetText(table.concat(entry.ids, ", "))
        row.minimum:SetText(tostring(entry.minimum))
    end
    self.prepFrame:Show()
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
    frame:SetBackdrop({
        bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
        edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
        edgeSize = 24,
        insets = { left = 6, right = 6, top = 6, bottom = 6 },
    })
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
    local settings = button(frame, "Supply settings", 140, 25, function() UI:ShowPrepSettings() end)
    settings:SetPoint("TOPRIGHT", -40, -18)
    local refresh = button(frame, "Refresh guild gear", 160, 25, function()
        local ok, err = ET.Comm:RequestGuildGear()
        if not ok then ET:Print(err); return end
        UI.overviewRequestedAt = time()
        UI:RefreshOverview()
        ET:Print("Refreshing gear from online guildmates with the addon.")
    end)
    refresh:SetPoint("RIGHT", settings, "LEFT", -10, 0)
    label(frame, "Last shared state. Click a character for the exact missing slots and items.",
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
                or result.configuredSupplies == 0 and "Not set"
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
    frame:SetSize(990, 680)
    frame:SetPoint("CENTER")
    frame:SetFrameStrata("HIGH")
    frame:SetBackdrop({
        bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
        edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
        edgeSize = 24,
        insets = { left = 6, right = 6, top = 6, bottom = 6 },
    })
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
    overview:SetPoint("TOPLEFT", 340, -18)
    self.overviewButton = overview
    local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
    close:SetPoint("TOPRIGHT", -4, -4)

    local search = CreateFrame("EditBox", nil, frame, "InputBoxTemplate")
    search:SetSize(236, 26)
    search:SetPoint("TOPLEFT", 29, -65)
    search:SetAutoFocus(false)
    search:SetScript("OnEscapePressed", search.ClearFocus)
    search:SetScript("OnTextChanged", function() UI:RefreshRoster() end)
    self.search = search
    local searchHint = label(frame, "Search characters", "GameFontHighlightSmall")
    searchHint:SetPoint("BOTTOMLEFT", search, "TOPLEFT", 0, 2)
    self.searchHint = searchHint

    local rosterScroll = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    rosterScroll:SetPoint("TOPLEFT", 23, -106)
    rosterScroll:SetPoint("BOTTOMLEFT", 23, 43)
    rosterScroll:SetWidth(256)
    local rosterChild = CreateFrame("Frame", nil, rosterScroll)
    rosterChild:SetWidth(236)
    rosterChild:SetHeight(1)
    rosterScroll:SetScrollChild(rosterChild)
    self.rosterScroll = rosterScroll
    self.rosterChild = rosterChild

    local divider = frame:CreateTexture(nil, "ARTWORK")
    divider:SetColorTexture(0.42, 0.35, 0.58, 0.7)
    divider:SetSize(1, 555)
    divider:SetPoint("TOPLEFT", 291, -53)
    self.divider = divider

    local detailScroll = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    detailScroll:SetPoint("TOPLEFT", 307, -55)
    detailScroll:SetPoint("BOTTOMRIGHT", -34, 44)
    self.detailScroll = detailScroll
    local detail = CreateFrame("Frame", nil, detailScroll)
    detail:SetWidth(636)
    detail:SetHeight(550)
    detailScroll:SetScrollChild(detail)
    self.detail = detail

    local heading = label(detail, "Select a character", "GameFontNormalLarge")
    heading:SetPoint("TOPLEFT", 6, -6)
    self.heading = heading
    local summary = label(detail, "", "GameFontHighlight")
    summary:SetPoint("TOPLEFT", 6, -35)
    self.summary = summary
    local timestamp = label(detail, "", "GameFontHighlightSmall")
    timestamp:SetPoint("TOPLEFT", 6, -55)
    self.timestamp = timestamp

    local talentButton = button(detail, "Copy talent build", 150, 25, function()
        local snapshot = ET.db.characters[UI.selected]
        if snapshot and snapshot.talent ~= "" then UI:ShowCopy("Talent build: " .. snapshot.name, snapshot.talent) end
    end)
    talentButton:SetPoint("TOPLEFT", 6, -77)
    self.talentButton = talentButton
    local simcButton = button(detail, "View last SimC", 140, 25, function()
        local snapshot = ET.db.characters[UI.selected]
        if not snapshot then return end
        local entry = ET:GetCachedSimC(UI.selected, snapshot.guid)
        if entry then UI:ShowCopy("SimC: " .. UI.selected .. " (" .. date("%Y-%m-%d %H:%M", entry.updated) .. ")", entry.profile) end
    end)
    simcButton:SetPoint("LEFT", talentButton, "RIGHT", 10, 0)
    self.simcButton = simcButton

    local refreshSimc = button(detail, "Request fresh", 140, 25, function()
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
            ET:Print("Requested a SimC profile from " .. UI.selected .. ". They must be online with SimulationCraft installed.")
        else
            ET:Print("That character is not currently available for requests.")
        end
    end)
    refreshSimc:SetPoint("LEFT", simcButton, "RIGHT", 10, 0)
    self.refreshSimcButton = refreshSimc

    local bagButton = button(detail, "View bags", 125, 25, function()
        local snapshot = ET.db.characters[UI.selected]
        if not snapshot then return end
        local content = ET.Inventory:Format(UI.selected, snapshot.guid)
        if content then UI:ShowCopy("Bags: " .. UI.selected, content) end
    end)
    bagButton:SetPoint("LEFT", refreshSimc, "RIGHT", 10, 0)
    self.bagButton = bagButton

    local prepButton = button(detail, "Raid prep report", 150, 25, function()
        local snapshot = ET.db.characters[UI.selected]
        if not snapshot then return end
        local inventory = ET.Inventory:Get(UI.selected, snapshot.guid)
        UI:ShowCopy("Raid prep: " .. UI.selected, ET.RaidPrep:Report(snapshot, inventory))
    end)
    prepButton:SetPoint("TOPLEFT", 6, -111)
    self.prepButton = prepButton
    local prepSettings = button(detail, "Prep settings", 130, 25, function() UI:ShowPrepSettings() end)
    prepSettings:SetPoint("LEFT", prepButton, "RIGHT", 10, 0)
    self.prepSettingsButton = prepSettings

    local gearHeading = label(detail, "EQUIPPED GEAR", "GameFontNormal")
    gearHeading:SetPoint("TOPLEFT", 6, -157)
    for index, slot in ipairs(ET.slots) do
        local column = (index - 1) >= 10 and 1 or 0
        local rowIndex = column == 0 and index - 1 or index - 11
        local row = CreateFrame("Button", nil, detail)
        row:SetSize(305, 25)
        row:SetPoint("TOPLEFT", 6 + column * 312, -183 - rowIndex * 26)
        row.slot = label(row, slot[2], "GameFontHighlightSmall")
        row.slot:SetPoint("LEFT", 0, 0)
        row.slot:SetWidth(82)
        row.item = label(row, "", "GameFontNormalSmall")
        row.item:SetPoint("LEFT", 83, 0)
        row.item:SetWidth(220)
        row:SetScript("OnEnter", function(self)
            if not self.link or self.link == "" then return end
            GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
            GameTooltip:SetHyperlink(self.link)
            GameTooltip:Show()
        end)
        row:SetScript("OnLeave", function() GameTooltip:Hide() end)
        row:SetScript("OnClick", function(self)
            if self.link and self.link ~= "" and IsModifiedClick("CHATLINK") then
                ChatEdit_InsertLink(self.link)
            end
        end)
        self.gearRows[index] = row
    end

    local wishHeading = label(detail, "GEAR WISHLIST", "GameFontNormal")
    wishHeading:SetPoint("TOPLEFT", 6, -463)
    local emptyWish = label(detail, "No items added yet.", "GameFontHighlightSmall")
    emptyWish:SetPoint("TOPLEFT", 6, -496)
    self.emptyWish = emptyWish

    local itemInput = CreateFrame("EditBox", nil, detail, "InputBoxTemplate")
    itemInput:SetSize(188, 24)
    itemInput:SetPoint("TOPLEFT", 14, -492)
    itemInput:SetAutoFocus(false)
    itemInput:SetScript("OnEscapePressed", itemInput.ClearFocus)
    self.itemInput = itemInput
    local noteInput = CreateFrame("EditBox", nil, detail, "InputBoxTemplate")
    noteInput:SetSize(245, 24)
    noteInput:SetPoint("LEFT", itemInput, "RIGHT", 18, 0)
    noteInput:SetAutoFocus(false)
    noteInput:SetScript("OnEscapePressed", noteInput.ClearFocus)
    self.noteInput = noteInput
    local add = button(detail, "Add item", 90, 25, function()
        local ok, err = ET:AddWish(itemInput:GetText(), noteInput:GetText())
        if not ok then ET:Print(err); return end
        itemInput:SetText("")
        noteInput:SetText("")
        UI:Refresh()
    end)
    add:SetPoint("LEFT", noteInput, "RIGHT", 12, 0)
    self.addButton = add
    local inputHint = label(detail, "Item link or ID", "GameFontHighlightSmall")
    inputHint:SetPoint("BOTTOMLEFT", itemInput, "TOPLEFT", 0, 1)
    self.inputHint = inputHint

    for index = 1, 20 do
        local row = CreateFrame("Button", nil, detail)
        row:SetSize(605, 24)
        row.item = label(row, "", "GameFontNormalSmall")
        row.item:SetPoint("LEFT", 0, 0)
        row.item:SetWidth(250)
        row.note = label(row, "", "GameFontHighlightSmall")
        row.note:SetPoint("LEFT", 255, 0)
        row.note:SetWidth(265)
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

    local footer = label(frame, "Gear and carried bags sync automatically while the addon is installed.", "GameFontHighlightSmall")
    footer:SetPoint("BOTTOMLEFT", 23, 20)
    self.footer = footer
    local memberMessage = label(frame,
        "Gear and carried bags sync automatically to online leaders. SimC syncs if installed.",
        "GameFontHighlightSmall")
    memberMessage:SetPoint("TOPLEFT", 24, -64)
    local memberRank = label(frame, "Guild browsing and raid prep are for ranks 0–5.", "GameFontHighlightSmall")
    memberRank:SetPoint("TOPLEFT", 24, -88)
    self.memberRank = memberRank
    self.memberMessage = memberMessage
    frame:SetScript("OnShow", function() UI:Refresh() end)
end

function UI:RefreshRoster()
    if not self.frame then return end
    local search = self.search:GetText():lower()
    local keys = {}
    for key, snapshot in pairs(ET.db.characters) do
        if type(snapshot) == "table" and key:lower():find(search, 1, true)
            and (key == ET.key or ET:CanUseLeaderTools()) then
            keys[#keys + 1] = key
        end
    end
    table.sort(keys, function(a, b) return a:lower() < b:lower() end)
    if not self.selected or not ET.db.characters[self.selected] then self.selected = ET.key end
    for index, key in ipairs(keys) do
        local row = self.rosterRows[index]
        if not row then
            row = CreateFrame("Button", nil, self.rosterChild)
            row:SetSize(232, 36)
            row.name = label(row, "", "GameFontNormal")
            row.name:SetPoint("TOPLEFT", 5, -3)
            row.name:SetWidth(220)
            row.description = label(row, "", "GameFontHighlightSmall")
            row.description:SetPoint("TOPLEFT", 5, -20)
            row.description:SetWidth(220)
            row:SetScript("OnClick", function(self)
                UI.selected = self.key
                UI:Refresh()
            end)
            self.rosterRows[index] = row
        end
        local snapshot = ET.db.characters[key]
        row.key = key
        row:SetPoint("TOPLEFT", 0, -((index - 1) * 38))
        row.name:SetText(snapshot.name .. (key == ET.key and "  (you)" or ""))
        row.description:SetText((snapshot.spec or "") .. "  •  " .. tostring(snapshot.itemLevel or 0) .. " ilvl")
        row.name:SetTextColor(key == self.selected and 1 or 0.75, key == self.selected and 0.8 or 0.7, 1)
        row:Show()
    end
    for index = #keys + 1, #self.rosterRows do self.rosterRows[index]:Hide() end
    self.rosterChild:SetHeight(math.max(1, #keys * 38))
end

function UI:RefreshDetail()
    if not self.frame then return end
    if self.selected ~= ET.key and not ET:CanUseLeaderTools() then self.selected = ET.key end
    local snapshot = ET.db.characters[self.selected]
    if not snapshot then return end
    local isSelf = self.selected == ET.key
    self.heading:SetText(snapshot.name .. "-" .. snapshot.realm)
    self.summary:SetText((snapshot.spec or "") .. "  •  Level " .. snapshot.level .. "  •  " .. snapshot.itemLevel .. " equipped ilvl")
    self.talentButton:SetEnabled(snapshot.talent ~= "")
    local cached = ET:GetCachedSimC(self.selected, snapshot.guid)
    self.simcButton:SetEnabled(cached ~= nil)
    self.timestamp:SetText("Snapshot: " .. date("%Y-%m-%d %H:%M", snapshot.updated)
        .. (cached and ("    SimC: " .. date("%Y-%m-%d %H:%M", cached.updated)) or "    SimC: none cached"))
    self.refreshSimcButton:SetText(isSelf and "Generate SimC" or "Request fresh")
    self.bagButton:SetEnabled(ET.Inventory:Get(self.selected, snapshot.guid) ~= nil)
    for index, row in ipairs(self.gearRows) do
        local link = snapshot.gear[index]
        row.link = link
        if link and link ~= "" then
            local itemName = link:match("%[(.-)%]") or "Item"
            row.item:SetText(itemName)
        else
            row.item:SetText("—")
        end
    end
    self.itemInput:SetShown(isSelf)
    self.noteInput:SetShown(isSelf)
    self.addButton:SetShown(isSelf)
    self.inputHint:SetShown(isSelf)
    local wishlist = snapshot.wishlist or {}
    self.emptyWish:SetShown(#wishlist == 0)
    for index, row in ipairs(self.wishRows) do
        local wish = wishlist[index]
        if wish then
            row.itemID = wish.id
            local itemName = GetItemInfo(wish.id)
            row.item:SetText(itemName or ("Item #" .. wish.id))
            row.note:SetText(wish.note or "")
            row.remove:SetShown(isSelf)
            row:SetPoint("TOPLEFT", 6, -(isSelf and 528 or 496) - (index - 1) * 25)
            row:Show()
        else
            row:Hide()
        end
    end
    self.detail:SetHeight(math.max(590, (isSelf and 548 or 516) + #wishlist * 25))
end

function UI:Refresh()
    if not self.frame then return end
    local isLeader = ET:CanUseLeaderTools()
    self.overviewButton:SetShown(isLeader)
    self.prepSettingsButton:SetShown(isLeader)
    self.frame:SetWidth(isLeader and 990 or 650)
    self.frame:SetHeight(isLeader and 680 or 135)
    self.search:SetShown(isLeader)
    self.searchHint:SetShown(isLeader)
    self.rosterScroll:SetShown(isLeader)
    self.divider:SetShown(isLeader)
    self.detailScroll:SetShown(isLeader)
    self.footer:SetShown(isLeader)
    self.memberMessage:SetShown(not isLeader)
    self.memberRank:SetShown(not isLeader)
    if not isLeader then
        if self.overviewFrame then self.overviewFrame:Hide() end
        if self.prepFrame then self.prepFrame:Hide() end
        if self.selected ~= ET.key and self.copyFrame then self.copyFrame:Hide() end
    end
    self:RefreshRoster()
    self:RefreshDetail()
    self:RefreshOverview()
end

function UI:Toggle()
    self:Create()
    if self.frame:IsShown() then self.frame:Hide() else self.frame:Show() end
end
