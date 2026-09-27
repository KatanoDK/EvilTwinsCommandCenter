-- Filtering and bounded rendering for a guild with a large snapshot cache.
local leader = true
local guildRows = {
    { "Alpha", true }, { "Beta-OtherRealm", false }, { "Gamma-Realm", true },
}
function IsInGuild() return true end
function GetNormalizedRealmName() return "Realm" end
function GetNumGuildMembers() return #guildRows end
function GetGuildRosterInfo(index)
    local entry = guildRows[index]
    return entry[1], nil, nil, nil, nil, nil, nil, nil, entry[2]
end
LOCALIZED_CLASS_NAMES_MALE = { MAGE = "Mage", WARRIOR = "Warrior" }
RAID_CLASS_COLORS = { MAGE = { r = 0.3, g = 0.8, b = 1 } }
ET = { key = "Self-Realm", db = { settings = {}, characters = {} }, slots = { { 1, "Head" } } }
function ET:CanUseLeaderTools() return leader end
function ET:GetCachedSimC() return nil end
ET.Inventory = { Get = function() return nil end }
function date() return "09/27 12:00" end
function GetItemInfo() return nil end
local function snapshot(name, class, spec, ilvl, updated, wishlist)
    return { name = name, realm = "Realm", class = class, spec = spec, itemLevel = ilvl,
        updated = updated, wishlist = wishlist or {}, level = 90, gear = {}, talent = "", guid = name }
end
ET.db.characters = {
    [ET.key] = snapshot("Self", "MAGE", "Frost", 100, 100),
    ["Alpha-Realm"] = snapshot("Alpha", "MAGE", "Arcane", 200, 200, { { id = 123 } }),
    ["Beta-OtherRealm"] = snapshot("Beta", "WARRIOR", "Arms", 300, 150),
    ["Gamma-Realm"] = snapshot("Gamma", "MAGE", "Fire", 200, 200),
    ["Unknown-Realm"] = snapshot("Unknown", "WARRIOR", "Fury", 50, 50),
}

