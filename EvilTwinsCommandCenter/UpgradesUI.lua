local _, ET = ...
local UI = ET.UI

local difficulties = {
    ["raid-lfr"] = "Raid Finder", ["raid-normal"] = "Normal",
    ["raid-heroic"] = "Heroic", ["raid-mythic"] = "Mythic",
    ["mythic-plus"] = "Mythic+",
}
local function difficultyLabel(value)
    return difficulties[value] or value:gsub("^raid%-", ""):gsub("%-", " "):gsub("(%a)([%w]*)", function(first, rest)
        return first:upper() .. rest
    end)
end

local function selectionKey(owner, specID)
    return owner .. ":" .. tostring(specID or 0)
end

function UI:SelectUpgradeReport(id)
    self.upgradeSelections = self.upgradeSelections or {}
    local snapshot = ET.db.characters[self.selected]
    if not snapshot then return end
    self.upgradeSelections[selectionKey(self.selected, snapshot.specID)] = id
    self.detailScroll:SetVerticalScroll(0)
    self:RefreshDetail()
end

local function label(parent, text, font)
    local widget = parent:CreateFontString(nil, "OVERLAY", font or "GameFontHighlightSmall")
    widget:SetJustifyH("LEFT")
    widget:SetWordWrap(false)
    widget:SetText(text or "")
    return widget
end

function UI:ShowUpgradeImport()
    if not ET:CanUseLeaderTools() then return end
    if not self.upgradeImport then
        local frame = CreateFrame("Frame", "EvilTwinsCCUpgradeImport", UIParent, "BackdropTemplate")
        frame:SetSize(690, 370)
        frame:SetPoint("CENTER")
        frame:SetFrameStrata("DIALOG")
        ET:StyleWindow(frame)
        local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
        close:SetPoint("TOPRIGHT", -5, -5)
        label(frame, "IMPORT DROPTIMIZER REPORT", "GameFontNormalLarge"):SetPoint("TOPLEFT", 24, -24)
        label(frame, "In the updater's Droptimizer tab, fetch a report and copy its addon import code."):SetPoint("TOPLEFT", 24, -57)
        local scroll = CreateFrame("ScrollFrame", nil, frame, "UIPanelScrollFrameTemplate")
        scroll:SetPoint("TOPLEFT", 30, -85)
        scroll:SetSize(615, 190)
        local input = CreateFrame("EditBox", nil, scroll)
        input:SetMultiLine(true)
        input:SetFontObject("ChatFontNormal")
        input:SetWidth(600)
        input:SetAutoFocus(false)
        input:SetMaxLetters(120005)
        scroll:SetScrollChild(input)
        input:SetScript("OnEscapePressed", function() frame:Hide() end)
        local result = label(frame, "")
        result:SetPoint("TOPLEFT", 24, -288)
        result:SetWidth(640)
        result:SetWordWrap(true)
        local import = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
        import:SetSize(145, 26)
        import:SetPoint("BOTTOMRIGHT", -24, 20)
        import:SetText("Import & share")
        import:SetScript("OnClick", function()
            local ok, err, report = ET.Upgrades:Import(input:GetText())
            if ok then
                ET:Print("Droptimizer upgrades saved and queued for sharing with online leaders.")
                input:SetText(""); frame:Hide()
                if ET.db.characters[report.owner] then UI.selected = report.owner
                else ET:Print("The character will appear after its addon shares a snapshot with a leader.") end
                UI.upgradeSelections = UI.upgradeSelections or {}
                UI.upgradeSelections[selectionKey(report.owner, report.specID)] = report.id
                UI:SetTab("wishlist")
                UI:Refresh()
            else result:SetText(err) end
        end)
        self.upgradeImport, self.upgradeInput, self.upgradeImportResult = frame, input, result
    end
    self.upgradeImportResult:SetText("")
    self.upgradeImport:Show()
    self.upgradeInput:SetFocus()
end

