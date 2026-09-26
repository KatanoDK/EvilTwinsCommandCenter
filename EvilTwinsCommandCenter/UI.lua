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
    local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
    close:SetPoint("TOPRIGHT", -4, -4)

    local share = CreateFrame("CheckButton", nil, frame, "UICheckButtonTemplate")
    share:SetPoint("TOPLEFT", 22, -52)
    share.Text:SetText("Share my gear, build, and wishlist")
    share:SetChecked(ET.db.settings.shareProfile)
    share:SetScript("OnClick", function(self)
        local enabled = self:GetChecked() and true or false
        ET.db.settings.shareProfile = enabled
        if enabled then
            ET.Comm:Publish(ET:CaptureSelf())
        else
            ET.db.settings.shareSimC = false
            ET.db.settings.shareInventory = false
            ET.db.simcProfiles[ET.key] = nil
            ET.db.inventory[ET.key] = nil
            UI.simcCheck:SetChecked(false)
            UI.bagCheck:SetChecked(false)
            ET.Comm:Revoke()
        end
        UI:Refresh()
    end)
    self.shareCheck = share

    local simc = CreateFrame("CheckButton", nil, frame, "UICheckButtonTemplate")
    simc:SetPoint("LEFT", share, "RIGHT", 250, 0)
    simc.Text:SetText("Share SimC export with guild cache")
    simc:SetChecked(ET.db.settings.shareSimC)
    simc:SetScript("OnClick", function(self)
        if self:GetChecked() and not ET.db.settings.shareProfile then
            self:SetChecked(false)
            ET:Print("Enable profile sharing first.")
            return
        end
        ET.db.settings.shareSimC = self:GetChecked() and true or false
        if ET.db.settings.shareSimC then
            ET:ScheduleSimCPublish()
        else
            ET.db.simcProfiles[ET.key] = nil
            ET.Comm:RevokeSimC()
        end
        UI:Refresh()
    end)
    self.simcCheck = simc

    local bags = CreateFrame("CheckButton", nil, frame, "UICheckButtonTemplate")
    bags:SetPoint("TOPLEFT", 22, -82)
    bags.Text:SetText("Share my bags with guildmates")
    bags:SetChecked(ET.db.settings.shareInventory)
    bags:SetScript("OnClick", function(self)
        if self:GetChecked() and not ET.db.settings.shareProfile then
            self:SetChecked(false)
            ET:Print("Enable profile sharing first.")
            return
        end
        ET.db.settings.shareInventory = self:GetChecked() and true or false
        if ET.db.settings.shareInventory then
            ET.Inventory:CaptureBags(false)
            ET.Comm:PublishBags()
        else
            ET.db.inventory[ET.key] = nil
            ET.Comm:RevokeBags()
        end
        UI:Refresh()
    end)
    self.bagCheck = bags

    local search = CreateFrame("EditBox", nil, frame, "InputBoxTemplate")
    search:SetSize(236, 26)
    search:SetPoint("TOPLEFT", 29, -126)
    search:SetAutoFocus(false)
    search:SetScript("OnEscapePressed", search.ClearFocus)
    search:SetScript("OnTextChanged", function() UI:RefreshRoster() end)
    self.search = search
    local searchHint = label(frame, "Search characters", "GameFontHighlightSmall")
    searchHint:SetPoint("BOTTOMLEFT", search, "TOPLEFT", 0, 2)

    local rosterScroll = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    rosterScroll:SetPoint("TOPLEFT", 23, -167)
    rosterScroll:SetPoint("BOTTOMLEFT", 23, 43)
    rosterScroll:SetWidth(256)
    local rosterChild = CreateFrame("Frame", nil, rosterScroll)
    rosterChild:SetWidth(236)
    rosterChild:SetHeight(1)
    rosterScroll:SetScrollChild(rosterChild)
    self.rosterChild = rosterChild

    local divider = frame:CreateTexture(nil, "ARTWORK")
    divider:SetColorTexture(0.42, 0.35, 0.58, 0.7)
    divider:SetSize(1, 555)
    divider:SetPoint("TOPLEFT", 291, -104)

    local detailScroll = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
    detailScroll:SetPoint("TOPLEFT", 307, -98)
    detailScroll:SetPoint("BOTTOMRIGHT", -34, 44)
    local detail = CreateFrame("Frame", nil, detailScroll)
    detail:SetWidth(636)
    detail:SetHeight(550)
    detailScroll:SetScrollChild(detail)
    self.detail = detail
    self.detailScroll = detailScroll

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
                if saved and ET.db.settings.shareSimC then ET.Comm:PublishSimC() end
                if not saved then ET:Print("This SimC profile is too large to cache or share.") end
            else ET:Print(err) end
        elseif ET.Comm:RequestSimC(UI.selected, snapshot.guid) then
            ET:Print("Requested a SimC profile from " .. UI.selected .. ". They must be online and sharing it.")
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

    local gearHeading = label(detail, "EQUIPPED GEAR", "GameFontNormal")
    gearHeading:SetPoint("TOPLEFT", 6, -119)
    for index, slot in ipairs(ET.slots) do
        local column = (index - 1) >= 10 and 1 or 0
        local rowIndex = column == 0 and index - 1 or index - 11
        local row = CreateFrame("Button", nil, detail)
        row:SetSize(305, 25)
        row:SetPoint("TOPLEFT", 6 + column * 312, -145 - rowIndex * 26)
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
    wishHeading:SetPoint("TOPLEFT", 6, -425)
    local emptyWish = label(detail, "No items added yet.", "GameFontHighlightSmall")
    emptyWish:SetPoint("TOPLEFT", 6, -458)
    self.emptyWish = emptyWish

    local itemInput = CreateFrame("EditBox", nil, detail, "InputBoxTemplate")
    itemInput:SetSize(188, 24)
    itemInput:SetPoint("TOPLEFT", 14, -454)
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

    local footer = label(frame, "Shared SimC and bags are cached by online guildmates, then available after you log off.", "GameFontHighlightSmall")
    footer:SetPoint("BOTTOMLEFT", 23, 20)
    frame:SetScript("OnShow", function() UI:Refresh() end)
end

function UI:RefreshRoster()
    if not self.frame then return end
    local search = self.search:GetText():lower()
    local keys = {}
    for key, snapshot in pairs(ET.db.characters) do
        if type(snapshot) == "table" and key:lower():find(search, 1, true) then
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
            row:SetPoint("TOPLEFT", 6, -(isSelf and 490 or 458) - (index - 1) * 25)
            row:Show()
        else
            row:Hide()
        end
    end
    self.detail:SetHeight(math.max(550, (isSelf and 510 or 478) + #wishlist * 25))
end

function UI:Refresh()
    if not self.frame then return end
    self.shareCheck:SetChecked(ET.db.settings.shareProfile)
    self.simcCheck:SetChecked(ET.db.settings.shareSimC)
    self.bagCheck:SetChecked(ET.db.settings.shareInventory)
    self:RefreshRoster()
    self:RefreshDetail()
end

function UI:Toggle()
    self:Create()
    if self.frame:IsShown() then self.frame:Hide() else self.frame:Show() end
end
