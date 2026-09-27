using System.Diagnostics;
using System.Text.Json;
using EvilTwinsUpdater.Core;

namespace EvilTwinsUpdater;

internal sealed class RaidPanel : UserControl
{
    private readonly string stateDirectory;
    private readonly Button importRaid = new ModernButton { Text = "Import raid export", AutoSize = true, Tag = "primary" };
    private readonly Button copySimC = new ModernButton { Text = "Copy player's SimC", AutoSize = true };
    private readonly Button raidbots = new ModernButton { Text = "Open Raidbots", AutoSize = true };
    private readonly Button importReport = new ModernButton { Text = "Import player's report", AutoSize = true };
    private readonly Button copyReport = new ModernButton { Text = "Copy addon report", AutoSize = true };
    private readonly Button loadLoot = new ModernButton { Text = "Load loot data", AutoSize = true };
    private readonly Button choose = new ModernButton { Text = "Choose engine", AutoSize = true };
    private readonly Button getEngine = new ModernButton { Text = "Get engine", AutoSize = true };
    private readonly Button run = new ModernButton { Text = "Queue local Droptimizers", AutoSize = true, Tag = "primary" };
    private readonly Button cancel = new ModernButton { Text = "Stop queue", AutoSize = true, Enabled = false };
    private readonly TextBox engine = new() { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = "Optional local mode: choose simc.exe" };
    private readonly ComboBox source = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox difficulty = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly ComboBox precision = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly ComboBox playerFilter = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly TextBox search = new() { Width = 230, PlaceholderText = "Find an item or boss" };
    private readonly Label summary = AppTheme.Label("In WoW: Build & SimC → Raid DPS export → Refresh → Copy raid export.", 10, muted: true);
    private readonly Label status = AppTheme.Label("Import a raid to start. Results are saved on this computer.", 9, muted: true);
    private readonly TextBox detail = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9) };
    private readonly ProgressBar progress = new ModernProgressBar { Dock = DockStyle.Fill, Height = 8 };
    private readonly DataGridView members = Grid();
    private readonly DataGridView loot = Grid();
    private readonly PageTabs tabs = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 250 };
    private readonly DroptimizerClient reports = new();
    private readonly List<RaidDropResult> results = [];
    private readonly Dictionary<string, string> states = new(StringComparer.OrdinalIgnoreCase);
    private RaidExport? raid;
    private string raidCode = "";
    private RaidLootCatalog? catalog;
    private CancellationTokenSource? cancellation;
    private Task? runningTask;
    private bool updating;
    private int jobIndex, jobCount;
    private string currentPlayer = "";
    private SimulationProgress? tracker;
    private readonly Stopwatch elapsed = new();
    public event Action? EngineChanged;
    public event Action? RunningChanged;
    public bool IsRunning => cancellation != null;
    public string EnginePath => engine.Text;
    public RaidPanel(string stateDirectory, bool preview = false)
    {
        this.stateDirectory = Path.Combine(stateDirectory, "raid-upgrades");
        Dock = DockStyle.Fill;
        summary.MaximumSize = status.MaximumSize = new Size(820, 0);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        actions.Controls.AddRange([importRaid, copySimC, raidbots, importReport, copyReport, cancel]);
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
        top.Controls.Add(summary); top.Controls.Add(actions); layout.Controls.Add(top, 0, 0);
        Columns(members, ("Player", 140), ("Spec", 55), ("Profile captured", 115), ("Queue / availability", 210), ("Best gain", 75));
        members.SelectionChanged += (_, _) => RefreshActions();
        members.CellDoubleClick += (_, _) => { if (SelectedMember?.Ready == true) { playerFilter.SelectedItem = SelectedMember.Key; tabs.SelectedIndex = 1; } };
        var rosterPage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        rosterPage.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); rosterPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        rosterPage.Controls.Add(members, 0, 0); rosterPage.Controls.Add(detail, 0, 1);
        tabs.AddPage("Raid group", rosterPage);
        Columns(loot, ("Drop / conversion", 260), ("Boss / difficulty", 150), ("Player", 120), ("DPS gain", 95), ("Gain %", 80), ("Assessment", 115));
        var widths = new[] { 210, 140, 115, 90, 75, 105 };
        for (var index = 0; index < widths.Length; index++) loot.Columns[index].MinimumWidth = widths[index];
        loot.Columns[0].DefaultCellStyle.WrapMode = loot.Columns[1].DefaultCellStyle.WrapMode = loot.Columns[2].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        var lootPage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        lootPage.RowStyles.Add(new RowStyle(SizeType.AutoSize)); lootPage.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        playerFilter.Items.Add("All DPS players"); playerFilter.SelectedIndex = 0;
        var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        filters.Controls.AddRange([playerFilter, search]);
        filters.Controls.Add(AppTheme.Label("Sorted by DPS gain • * within uncertainty", 9, muted: true));
        lootPage.Controls.Add(filters, 0, 0); lootPage.Controls.Add(loot, 0, 1);
        tabs.AddPage("Loot priorities", lootPage); layout.Controls.Add(tabs, 0, 1);
        playerFilter.SelectedIndexChanged += (_, _) => RefreshLoot(); search.TextChanged += (_, _) => RefreshLoot();
        loot.SelectionChanged += (_, _) => RefreshActions();

        var localCard = new CardPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(16), Margin = new Padding(0, 12, 0, 12) };
        var localLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5 };
        localLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 5; row++) localLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        localLayout.Controls.Add(AppTheme.Label("Optional local Droptimizer queue", 11, true), 0, 0);
        localLayout.Controls.Add(AppTheme.Label("One drop at a time, one DPS player at a time. Uses CPU SimulationCraft; it still takes time.", 9, muted: true), 0, 1);
        var engineRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3 };
        engineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); engineRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); engineRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        engineRow.Controls.Add(engine, 0, 0); engineRow.Controls.Add(choose, 1, 0); engineRow.Controls.Add(getEngine, 2, 0); engine.Margin = new Padding(0, 8, 12, 8);
        localLayout.Controls.Add(engineRow, 0, 2);
        difficulty.Items.AddRange(["LFR", "Normal", "Heroic", "Mythic"]); difficulty.SelectedIndex = 2;
        precision.Items.AddRange(["Screening: 1,000", "Standard: 5,000", "Detailed: 20,000"]); precision.SelectedIndex = 0;
        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        options.Controls.AddRange([loadLoot, source, difficulty, precision]); localLayout.Controls.Add(options, 0, 3);
        var queueActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        queueActions.Controls.Add(run); localLayout.Controls.Add(queueActions, 0, 4);
        localCard.Controls.Add(localLayout); layout.Controls.Add(localCard, 0, 2); layout.Controls.Add(status, 0, 3); layout.Controls.Add(progress, 0, 4);
        tabs.SelectedIndexChanged += () => { localCard.Visible = tabs.SelectedIndex == 0; RefreshActions(); };
        Controls.Add(layout);
        importRaid.Click += (_, _) => ImportRaid(); copySimC.Click += (_, _) => Copy(SelectedMember?.Profile, "Player's SimC copied. Paste it into Raidbots Droptimizer.");
        raidbots.Click += (_, _) => Open("https://www.raidbots.com/simbot/droptimizer");
        importReport.Click += async (_, _) => { if (!IsRunning && !updating) { runningTask = ImportReportAsync(); await runningTask; } };
        copyReport.Click += (_, _) => Copy(SelectedResult?.Report.ImportCode(), "Report copied. Paste into the addon's Wishlist & upgrades → Import report.");
        choose.Click += (_, _) => {
            using var dialog = new OpenFileDialog { Filter = "SimulationCraft engine|simc.exe", Title = "Choose SimulationCraft's simc.exe" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { SetEnginePath(LocalSimulator.ValidateEngine(dialog.FileName)); EngineChanged?.Invoke(); }
            catch (Exception error) { status.Text = error.Message; }
        };
        getEngine.Click += (_, _) => Open("https://www.simulationcraft.org/download.html");
        loadLoot.Click += async (_, _) => { if (!IsRunning && !updating) { runningTask = LoadLootAsync(); await runningTask; } };
        run.Click += async (_, _) => { if (!IsRunning && !updating) { runningTask = RunQueueAsync(); await runningTask; } };
        cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.Enabled = false; status.Text = "Stopping queue; completed results will be kept…"; };
        displayTimer.Tick += (_, _) => UpdateProgress();
        if (!preview) LoadSession();
        RefreshMembers(); RefreshActions();
    }

    private static DataGridView Grid() => new() { Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private static void Columns(DataGridView grid, params (string Name, int Weight)[] columns)
    {
        foreach (var column in columns) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column.Name, FillWeight = column.Weight, SortMode = DataGridViewColumnSortMode.NotSortable });
    }
    private RaidMember? SelectedMember => tabs.SelectedIndex == 1 && loot.CurrentRow?.Tag is LootBenefit benefit ? benefit.Member : members.CurrentRow?.Tag as RaidMember;
    private RaidDropResult? SelectedResult => tabs.SelectedIndex == 1 && loot.CurrentRow?.Tag is LootBenefit benefit
        ? results.FirstOrDefault(result => result.Report.ReportId == benefit.Report.ReportId)
        : results.FirstOrDefault(result => result.Member.Key == SelectedMember?.Key);
    public void SetEnginePath(string path) { engine.Text = path; RefreshActions(); }
    public void SetApplicationUpdating(bool value) { updating = value; RefreshActions(); }
    private void RefreshActions()
    {
        var available = !IsRunning && !updating;
        importRaid.Enabled = importReport.Enabled = choose.Enabled = loadLoot.Enabled = available;
        copySimC.Enabled = SelectedMember?.Ready == true;
        copyReport.Enabled = SelectedResult != null;
        source.Enabled = difficulty.Enabled = precision.Enabled = available;
        run.Enabled = available && catalog != null && raid?.Members.Any(member => member.Ready) == true && engine.Text.Length > 0;
        cancel.Enabled = IsRunning;
        cancel.Visible = IsRunning;
        var selected = SelectedResult;
        detail.Text = selected != null ? selected.Report.Assumptions + (selected.Warnings.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, selected.Warnings) : "")
            : SelectedMember?.State ?? "Select a player to copy their export or share a completed result.";
    }

    private string? Prompt(string title, string hint, int maximum)
    {
        using var dialog = new Form { Text = title, ClientSize = new Size(740, 440), MinimumSize = new Size(550, 350),
            StartPosition = FormStartPosition.CenterParent, Font = Font, ShowInTaskbar = false };
        var text = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, MaxLength = maximum };
        var label = AppTheme.Label(hint, 10, muted: true); label.Dock = DockStyle.Top;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var accept = new ModernButton { Text = "Import", AutoSize = true, DialogResult = DialogResult.OK, Tag = "primary" };
        var dismiss = new ModernButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([accept, dismiss]);
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) }; panel.Controls.Add(text); panel.Controls.Add(label); panel.Controls.Add(buttons);
        dialog.Controls.Add(panel); dialog.AcceptButton = accept; dialog.CancelButton = dismiss; AppTheme.Apply(dialog);
        dialog.HandleCreated += (_, _) => AppTheme.DarkTitleBar(dialog);
        return dialog.ShowDialog(this) == DialogResult.OK ? text.Text.Trim() : null;
    }
    private void ImportRaid()
    {
        var code = Prompt("Import raid DPS export", "Paste the ETR1 code copied from Raid DPS export in the addon.", 4_800_010);
        if (code == null) return;
        try { SetRaid(code); SaveSession(); }
        catch (Exception error) { status.Text = error.Message; }
    }
    internal void SetRaid(string code)
    {
        var parsed = RaidExport.Parse(code);
        var retained = results.Where(result => parsed.Members.Any(member => member.Ready && member.Key == result.Member.Key
            && member.Guid == result.Member.Guid && member.SpecId == result.Member.SpecId
            && LocalSimulator.PrepareProfile(member.Profile) == LocalSimulator.PrepareProfile(result.Member.Profile))).ToArray();
        raid = parsed; raidCode = code.Trim(); results.Clear(); results.AddRange(retained); states.Clear();
        summary.Text = $"{raid.Members.Count(member => member.Ready)} ready DPS / {raid.Members.Count} raid members • Collected {DateTimeOffset.FromUnixTimeSeconds(raid.Captured).ToLocalTime():g}";
        playerFilter.Items.Clear(); playerFilter.Items.Add("All DPS players");
        foreach (var member in raid.Members.Where(member => member.Ready)) playerFilter.Items.Add(member.Key);
        playerFilter.SelectedIndex = 0; RefreshMembers(); RefreshLoot(); RefreshActions();
        status.Text = "Check capture times; refresh in WoW after gear or talent changes."
            + (retained.Length > 0 ? $" Kept {retained.Length} result(s) for unchanged profiles." : "");
    }
    private void RefreshMembers()
    {
        var selected = SelectedMember?.Key; members.Rows.Clear();
        if (raid == null) return;
        foreach (var member in raid.Members)
        {
            var result = results.FirstOrDefault(value => value.Member.Key == member.Key);
            var best = result?.Report.Items.FirstOrDefault();
            var row = members.Rows[members.Rows.Add(member.Key, DpsSpecs.All.TryGetValue(member.SpecId, out var spec) ? spec.Token.Replace('_', ' ') : member.Role.ToLowerInvariant(),
                member.Updated > 0 ? DateTimeOffset.FromUnixTimeSeconds(member.Updated).ToLocalTime().ToString("g") : "—",
                states.GetValueOrDefault(member.Key, result != null ? "Complete" : member.State), best != null ? $"+{best.Percent:F2}%" : result != null ? "No gains" : "—")];
            row.Tag = member; row.Cells[3].ToolTipText = states.GetValueOrDefault(member.Key, member.State);
            row.Cells[2].ToolTipText = member.Updated > 0 ? DateTimeOffset.FromUnixTimeSeconds(member.Updated).ToLocalTime().ToString("F") : "No captured profile";
            if (member.Key == selected) members.CurrentCell = row.Cells[0];
        }
        RefreshActions();
    }
    private void RefreshLoot()
    {
        loot.Rows.Clear(); var filter = playerFilter.SelectedIndex > 0 ? playerFilter.SelectedItem?.ToString() : null;
        var query = search.Text.Trim();
        foreach (var benefit in RaidDroptimizer.Benefits(results).Where(value => (filter == null || value.Member.Key == filter)
            && (query.Length == 0 || (value.Item.Name + " " + value.Item.Boss + " " + value.Item.DropId).Contains(query, StringComparison.OrdinalIgnoreCase))))
        {
            var item = benefit.Item; var row = loot.Rows[loot.Rows.Add(item.Name + $" ({item.ItemLevel})", item.Boss + "\n"
                + System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(item.Difficulty.Replace("raid-", "")), benefit.Member.Key.Split('-', 2)[0],
                $"+{item.Gain:N0}", $"+{item.Percent:F2}%", item.WithinError ? "* uncertain" : "Positive gain")];
            row.Tag = benefit; row.Cells[3].Style.ForeColor = item.WithinError ? AppTheme.Muted : AppTheme.Success;
            row.Cells[2].ToolTipText = benefit.Member.Key;
            row.Cells[0].ToolTipText = $"Drop #{item.DropId} • simulated #{item.SimulatedId}\n{benefit.Report.Title}\n{benefit.Report.FightStyle} • {benefit.Report.Duration}s • {benefit.Report.Enemies} target(s)\n{benefit.Report.Assumptions}";
        }
        RefreshActions();
    }
    private async Task ImportReportAsync()
    {
        if (raid == null) { status.Text = "Import a raid export first."; return; }
        var link = Prompt("Import completed Droptimizer", "Paste a completed Raidbots report link for a DPS player in this raid.", 500);
        if (link == null) return;
        BeginWork(); status.Text = "Downloading completed report…";
        try
        {
            var report = await reports.FetchAsync(link, cancellation!.Token);
            var member = raid.Members.FirstOrDefault(value => value.Ready && value.SpecId == report.SpecId
                && Normalize(value.Key) == Normalize(report.Name + "-" + report.Realm));
            if (member == null || raid.Region != report.Region) throw new InvalidDataException("The report must belong to a ready DPS player and specialization in this raid.");
            if (!RaidDroptimizer.Matches(member, report)) throw new InvalidDataException("This report's gear or talents differ from the collected profile. Refresh the raid export or run a new report with the copied SimC.");
            AddResult(new(member, report, "", ["Imported Raidbots report; compare players using the same fight and loot settings."]));
            status.Text = $"Imported {member.Key}: {report.Items.Count} positive upgrades.";
        }
        catch (OperationCanceledException) { status.Text = "Report import stopped."; }
        catch (Exception error) { status.Text = error.Message; }
        finally { EndWork(); }
    }
    private void BeginWork() { cancellation = new CancellationTokenSource(); RefreshActions(); RunningChanged?.Invoke(); }
    private void EndWork() { displayTimer.Stop(); cancellation?.Dispose(); cancellation = null; tracker = null; RefreshActions(); RunningChanged?.Invoke(); }
    private async Task LoadLootAsync()
    {
        BeginWork(); status.Text = "Loading current-season loot data…";
        try
        {
            catalog = await RaidLootCatalog.LoadAsync(Path.Combine(stateDirectory, "loot-data"), cancellation!.Token);
            source.Items.Clear(); foreach (var value in catalog.Sources) source.Items.Add(value); source.SelectedIndex = 0;
            status.Text = catalog.SeasonName + " loot loaded. Local queues use 300s Patchwerk, one target, raid buffs and background CPU mode.";
        }
        catch (OperationCanceledException) { status.Text = "Loot download stopped."; }
        catch (Exception error) { status.Text = "Could not load loot: " + error.Message + " You can still import completed Raidbots reports."; }
        finally { EndWork(); }
    }
    private async Task RunQueueAsync()
    {
        if (raid == null || catalog == null || source.SelectedItem is not RaidLootSource selected) return;
        try { LocalSimulator.ValidateEngine(engine.Text); }
        catch (Exception error) { status.Text = error.Message; return; }
        var options = new SimulationOptions(new[] { 1000, 5000, 20000 }[precision.SelectedIndex]);
        var scope = selected.Id + "/raid-" + difficulty.Text.ToLowerInvariant();
        if (results.Any(value => value.Report.Scope != scope || value.Iterations != options.Iterations
            || value.Report.FightStyle != "Patchwerk" || value.Report.Duration != options.Duration || value.Report.Enemies != options.Targets)) results.Clear();
        var membersToRun = raid.Members.Where(member => member.Ready && !results.Any(value => value.Member.Key == member.Key)).ToArray();
        if (membersToRun.Length == 0) { status.Text = "Every ready DPS player already has a result with these settings. Change precision or difficulty for a new queue."; return; }
        BeginWork(); jobCount = membersToRun.Length; jobIndex = 0; elapsed.Restart();
        states.Clear(); RefreshLoot(); progress.Value = 0; displayTimer.Start();
        foreach (var member in membersToRun) states[member.Key] = "Queued";
        SaveSession(); RefreshMembers();
        try
        {
            foreach (var member in membersToRun)
            {
                cancellation!.Token.ThrowIfCancellationRequested(); currentPlayer = member.Key;
                states[member.Key] = "Running"; RefreshMembers(); tracker = null;
                try
                {
                    var plan = catalog.Plan(member, selected.Id, difficulty.Text);
                    tracker = new SimulationProgress(plan.Cases.Count + 1);
                    var callback = new InlineProgress(text => tracker?.Append(text));
                    var result = await RaidDroptimizer.RunAsync(engine.Text, raid, plan, options, Path.Combine(stateDirectory, "runs"), callback, cancellation.Token);
                    AddResult(result); states[member.Key] = $"Complete • {plan.Cases.Count} tests";
                }
                catch (OperationCanceledException) { states[member.Key] = "Stopped"; throw; }
                catch (Exception error) { states[member.Key] = "Failed: " + error.Message; }
                jobIndex++; tracker = null; SaveSession(); RefreshMembers(); UpdateProgress();
            }
            progress.Value = 1000;
            status.Text = $"Queue finished: {results.Count} player result(s) saved • {elapsed.Elapsed:g}. Failed players show their error in the raid list.";
        }
        catch (OperationCanceledException) { status.Text = $"Queue stopped. Kept {results.Count} completed player result(s)."; }
        finally
        {
            foreach (var key in states.Keys.ToArray()) if (states[key] == "Queued") states[key] = "Not run";
            elapsed.Stop(); SaveSession(); EndWork(); RefreshMembers(); RefreshLoot();
        }
    }
    private void AddResult(RaidDropResult result)
    {
        if (results.Any(value => value.Member.Key != result.Member.Key && (value.Report.Scope != result.Report.Scope
            || value.Report.FightStyle != result.Report.FightStyle || value.Report.Duration != result.Report.Duration || value.Report.Enemies != result.Report.Enemies)))
            throw new InvalidOperationException("Use the same loot sources, difficulty, fight style, duration and targets for every player in this raid comparison.");
        results.RemoveAll(value => value.Member.Key == result.Member.Key); results.Add(result);
        states[result.Member.Key] = "Complete"; SaveSession(); RefreshMembers(); RefreshLoot();
    }
    private void UpdateProgress()
    {
        if (jobCount == 0) return;
        var fraction = Math.Clamp((jobIndex + (tracker?.Fraction ?? 0)) / jobCount, 0, 1);
        progress.Maximum = 1000; progress.Value = (int)(fraction * 1000);
        var estimate = fraction > 0.005 && elapsed.Elapsed.TotalSeconds > 1
            ? " • about " + TimeSpan.FromSeconds(elapsed.Elapsed.TotalSeconds * (1 - fraction) / fraction).ToString(@"hh\:mm\:ss") + " left" : " • estimating time remaining…";
        status.Text = $"{jobIndex}/{jobCount} players finished • {currentPlayer} • {fraction:P0}{estimate} (estimate varies by class and loot count)";
    }
    public async Task StopAsync()
    {
        cancellation?.Cancel(); if (runningTask != null) await runningTask;
    }
    private sealed record SavedSession(string Code, IReadOnlyList<RaidDropResult> Results, Dictionary<string, string> States);
    private void SaveSession()
    {
        if (raid == null) return;
        try
        {
            Directory.CreateDirectory(stateDirectory); var path = Path.Combine(stateDirectory, "session.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new SavedSession(raidCode, results, states)));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception error) { status.Text = "Results are visible but could not be saved: " + error.Message; }
    }
    private void LoadSession()
    {
        try
        {
            var path = Path.Combine(stateDirectory, "session.json"); if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 12_000_000) throw new InvalidDataException("Saved raid is too large.");
            var saved = JsonSerializer.Deserialize<SavedSession>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty saved raid.");
            SetRaid(saved.Code);
            foreach (var result in saved.Results.Take(40))
            {
                if (!raid!.Members.Any(member => member.Ready && member.Key == result.Member.Key && member.Guid == result.Member.Guid && member.SpecId == result.Report.SpecId)) continue;
                _ = result.Report.ImportCode(); results.Add(result);
            }
            foreach (var (key, value) in saved.States) states[key] = value is "Running" or "Queued" ? "Interrupted — run again" : value;
            RefreshMembers(); RefreshLoot(); status.Text = "Restored the last raid and completed results. Check capture times before using them.";
        }
        catch (Exception error) { status.Text = "Could not restore the last raid: " + error.Message; }
    }
    private static string Normalize(string value) => value.ToLowerInvariant().Replace(" ", "").Replace("'", "").Replace("_", "").Replace("-", "");
    private void Copy(string? value, string message)
    {
        if (string.IsNullOrEmpty(value)) return;
        try { Clipboard.SetText(value); status.Text = message; }
        catch (Exception error) { status.Text = error.Message; }
    }
    private void Open(string value)
    {
        try { Process.Start(new ProcessStartInfo(value) { UseShellExecute = true }); }
        catch (Exception error) { status.Text = error.Message; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { displayTimer.Dispose(); reports.Dispose(); cancellation?.Cancel(); }
        base.Dispose(disposing);
    }
    private sealed class InlineProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }
}