function UI:CreateUpgradesPanel()
    local parent = self.wishPanel
    self.upgradeRows = {}
    self.upgradeHeading = label(parent, "SIMULATED UPGRADES", "GameFontNormal")
    self.upgradeHeading:SetPoint("TOPLEFT", 16, -14)
    self.importUpgradeButton = CreateFrame("Button", nil, parent, "UIPanelButtonTemplate")
    self.importUpgradeButton:SetSize(130, 25)
    self.importUpgradeButton:SetPoint("TOPRIGHT", -16, -8)
    self.importUpgradeButton:SetText("Import report")
    self.importUpgradeButton:SetScript("OnClick", function() UI:ShowUpgradeImport() end)
    self.upgradeLinkButton = CreateFrame("Button", nil, parent, "UIPanelButtonTemplate")
    self.upgradeLinkButton:SetSize(130, 25)
    self.upgradeLinkButton:SetPoint("TOPRIGHT", -152, -8)
    self.upgradeLinkButton:SetText("Copy report link")
    self.upgradeLinkButton:SetScript("OnClick", function()
        if self.selectedUpgradeID and ET:CanUseLeaderTools() then
            UI:ShowCopy("Raidbots report", "https://www.raidbots.com/simbot/report/" .. self.selectedUpgradeID)
        end
    end)
    self.upgradeHint = label(parent, "Import a Droptimizer report to rank upgrades without entering a wishlist.")
    self.upgradeHint:SetPoint("TOPLEFT", 16, -45)
    self.upgradeHint:SetWidth(650)
    self.upgradeSelector = CreateFrame("DropdownButton", nil, parent, "WowStyle1DropdownTemplate")
    self.upgradeSelector:SetPoint("TOPLEFT", 16, -72)
    self.upgradeSelector:SetWidth(650)
    self.upgradeSelector:SetupMenu(function(_, root)
        root:CreateRadio("Manual wishlist (hide report)",
            function() return self.selectedUpgradeID == nil end,
            function() self:SelectUpgradeReport(false) end, false)
        for _, report in ipairs(self.selectedUpgradeReports or {}) do
            root:CreateRadio(report.title .. " • " .. date("%m/%d %H:%M", report.updated),
                function(value) return self.selectedUpgradeID == value end,
                function(value) self:SelectUpgradeReport(value) end, report.id)
        end
    end)
    self.upgradeSummary = label(parent, "")
    self.upgradeSummary:SetPoint("TOPLEFT", 16, -108)
    self.upgradeSummary:SetWidth(650)
    self.upgradeSummary:SetWordWrap(true)
    self.upgradeSummary:SetMaxLines(3)
    self.upgradeSummary:SetHeight(56)
    for index = 1, 100 do
        local row = CreateFrame("Button", nil, parent)
        row:SetSize(650, 46)
        row:SetPoint("TOPLEFT", 16, -170 - (index - 1) * 48)
        local background = row:CreateTexture(nil, "BACKGROUND")
        background:SetAllPoints()
        background:SetColorTexture(1, 1, 1, index % 2 == 0 and 0.035 or 0)
        row.icon = row:CreateTexture(nil, "ARTWORK")
        row.icon:SetSize(32, 32)
        row.icon:SetPoint("LEFT", 6, 0)
        row.title = label(row, "", "GameFontNormalSmall")
        row.title:SetPoint("TOPLEFT", 48, -5)
        row.title:SetWidth(383)
        row.gain = label(row, "")
        row.gain:SetPoint("TOPRIGHT", -6, -5)
        row.gain:SetJustifyH("RIGHT")
        row.gain:SetWidth(210)
        row.source = label(row, "")
        row.source:SetPoint("TOPLEFT", 48, -25)
        row.source:SetWidth(596)
        row:SetScript("OnEnter", function(self)
            if not self.item then return end
            local item = self.item
            GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
            GameTooltip:SetHyperlink("item:" .. item.dropID)
            GameTooltip:AddLine("Simulated item level: " .. item.itemLevel, 1, 0.82, 0.4)
            if item.dropID ~= item.simulatedID then
                GameTooltip:AddLine("Requires tier conversion to item #" .. item.simulatedID, 1, 0.82, 0.4, true)
            end
            if item.withinError then GameTooltip:AddLine("Small gain within simulation uncertainty.", 1, 0.8, 0.35, true) end
            GameTooltip:AddLine("Uses this report's gear, talents and assumptions; resim after upgrades.", 0.7, 0.75, 0.85, true)
            GameTooltip:Show()
        end)
        row:SetScript("OnLeave", function() GameTooltip:Hide() end)
        row:SetScript("OnClick", function(self)
            if self.item and IsModifiedClick("CHATLINK") then ChatEdit_InsertLink("item:" .. self.item.dropID) end
        end)
        self.upgradeRows[index] = row
    end