function RunRosterChecks()
    local entries, total, classes = ET.Roster:Build()
    assert(#entries == 5 and total == 5 and #classes == 2)
    assert(entries[1].key == "Alpha-Realm" and entries[1].online == true)
    assert(entries[2].online == false and entries[5].online == nil)
    assert(#ET.Roster:Build("  arcane ") == 1, "Spec search must trim whitespace")
    assert(#ET.Roster:Build("otherrealm") == 1, "Cross-realm search")
    assert(#ET.Roster:Build("", "ONLINE") == 3, "Offline/unknown characters must not pass the online filter")
    assert(#ET.Roster:Build("", "WISHLIST") == 1)
    assert(#ET.Roster:Build("", "ALL", "MAGE") == 3)
    assert(#ET.Roster:Build("mage", "ONLINE", "MAGE") == 3, "Filters should combine")
    assert(#ET.Roster:Build("does not exist") == 0)
    entries = ET.Roster:Build("", "ALL", "ALL", "ITEM_LEVEL")
    assert(entries[1].key == "Beta-OtherRealm" and entries[2].key == "Alpha-Realm")
    entries = ET.Roster:Build("", "ALL", "ALL", "RECENT")
    assert(entries[1].key == "Alpha-Realm" and entries[2].key == "Gamma-Realm", "Ties should be deterministic")
    leader = false
    entries, total = ET.Roster:Build()
    assert(#entries == 1 and entries[1].key == ET.key and total == 1, "Member access must remain restricted")
    leader = true
    for index = 1, 1000 do
        local name = string.format("Toon%04d", index)
        ET.db.characters[name .. "-Realm"] = snapshot(name, "MAGE", "Frost", index, index)
    end
end

-- Minimal UI host: exercise the real callbacks and recycled row bindings.
local methods = {}
function methods:GetFrameLevel() return 1 end
function methods:SetFrameLevel(value) self.frameLevel = value end
local function widget(parent) return setmetatable({ parent = parent, shown = true, scripts = {}, hooks = {}, height = 406 }, { __index = methods }) end
for _, name in ipairs({ "SetBackdrop", "SetBackdropColor", "SetBackdropBorderColor", "SetAlpha",
    "SetClampedToScreen", "SetToplevel", "Raise", "SetJustifyH", "SetWordWrap", "SetTextColor", "SetFrameStrata",
    "EnableMouse", "SetMovable", "RegisterForDrag", "StartMoving", "StopMovingOrSizing", "SetAutoFocus",
    "ClearFocus", "SetColorTexture", "SetAllPoints", "SetHighlightTexture", "SetMaxLines", "RegisterEvent", "SetTexture" }) do methods[name] = function() end end
function methods:SetSize(w, h) self.width, self.height = w, h end
function methods:SetWidth(w) self.width = w end
function methods:SetHeight(h) self.height = h end
function methods:GetHeight() return self.height end
function methods:SetPoint(...) self.point = { ... } end
function methods:ClearAllPoints() self.point = nil end
function methods:SetText(text) self.text = text end
function methods:GetText() return self.text or "" end
function methods:SetEnabled(enabled) self.enabled = enabled end
function methods:Show() local was = self.shown; self.shown = true; if not was and self.scripts.OnShow then self.scripts.OnShow(self) end end
function methods:Hide() self.shown = false end
function methods:IsShown() return self.shown and (not self.parent or self.parent:IsShown()) end
function methods:SetShown(shown) if shown then self:Show() else self:Hide() end end
function methods:CreateFontString() return widget(self) end
function methods:CreateTexture() return widget(self) end
function methods:SetScript(name, callback) self.scripts[name] = callback end
function methods:HookScript(name, callback) self.hooks[name] = callback end
function methods:SetScrollChild(child) self.child = child end
function methods:GetVerticalScroll() return self.offset or 0 end
function methods:SetVerticalScroll(offset) self.offset = offset; if self.hooks.OnVerticalScroll then self.hooks.OnVerticalScroll(self, offset) end end
function methods:SetupMenu(generator) self.generator = generator; self:GenerateMenu() end
function methods:GenerateMenu()
    local root = { choices = {} }
    function root:CreateRadio(text, selected, pick, value)
        self.choices[#self.choices + 1] = { text = text, selected = selected, pick = pick, value = value }
    end
    self.generator(self, root)
    self.choices = root.choices
end
UIParent = widget()
function CreateFrame(_, _, parent) return widget(parent) end

function RunUIChecks()
    local UI = ET.UI
    function UI:RefreshCrafting() end
    UI:Create()
    UI.rosterScroll.height = 406
    UI:Refresh()
    assert(UI.activeTab == "prep" and UI.tabPanels.prep.shown and not UI.tabPanels.build.shown)
    assert(not UI.prepSettingsButton and not UI.gearPanel, "Settings and the full equipment panel must be removed")
    assert(#UI.rosterEntries == 1005 and #UI.rosterRows <= 10, "A large roster must use a small row pool")
    local firstKey = UI.rosterRows[1].key
    UI.rosterScroll:SetVerticalScroll(46 * 500)
    assert(UI.rosterRows[1].key ~= firstKey and #UI.rosterRows <= 10, "Scrolling must recycle row widgets")
    local clickedKey = UI.rosterRows[1].key
    UI.rosterRows[1].scripts.OnClick(UI.rosterRows[1])
    assert(UI.selected == clickedKey and UI.heading.text:find(ET.db.characters[clickedKey].name, 1, true), "Click must select the recycled row's current toon")
    UI:Refresh()
    assert(UI.selected == clickedKey and UI.rosterScroll:GetVerticalScroll() == 46 * 500, "Incoming refreshes must preserve selection and scroll position")
    UI.search:SetText("Alpha")
    UI.search.scripts.OnTextChanged()
    assert(#UI.rosterEntries == 1 and UI.rosterScroll:GetVerticalScroll() == 0)
    for index = 2, #UI.rosterRows do assert(not UI.rosterRows[index].shown) end
    UI.search:SetText("absent")
    UI.search.scripts.OnTextChanged()
    assert(UI.rosterEmpty.shown and not UI.rosterRows[1].shown, "An empty search must clear old rows")
    UI.search:SetText("")
    UI.visibilityFilter.choices[2].pick("ONLINE")
    assert(#UI.rosterEntries == 3 and ET.db.settings.rosterVisibility == "ONLINE")
    UI.selected = ET.key
    UI:SetTab("wishlist")
    UI:RefreshDetail()
    assert(UI.itemInput.shown and UI.noteHint.shown and UI.emptyWish.point[3] == -184)
    assert(UI.importUpgradeButton and #UI.upgradeRows == 100 and not UI.upgradeRows[1].shown, "Upgrade rows must stay bounded and clear when no report exists")
    assert(UI.tabPanels.wishlist.shown and not UI.tabPanels.prep.shown)
    UI.selected = "Alpha-Realm"
    UI:RefreshDetail()
    assert(not UI.itemInput.shown and not UI.noteHint.shown and UI.wishRows[1].shown and not UI.wishRows[1].remove.shown)
    UI:SetTab("build")
    assert(UI.tabPanels.build.shown and not UI.tabPanels.wishlist.shown and UI.activeTab == "build")
    UI:SetTab("bags")
    assert(UI.activeTab == "build" and not UI.tabPanels.bags and not UI.bagButton, "Bags must stay internal")
    assert(UI.tabButtons.build.selected and UI.tabButtons.build.accent.shown and not UI.tabButtons.prep.accent.shown)
    leader = false
    UI:Refresh()
    assert(not UI.sidebar.shown and not UI.detailScroll.shown and UI.craftingButton.shown, "Losing leader access must hide all leader panels")
    assert(UI.selected == ET.key and #UI.rosterEntries == 1)
    assert(not UI.profilePanel.shown and not UI.tabButtons.prep.shown)
    UI:SetTab("prep")
    assert(UI.activeTab == "build", "Members must not switch to leader tools")
end

function RunUpgradeUIChecks()
    leader = true
    local UI = ET.UI
    UI.selected = ET.key
    ET.db.characters[ET.key].specID = 1480
    local report = { owner = ET.key, specID = 1480, id = "FixtureReport123", updated = 1,
        title = "Fixture raid", fightStyle = "Patchwerk", duration = 300, enemies = 1,
        assumptions = "Conversions included", talents = "", gear = {}, items = {} }
    for index = 1, 100 do
        report.items[index] = { dropID = 1000 + index, simulatedID = 2000 + index,
            name = "Upgrade " .. index, itemLevel = 302, gain = 100, percent = 0.5,
            boss = "Test boss", difficulty = "raid-normal", withinError = false }
    end
    ET.db.upgradeReports = { fixture = report }
    UI:Refresh()
    UI:SetTab("wishlist")
    assert(UI.upgradeRows[1].shown and UI.upgradeRows[100].shown and #UI.upgradeRows == 100)
    assert(UI.upgradeRows[1].source.text:find("Convert to tier", 1, true))
    assert(UI.upgradeRows[1].gain.text:find("0.50%", 1, true) and UI.upgradeLinkButton.enabled)
    assert(UI.wishHeading.point[3] < -4900 and UI.wishPanel:GetHeight() > 5000, "Manual wishlist must be laid out after upgrade rows")
    UI.selected = "Alpha-Realm"; UI:RefreshDetail()
    assert(not UI.upgradeRows[1].shown and not UI.upgradeLinkButton.enabled and not UI.upgradeSelector.shown, "Switching characters must clear the previous report")
    ET.db.upgradeReports = {}; leader = false
    UI:Refresh()
end