end

function UI:RefreshUpgrades(snapshot)
    self.selectedUpgradeReports = ET.Upgrades:Get(self.selected, snapshot.specID)
    self.upgradeSelections = self.upgradeSelections or {}
    local key = selectionKey(self.selected, snapshot.specID)
    local chosen = self.upgradeSelections[key]
    local selected
    for _, report in ipairs(self.selectedUpgradeReports) do
        if report.id == chosen then selected = report; break end
    end
    if chosen ~= false then selected = selected or self.selectedUpgradeReports[1] end
    if selected then self.upgradeSelections[key] = selected.id end
    self.selectedUpgradeID = selected and selected.id or nil
    self.upgradeLinkButton:SetEnabled(selected ~= nil)
    local hasReports = #self.selectedUpgradeReports > 0
    self.upgradeSelector:SetShown(hasReports)
    self.upgradeSummary:SetShown(selected ~= nil)
    self.upgradeSelector:GenerateMenu()
    self.upgradeHint:SetText(selected and "Positive DPS gains for this report only • top 100 • * within sim uncertainty"
        or hasReports and "Report hidden. Select a saved report above to show simulated upgrades again."
        or "Import a Droptimizer report to rank upgrades without entering a wishlist.")
    if selected then
        self.upgradeSummary:SetText(ET.Upgrades:Freshness(selected, snapshot)
            .. "\n" .. selected.fightStyle .. " • " .. selected.duration .. "s • " .. selected.enemies .. " target(s)"
            .. " • " .. date("%Y-%m-%d %H:%M", selected.updated) .. "\n" .. selected.assumptions)
    end
    for index, row in ipairs(self.upgradeRows) do
        local item = selected and selected.items[index]
        row.item = item
        if item then
            local icon = C_Item and C_Item.GetItemIconByID and C_Item.GetItemIconByID(item.dropID)
            if not icon and GetItemInfoInstant then icon = select(5, GetItemInfoInstant(item.dropID)) end
            if not icon and C_Item and C_Item.RequestLoadItemDataByID then
                self.upgradeIconRequests = self.upgradeIconRequests or {}
                if not self.upgradeIconRequests[item.dropID] then
                    self.upgradeIconRequests[item.dropID] = true
                    C_Item.RequestLoadItemDataByID(item.dropID)
                end
            end
            row.icon:SetTexture(icon or 134400)
            row.title:SetText(index .. ". " .. item.name .. "  (" .. item.itemLevel .. ")")
            row.gain:SetText(string.format("+%.0f DPS  (+%.2f%%)%s", item.gain, item.percent, item.withinError and " *" or ""))
            row.source:SetText((item.boss ~= "" and item.boss or item.instance) .. " • " .. difficultyLabel(item.difficulty) .. (item.dropID ~= item.simulatedID and " • Convert to tier" or ""))
            row:Show()
        else row:Hide() end
    end
    if selected and #selected.items == 0 then self.upgradeHint:SetText("No positive upgrades in this completed report.") end
    return selected and (188 + #selected.items * 48) or hasReports and 116 or 86
end
