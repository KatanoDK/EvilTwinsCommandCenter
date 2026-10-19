using System.Diagnostics;
using System.Text.Json;
using EvilTwinsUpdater.Core;

namespace EvilTwinsUpdater;

internal sealed partial class RaidPanel : UserControl
{
    private readonly string stateDirectory;
    private readonly bool persistLayout;
    private readonly Button importRaid = new ModernButton { Text = "Import raid export", AutoSize = true, Tag = "primary" };
    private readonly Button copySimC = new ModernButton { Text = "Copy player's SimC", AutoSize = true };
    private readonly Button raidbots = new ModernButton { Text = "Open Raidbots", AutoSize = true };
    private readonly Button importReport = new ModernButton { Text = "Import player's report", AutoSize = true };
    private readonly Button copyReport = new ModernButton { Text = "Copy addon report", AutoSize = true };
    private readonly Button loadLoot = new ModernButton { Text = "Load loot data", AutoSize = true };
    private readonly Button choose = new ModernButton { Text = "Choose engine", AutoSize = true };
    private readonly Button getEngine = new ModernButton { Text = "Install / update SimulationCraft", AutoSize = true };
    private readonly Button run = new ModernButton { Text = "Run raid simulations", AutoSize = true, Tag = "primary" };
    private readonly Button cancel = new ModernButton { Text = "Stop queue", AutoSize = true, Enabled = false };
    private readonly Button pauseQueue = new ModernButton { Text = "Pause after player", AutoSize = true, Visible = false };
    private readonly Button retryFailed = new ModernButton { Text = "Retry failed", AutoSize = true };
    private readonly Button runNext = new ModernButton { Text = "Run next", AutoSize = true, Visible = false };
    private readonly Button checkAll = new ModernButton { Text = "Check all DPS", AutoSize = true, Tag = "tab", MinimumSize = new Size(0, 30), Padding = new Padding(8, 2, 8, 2) };
    private readonly Button uncheckAll = new ModernButton { Text = "Clear", AutoSize = true, Tag = "tab", MinimumSize = new Size(0, 30), Padding = new Padding(8, 2, 8, 2) };
    private readonly TextBox engine = new() { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = "Optional local mode: choose simc.exe" };
    private readonly ComboBox source = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox difficulty = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
    private readonly ComboBox precision = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 185 };
    private readonly ComboBox cpu = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly ComboBox playerFilter = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly ComboBox rosterFilter = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly ComboBox bossFilter = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox dropFilter = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label comparisonHint = AppTheme.Label("Load loot data or import completed results to choose a drop.", 10, muted: true);
    private readonly DataGridView standings = Grid();
    private readonly Dictionary<string, DropPlan> comparisonPlans = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> comparisonErrors = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<RaidDropChoice> comparisonChoices = [];
    private bool refreshingComparison, refreshingMembers;
    private readonly HashSet<string> checkedPlayers = new(StringComparer.OrdinalIgnoreCase);
    private RaidSimulationQueue? queue;
    private IReadOnlyList<string> pendingOrder = [];
    private int requestedSourceId;
    private readonly Label rosterCounts = AppTheme.Label("No raid imported", 10, muted: true);
    private readonly Label selectionTitle = AppTheme.Label("Select a player", 10, true);
    private readonly Label selectionSummary = AppTheme.Label("Import a raid export to see player readiness and results.", 10, muted: true);
    private readonly Label engineStatus = AppTheme.Label("SimulationCraft is not installed", 10, muted: true);
    private readonly Button manageEngine = new ModernButton { Text = "Manage engine", AutoSize = true };
    private readonly Button showDetails = new ModernButton { Text = "Details", AutoSize = true, Tag = "tab" };
    private readonly TableLayoutPanel engineSettings = new() { Dock = DockStyle.Fill, AutoSize = true, Visible = false, ColumnCount = 2 };
    private readonly TextBox search = new() { Width = 230, PlaceholderText = "Find an item or boss" };
    private readonly Label summary = AppTheme.Label("In WoW: Build & SimC → Raid DPS export → Refresh → Copy raid export.", 10, muted: true);
    private readonly Label status = AppTheme.Label("Import a raid to start. Results are saved on this computer.", 9, muted: true);
    private readonly TextBox detail = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Top, Height = 100, Visible = false, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10) };
    private readonly ProgressBar progress = new ModernProgressBar { Dock = DockStyle.Fill, Height = 8 };
    private readonly DataGridView members = Grid();
    private readonly DataGridView loot = Grid();
    private readonly PageTabs tabs = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer playerSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
        Size = new Size(600, 500), SplitterDistance = 330, SplitterWidth = 12,
        Panel1MinSize = 200, Panel2MinSize = 90, Margin = Padding.Empty };
    private readonly Button toggleLocal = new ModernButton { Text = "Hide local controls", AutoSize = true,
        MinimumSize = new Size(0, 34), Padding = new Padding(12, 4, 12, 4), Tag = "tab" };
    private readonly ToolTip layoutHints = new();
    private double playerListShare = 0.68;
    private bool localControlsHidden, arrangingPanes;
    private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 250 };
    private readonly DroptimizerClient reports = new();
    private readonly SimulationCraftUpdates engineUpdates = new();
    private readonly List<RaidDropResult> results = [];
    private readonly Dictionary<string, string> states = new(StringComparer.OrdinalIgnoreCase);
    private RaidExport? raid;
    private string raidCode = "";
    private RaidLootCatalog? catalog;
    private CancellationTokenSource? cancellation;
    private Task? runningTask;
    private bool updating;
    private bool updatingEngine;
    private int jobIndex, jobCount;
    private string currentPlayer = "";
    private SimulationProgress? tracker;
    private string simulationPhase = "";
    private double phaseStart, phaseWeight = 1;
    private readonly Stopwatch elapsed = new();
    public event Action? EngineChanged;
    public event Action? RunningChanged;
    public bool IsRunning => cancellation != null;
    public string EnginePath => engine.Text;
    public RaidPanel(string stateDirectory, bool preview = false)
    {
        this.stateDirectory = Path.Combine(stateDirectory, "raid-upgrades");
        persistLayout = !preview;
        Dock = DockStyle.Fill;
        summary.MaximumSize = status.MaximumSize = new Size(820, 0);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        actions.Controls.AddRange([importRaid, importReport, raidbots, pauseQueue, cancel]);
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
        top.Controls.Add(summary); top.Controls.Add(actions); layout.Controls.Add(top, 0, 0);
        Columns(members, ("Player", 150), ("Specialization", 85), ("Profile captured", 115), ("Status", 130), ("Best gain", 75));
        members.ReadOnly = false;
        foreach (DataGridViewColumn column in members.Columns) column.ReadOnly = true;
        members.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Simulate", HeaderText = "Run", AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 62 });
        members.CurrentCellDirtyStateChanged += (_, _) => { if (members.IsCurrentCellDirty) members.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        members.CellValueChanged += (_, args) => {
            if (refreshingMembers || args.RowIndex < 0 || args.ColumnIndex != 5 || members.Rows[args.RowIndex].Tag is not RaidMember member || !member.Ready || IsRunning) return;
            if (Equals(members.Rows[args.RowIndex].Cells[5].Value, true)) checkedPlayers.Add(member.Key); else checkedPlayers.Remove(member.Key);
            SaveSession(); RefreshActions();
        };
        members.CellPainting += PaintMemberStatus;
        members.SelectionChanged += (_, _) => RefreshActions();
        members.CurrentCellChanged += (_, _) => RefreshActions();
        members.CellDoubleClick += (_, _) => { if (SelectedMember?.Ready == true) { playerFilter.SelectedItem = SelectedMember.Key; tabs.SelectedIndex = 1; } };
        var rosterPage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        rosterPage.RowStyles.Add(new RowStyle(SizeType.AutoSize)); rosterPage.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rosterFilter.Items.AddRange(["All players", "Ready DPS", "Missing profiles"]); rosterFilter.SelectedIndex = 0;
        var rosterToolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        rosterCounts.Margin = new Padding(12, 6, 0, 0);
        rosterToolbar.Controls.AddRange([rosterFilter, rosterCounts, checkAll, uncheckAll]);
        checkAll.Click += (_, _) => { if (raid != null) checkedPlayers.UnionWith(raid.Members.Where(value => value.Ready).Select(value => value.Key)); RefreshMembers(); SaveSession(); };
        uncheckAll.Click += (_, _) => { checkedPlayers.Clear(); RefreshMembers(); SaveSession(); };
        rosterPage.Controls.Add(rosterToolbar, 0, 0); rosterPage.Controls.Add(members, 0, 1);
        rosterFilter.SelectedIndexChanged += (_, _) => {
            RefreshMembers();
            if (members.Rows.Count > 0) members.FirstDisplayedScrollingRowIndex = 0;
        };
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
        tabs.AddPage("Loot priorities", lootPage);
        var comparePage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        comparePage.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        comparePage.RowStyles.Add(new RowStyle(SizeType.AutoSize)); comparePage.RowStyles.Add(new RowStyle(SizeType.AutoSize)); comparePage.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var compareFilters = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12) };
        compareFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); compareFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        compareFilters.Controls.Add(AppTheme.Label("Boss", 10, muted: true), 0, 0); compareFilters.Controls.Add(AppTheme.Label("Drop / difficulty / item level", 10, muted: true), 1, 0);
        compareFilters.Controls.Add(bossFilter, 0, 1); compareFilters.Controls.Add(dropFilter, 1, 1);
        comparePage.Controls.Add(compareFilters, 0, 0); comparePage.Controls.Add(comparisonHint, 0, 1); comparePage.Controls.Add(standings, 0, 2);
        Columns(standings, ("Rank", 45), ("Player", 150), ("DPS gain", 85), ("Gain %", 75), ("Assessment", 180), ("Reward / conversion", 230));
        standings.Columns[0].MinimumWidth = 65;
        standings.Columns[4].DefaultCellStyle.WrapMode = standings.Columns[5].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        standings.SelectionChanged += (_, _) => RefreshActions();
        standings.CurrentCellChanged += (_, _) => RefreshActions();
        bossFilter.SelectedIndexChanged += (_, _) => { if (!refreshingComparison) PopulateDrops(); };
        dropFilter.SelectedIndexChanged += (_, _) => { if (!refreshingComparison) RefreshStandings(); };
        tabs.AddPage("Boss / drop", comparePage);
        tabs.AddAction(toggleLocal);
        var selectedCard = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(16, 8, 16, 8) };
        selectedCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); selectedCard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectionTitle.Margin = new Padding(0, 0, 8, 3); selectionSummary.Margin = new Padding(0, 0, 8, 0);
        selectionSummary.AutoEllipsis = true; selectionSummary.AutoSize = false; selectionSummary.Dock = DockStyle.Top; selectionSummary.Height = 22;
        var selectedActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        selectedActions.Controls.AddRange([copySimC, copyReport, runNext, showDetails]);
        selectedCard.Controls.Add(selectionTitle, 0, 0); selectedCard.Controls.Add(selectionSummary, 0, 1);
        selectedActions.Margin = Padding.Empty;
        selectedCard.Controls.Add(selectedActions, 1, 0); selectedCard.SetRowSpan(selectedActions, 2);
        selectedCard.Controls.Add(detail, 0, 2); selectedCard.SetColumnSpan(detail, 2);
        showDetails.Click += (_, _) => { detail.Visible = !detail.Visible; showDetails.Text = detail.Visible ? "Hide details" : "Details"; };
        playerSplit.Panel1.Controls.Add(tabs); playerSplit.Panel1.Controls.Add(selectedCard); layout.Controls.Add(playerSplit, 0, 1);
        playerFilter.SelectedIndexChanged += (_, _) => RefreshLoot(); search.TextChanged += (_, _) => RefreshLoot();
        loot.SelectionChanged += (_, _) => RefreshActions();
        loot.CurrentCellChanged += (_, _) => RefreshActions();

        var localCard = new CardPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12), Margin = Padding.Empty };
        var localLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 4 };
        localLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 4; row++) localLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var engineSummary = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 0, 0, 6) };
        engineSummary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); engineSummary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); engineSummary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var localTitle = AppTheme.Label("Local raid simulations", 11, true); localTitle.Anchor = AnchorStyles.Left; localTitle.Margin = new Padding(0, 0, 16, 0);
        engineStatus.AutoSize = false; engineStatus.Dock = DockStyle.Fill; engineStatus.TextAlign = ContentAlignment.MiddleLeft; engineStatus.AutoEllipsis = true; engineStatus.Margin = Padding.Empty;
        engineSummary.Controls.Add(localTitle, 0, 0); engineSummary.Controls.Add(engineStatus, 1, 0); engineSummary.Controls.Add(manageEngine, 2, 0);
        localLayout.Controls.Add(engineSummary, 0, 0);
        var engineRow = engineSettings;
        engineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); engineRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        engineRow.Controls.Add(engine, 0, 0); engineRow.Controls.Add(choose, 1, 0); engine.Margin = new Padding(0, 8, 12, 8);
        var engineActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        engineActions.Controls.Add(getEngine);
        engineRow.Controls.Add(engineActions, 0, 1); engineRow.SetColumnSpan(engineActions, 2);
        localLayout.Controls.Add(engineSettings, 0, 1);
        manageEngine.Click += (_, _) => { engineSettings.Visible = !engineSettings.Visible; manageEngine.Text = engineSettings.Visible ? "Close engine settings" : "Manage engine"; };
        difficulty.Items.AddRange(["LFR", "Normal", "Heroic", "Mythic"]); difficulty.SelectedIndex = 2;
        source.Items.Add("Load loot data to select a raid"); source.SelectedIndex = 0;
        precision.Items.AddRange(["Screening: 1,000", "Standard: 5,000", "Detailed: 20,000", "Automatic: 1k → 5k"]); precision.SelectedIndex = 3;
        cpu.Items.AddRange(["Background · half threads", "Full speed · all threads"]); cpu.SelectedIndex = 0;
        layoutHints.SetToolTip(cpu, "Background leaves CPU capacity for WoW. Full speed uses all CPU threads at normal priority; other apps may feel slower.");
        layoutHints.SetToolTip(precision, "Automatic screens all candidates at 1,000 iterations, then refines promising and uncertain drops at 5,000. Fixed modes test every candidate at the chosen precision.");
        var options = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 0, 0, 8) };
        foreach (var width in new float[] { 30, 16, 25, 29 }) options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        var labels = new[] { "Raid / dungeon", "Difficulty", "Precision", "CPU usage" };
        var inputs = new ComboBox[] { source, difficulty, precision, cpu };
        for (var column = 0; column < inputs.Length; column++) {
            var label = AppTheme.Label(labels[column], 10, muted: true); label.Margin = new Padding(0, 0, 0, 5);
            options.Controls.Add(label, column, 0); inputs[column].Dock = DockStyle.Fill;
            inputs[column].Margin = new Padding(0, 0, column == 3 ? 0 : 10, 0); options.Controls.Add(inputs[column], column, 1);
        }
        localLayout.Controls.Add(options, 0, 2);
        var queueActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        queueActions.Controls.AddRange([run, retryFailed, loadLoot]); localLayout.Controls.Add(queueActions, 0, 3);
        layoutHints.SetToolTip(run, "Simulates checked DPS players, one at a time. Completed results with the same settings are retained.");
        layoutHints.SetToolTip(retryFailed, "Retry all failed DPS players with the current simulation settings, keeping completed results.");
        layoutHints.SetToolTip(runNext, "Move this waiting player to the front. The active player finishes first.");
        layoutHints.SetToolTip(getEngine, "No login required. Downloads use HTTPS and are checked against GitHub's published SHA-256 checksum before installation.");
        var localScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        localCard.Controls.Add(localLayout); localScroll.Controls.Add(localCard); playerSplit.Panel2.Controls.Add(localScroll);
        layout.Controls.Add(status, 0, 2); layout.Controls.Add(progress, 0, 3);
        playerSplit.SizeChanged += (_, _) => ArrangePanes();
        playerSplit.SplitterMoved += (_, _) => {
            if (arrangingPanes || playerSplit.Panel2Collapsed || playerSplit.Height <= playerSplit.SplitterWidth) return;
            playerListShare = (double)playerSplit.SplitterDistance / (playerSplit.Height - playerSplit.SplitterWidth);
            SaveLayout();
        };
        playerSplit.Paint += (_, args) => {
            if (playerSplit.Panel2Collapsed) return;
            var grip = playerSplit.SplitterRectangle;
            using var pen = new Pen(AppTheme.Muted, 2);
            args.Graphics.DrawLine(pen, grip.Left + grip.Width / 2 - 28, grip.Top + grip.Height / 2,
                grip.Left + grip.Width / 2 + 28, grip.Top + grip.Height / 2);
        };
        layoutHints.SetToolTip(playerSplit, "Drag the divider up or down to resize the player list.");
        toggleLocal.Click += (_, _) => { localControlsHidden = !localControlsHidden; ArrangePanes(); SaveLayout(); };
        tabs.SelectedIndexChanged += () => { ArrangePanes(); RefreshActions(); };
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
        getEngine.Click += async (_, _) => { if (!IsRunning && !updating) { runningTask = UpdateEngineAsync(); await runningTask; } };
        loadLoot.Click += async (_, _) => { if (!IsRunning && !updating) { runningTask = LoadLootAsync(); await runningTask; } };
        run.Click += async (_, _) => { if (!IsRunning && !updating) { runningTask = RunQueueAsync(); await runningTask; } };
        retryFailed.Click += async (_, _) => { if (!IsRunning && !updating) { runningTask = RunQueueAsync(true); await runningTask; } };
        pauseQueue.Click += (_, _) => {
            if (queue == null) return;
            if (queue.PauseRequested) { queue.Resume(); elapsed.Start(); }
            else queue.PauseAfterCurrent();
            SaveSession(); RefreshActions(); UpdateProgress();
        };
        runNext.Click += (_, _) => {
            if (SelectedMember is { } next && queue?.MoveNext(next.Key) == true) { UpdateQueuedStates(); SaveSession(); RefreshMembers(); RefreshStandings(); }
        };
        cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.Enabled = false;
            status.Text = updatingEngine ? "Stopping SimulationCraft download…" : "Stopping queue; completed results will be kept…"; };
        displayTimer.Tick += (_, _) => UpdateProgress();
        if (!preview) { LoadLayout(); LoadSession(); }
        precision.SelectedIndexChanged += (_, _) => SaveLayout();
        cpu.SelectedIndexChanged += (_, _) => SaveLayout();
        source.SelectedIndexChanged += (_, _) => { if (source.SelectedItem is RaidLootSource value) requestedSourceId = value.Id; comparisonPlans.Clear(); comparisonErrors.Clear(); RefreshComparisonChoices(); };
        difficulty.SelectedIndexChanged += (_, _) => { comparisonPlans.Clear(); comparisonErrors.Clear(); RefreshComparisonChoices(); };
        ArrangePanes();
        RefreshMembers(); RefreshActions();
    }

    private void ArrangePanes()
    {
        if (arrangingPanes) return;
        arrangingPanes = true;
        try
        {
            toggleLocal.Visible = tabs.SelectedIndex == 0;
            toggleLocal.Text = localControlsHidden ? "Show local controls" : "Hide local controls";
            var available = playerSplit.ClientSize.Height - playerSplit.SplitterWidth;
            playerSplit.Panel2Collapsed = tabs.SelectedIndex != 0 || localControlsHidden
                || available < playerSplit.Panel1MinSize + playerSplit.Panel2MinSize;
            if (!playerSplit.Panel2Collapsed)
                playerSplit.SplitterDistance = Math.Clamp((int)(available * playerListShare),
                    playerSplit.Panel1MinSize, available - playerSplit.Panel2MinSize);
            playerSplit.Invalidate();
        }
        finally { arrangingPanes = false; }
    }

    private sealed record LayoutPreferences(double PlayerListShare, bool LocalControlsHidden)
    {
        public int PrecisionIndex { get; init; } = 3;
        public SimulationMode CpuMode { get; init; } = SimulationMode.Background;
    }
    private void LoadLayout()
    {
        try
        {
            var path = Path.Combine(stateDirectory, "layout.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return;
            var saved = JsonSerializer.Deserialize<LayoutPreferences>(File.ReadAllText(path));
            if (saved == null) return;
            if (double.IsFinite(saved.PlayerListShare) && saved.PlayerListShare is >= 0.1 and <= 0.95)
                playerListShare = saved.PlayerListShare;
            localControlsHidden = saved.LocalControlsHidden;
            if (saved.PrecisionIndex >= 0 && saved.PrecisionIndex < precision.Items.Count) precision.SelectedIndex = saved.PrecisionIndex;
            if (Enum.IsDefined(saved.CpuMode)) cpu.SelectedIndex = (int)saved.CpuMode;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { /* Use the default layout. */ }
    }
    private void SaveLayout()
    {
        if (!persistLayout) return;
        try
        {
            Directory.CreateDirectory(stateDirectory);
            var path = Path.Combine(stateDirectory, "layout.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new LayoutPreferences(playerListShare, localControlsHidden)
                { PrecisionIndex = precision.SelectedIndex, CpuMode = (SimulationMode)cpu.SelectedIndex }));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Resizing remains usable without persistence. */ }
    }

    private static DataGridView Grid() => new() { Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private static void Columns(DataGridView grid, params (string Name, int Weight)[] columns)
    {
        foreach (var column in columns) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column.Name, FillWeight = column.Weight, SortMode = DataGridViewColumnSortMode.NotSortable });
    }
    private RaidMember? SelectedMember => tabs.SelectedIndex switch {
        1 => (loot.CurrentRow?.Tag as LootBenefit)?.Member,
        2 => (standings.CurrentRow?.Tag as RaidDropStanding)?.Member,
        _ => members.CurrentRow?.Tag as RaidMember };
    private RaidDropResult? SelectedResult => tabs.SelectedIndex == 2 ? (standings.CurrentRow?.Tag as RaidDropStanding)?.Result
        : tabs.SelectedIndex == 1 && loot.CurrentRow?.Tag is LootBenefit benefit
            ? results.FirstOrDefault(result => result.Report.ReportId == benefit.Report.ReportId)
            : results.FirstOrDefault(result => result.Member.Key == SelectedMember?.Key);
    public void SetEnginePath(string path)
    {
        engine.Text = path;
        var folder = Path.GetFileName(Path.GetDirectoryName(path));
        engineStatus.Text = string.IsNullOrWhiteSpace(path) ? "SimulationCraft is not installed"
            : "SimulationCraft ready" + (string.IsNullOrEmpty(folder) ? "" : " · " + folder);
        layoutHints.SetToolTip(engineStatus, path);
        RefreshActions();
    }
    public void SetApplicationUpdating(bool value) { updating = value; RefreshActions(); }
    private void RefreshActions()
    {
        var available = !IsRunning && !updating;
        importRaid.Enabled = importReport.Enabled = choose.Enabled = getEngine.Enabled = loadLoot.Enabled = available;
        copySimC.Enabled = SelectedMember?.Ready == true;
        copyReport.Enabled = SelectedResult != null;
        difficulty.Enabled = precision.Enabled = cpu.Enabled = available;
        source.Enabled = available && catalog != null;
        var canRun = available && catalog != null && raid?.Members.Any(member => member.Ready) == true && engine.Text.Length > 0;
        run.Enabled = canRun && checkedPlayers.Count > 0;
        run.Text = $"Run checked players ({checkedPlayers.Count})";
        retryFailed.Enabled = canRun && raid!.Members.Any(member => member.Ready && states.GetValueOrDefault(member.Key, "").StartsWith("Failed:", StringComparison.Ordinal));
        checkAll.Enabled = uncheckAll.Enabled = available;
        members.ReadOnly = !available;
        pauseQueue.Visible = queue != null;
        pauseQueue.Enabled = queue?.Pending.Count > 0;
        pauseQueue.Text = queue?.IsPaused == true ? "Resume queue" : queue?.PauseRequested == true ? "Keep running" : "Pause after player";
        runNext.Visible = queue != null;
        runNext.Enabled = SelectedMember != null && queue?.Pending.Contains(SelectedMember.Key, StringComparer.OrdinalIgnoreCase) == true;
        cancel.Enabled = IsRunning;
        cancel.Visible = IsRunning;
        var selected = SelectedResult;
        selectionTitle.Text = SelectedMember?.Key ?? "Select a player";
        selectionSummary.Text = selected != null
            ? $"{selected.Report.Items.Count} positive upgrade(s) · " + (selected.AutomaticRefinement
                ? $"{selected.ScreenedCases} screened → {selected.RefinedCases} refined"
                : selected.Iterations > 0 ? $"{selected.Iterations:N0} iterations" : "Imported Raidbots report")
            : SelectedMember is { } current ? current.Ready
                ? states.GetValueOrDefault(current.Key, "Ready to simulate · Profile captured " + DateTimeOffset.FromUnixTimeSeconds(current.Updated).ToLocalTime().ToString("g"))
                : current.Role.Equals("DAMAGER", StringComparison.OrdinalIgnoreCase)
                    ? "Missing profile · Refresh Raid DPS export in WoW, then import it here."
                    : "Tank or healer · DPS upgrade simulations skip this player."
                : "Import a raid export to see player readiness and results.";
        layoutHints.SetToolTip(selectionSummary, selectionSummary.Text);
        detail.Text = selected != null ? selected.Report.Assumptions + (selected.Warnings.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, selected.Warnings) : "")
            : SelectedMember is { } member ? states.GetValueOrDefault(member.Key, member.State).Replace("\n", Environment.NewLine)
                : "Select a player to copy their export or share a completed result.";
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
        var known = raid?.Members.Select(member => member.Key).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var selectedPlayers = parsed.Members.Where(member => member.Ready && (!known.Contains(member.Key) || checkedPlayers.Contains(member.Key))).Select(member => member.Key).ToArray();
        var retained = results.Where(result => parsed.Members.Any(member => member.Ready && member.Key == result.Member.Key
            && member.Guid == result.Member.Guid && member.SpecId == result.Member.SpecId
            && LocalSimulator.PrepareProfile(member.Profile) == LocalSimulator.PrepareProfile(result.Member.Profile))).ToArray();
        raid = parsed; raidCode = code.Trim(); results.Clear(); results.AddRange(retained); states.Clear();
        checkedPlayers.Clear(); checkedPlayers.UnionWith(selectedPlayers); comparisonPlans.Clear(); comparisonErrors.Clear();
        summary.Text = $"{raid.Members.Count} raid members · Collected {DateTimeOffset.FromUnixTimeSeconds(raid.Captured).ToLocalTime():g}";
        playerFilter.Items.Clear(); playerFilter.Items.Add("All DPS players");
        foreach (var member in raid.Members.Where(member => member.Ready)) playerFilter.Items.Add(member.Key);
        playerFilter.SelectedIndex = 0; RefreshMembers(); RefreshLoot(); RefreshComparisonChoices(); RefreshActions();
        status.Text = "Check capture times; refresh in WoW after gear or talent changes."
            + (retained.Length > 0 ? $" Kept {retained.Length} result(s) for unchanged profiles." : "");
    }
    private void RefreshMembers()
    {
        refreshingMembers = true;
        try {
        var selected = SelectedMember?.Key;
        var firstDisplayed = members.FirstDisplayedScrollingRowIndex;
        members.Rows.Clear();
        if (raid == null) return;
        var ready = raid.Members.Count(member => member.Ready);
        var missing = raid.Members.Count(member => !member.Ready && member.Role.Equals("DAMAGER", StringComparison.OrdinalIgnoreCase));
        rosterCounts.Text = $"{ready} ready DPS   ·   {missing} missing   ·   {raid.Members.Count - ready - missing} skipped";
        var visible = raid.Members.Where(member => rosterFilter.SelectedIndex switch {
            1 => member.Ready, 2 => !member.Ready && member.Role.Equals("DAMAGER", StringComparison.OrdinalIgnoreCase), _ => true });
        foreach (var member in visible.OrderBy(member => member.Ready ? 0 : member.Role.Equals("DAMAGER", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(member => member.Key, StringComparer.OrdinalIgnoreCase))
        {
            var result = results.FirstOrDefault(value => value.Member.Key == member.Key);
            var best = result?.Report.Items.FirstOrDefault();
            var row = members.Rows[members.Rows.Add(member.Key, DpsSpecs.All.TryGetValue(member.SpecId, out var spec)
                ? System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(spec.Token.Replace('_', ' '))
                : member.Role.Equals("DAMAGER", StringComparison.OrdinalIgnoreCase) ? "DPS" : member.Role.ToLowerInvariant(),
                member.Updated > 0 ? DateTimeOffset.FromUnixTimeSeconds(member.Updated).ToLocalTime().ToString("g") : "—",
                MemberStatus(member, result), best != null ? $"+{best.Percent:F2}%" : result != null ? "No gains" : "—", member.Ready && checkedPlayers.Contains(member.Key))];
            row.Tag = member; row.Cells[3].ToolTipText = states.GetValueOrDefault(member.Key, member.State);
            row.Cells[5].ReadOnly = !member.Ready;
            row.Cells[2].ToolTipText = member.Updated > 0 ? DateTimeOffset.FromUnixTimeSeconds(member.Updated).ToLocalTime().ToString("F") : "No captured profile";
            if (!member.Ready && !member.Role.Equals("DAMAGER", StringComparison.OrdinalIgnoreCase)) row.DefaultCellStyle.ForeColor = AppTheme.Muted;
            if (best != null) row.Cells[4].Style.ForeColor = AppTheme.Success;
            if (member.Key == selected) members.CurrentCell = row.Cells[0];
        }
        if (members.Rows.Count > 0 && firstDisplayed >= 0) members.FirstDisplayedScrollingRowIndex = Math.Min(firstDisplayed, members.Rows.Count - 1);
        }
        finally { refreshingMembers = false; }
        RefreshActions();
    }
    private string MemberStatus(RaidMember member, RaidDropResult? result)
    {
        var state = states.GetValueOrDefault(member.Key, "");
        if (state.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)) return "Failed";
        if (result != null) return "Complete";
        if (state.StartsWith("Screening", StringComparison.OrdinalIgnoreCase)) return "Screening";
        if (state.StartsWith("Refining", StringComparison.OrdinalIgnoreCase)) return "Refining";
        if (state.StartsWith("Running", StringComparison.OrdinalIgnoreCase)) return "Running";
        if (state.StartsWith("Queued", StringComparison.OrdinalIgnoreCase)) return state;
        if (state.StartsWith("Stopped", StringComparison.OrdinalIgnoreCase)) return "Stopped";
        if (state.StartsWith("Simulating", StringComparison.OrdinalIgnoreCase)) return "Running";
        if (state.StartsWith("Interrupted", StringComparison.OrdinalIgnoreCase)) return "Interrupted";
        if (state == "Not run") return "Not run";
        return member.Ready ? "Ready" : member.Role.Equals("DAMAGER", StringComparison.OrdinalIgnoreCase) ? "Missing profile" : "Skipped";
    }
    private void PaintMemberStatus(object? sender, DataGridViewCellPaintingEventArgs args)
    {
        if (args.RowIndex < 0 || args.ColumnIndex != 3) return;
        args.PaintBackground(args.ClipBounds, true);
        var text = args.FormattedValue?.ToString() ?? "";
        var color = text is "Ready" or "Complete" ? AppTheme.Success
            : text is "Failed" or "Missing profile" ? AppTheme.Negative
            : text is "Running" or "Screening" or "Refining" || text.StartsWith("Queued", StringComparison.Ordinal) ? AppTheme.Accent : AppTheme.Muted;
        var width = Math.Min(args.CellBounds.Width - 16, TextRenderer.MeasureText(text, members.Font).Width + 16);
        if (width > 0) {
            var bounds = new Rectangle(args.CellBounds.Left + 8, args.CellBounds.Top + (args.CellBounds.Height - 26) / 2, width, 26);
            using var shape = AppTheme.Rounded(bounds, 6);
            using var brush = new SolidBrush(Color.FromArgb(28, color)); args.Graphics!.FillPath(brush, shape);
            TextRenderer.DrawText(args.Graphics, text, members.Font, bounds, color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }
        args.Handled = true;
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
    private async Task UpdateEngineAsync()
    {
        updatingEngine = true; cancel.Text = "Stop download";
        BeginWork(); progress.Maximum = 100; progress.Value = 0;
        try
        {
            var updateProgress = new Progress<EngineUpdateProgress>(value => {
                if (!updatingEngine) return;
                status.Text = value.Message;
                progress.Style = value.Percent.HasValue ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                if (value.Percent.HasValue) progress.Value = Math.Clamp(value.Percent.Value, 0, 100);
            });
            var installed = await engineUpdates.UpdateAsync(Path.Combine(stateDirectory, "engines"), updateProgress, cancellation!.Token);
            SetEnginePath(installed.EnginePath); EngineChanged?.Invoke();
            status.Text = installed.AlreadyCurrent ? $"SimulationCraft is up to date: {installed.Build.FileName}. Engine selected."
                : $"SimulationCraft installed: {installed.Build.FileName}. Ready for local Droptimizers.";
            progress.Value = 100;
        }
        catch (OperationCanceledException) { status.Text = "SimulationCraft update stopped. Your selected engine was kept."; }
        catch (Exception error) { status.Text = "Could not update SimulationCraft: " + error.Message + " Your selected engine was kept."; }
        finally
        {
            updatingEngine = false; cancel.Text = "Stop queue"; progress.Style = ProgressBarStyle.Continuous;
            progress.Maximum = 1000; progress.Value = 0; EndWork();
        }
    }
    private async Task LoadLootAsync()
    {
        BeginWork(); status.Text = "Loading current-season loot data…";
        try
        {
            catalog = await RaidLootCatalog.LoadAsync(Path.Combine(stateDirectory, "loot-data"), cancellation!.Token);
            source.Items.Clear(); foreach (var value in catalog.Sources) source.Items.Add(value);
            source.SelectedItem = catalog.Sources.FirstOrDefault(value => value.Id == requestedSourceId);
            if (source.SelectedIndex < 0) source.SelectedIndex = 0;
            status.Text = catalog.SeasonName + " loot loaded. Local queues use 300s Patchwerk, one target and raid buffs. Choose precision and CPU usage below.";
        }
        catch (OperationCanceledException) { status.Text = "Loot download stopped."; }
        catch (Exception error) { status.Text = "Could not load loot: " + error.Message + " You can still import completed Raidbots reports."; }
        finally { EndWork(); }
    }
    private async Task RunQueueAsync(bool failedOnly = false)
    {
        if (raid == null || catalog == null || source.SelectedItem is not RaidLootSource selected) return;
        try { LocalSimulator.ValidateEngine(engine.Text); }
        catch (Exception error) { status.Text = error.Message; return; }
        var automaticRefinement = precision.SelectedIndex == 3;
        var options = new SimulationOptions(new[] { 1000, 5000, 20000, 5000 }[precision.SelectedIndex], Mode: (SimulationMode)cpu.SelectedIndex);
        var scope = selected.Id + "/raid-" + difficulty.Text.ToLowerInvariant();
        if (results.Any(value => !RaidDroptimizer.MatchesSettings(value, scope, options, automaticRefinement))) {
            if (failedOnly) { status.Text = "Simulation settings changed. Use Run checked players to start a new comparison."; return; }
            results.Clear(); states.Clear();
        }
        var membersToRun = raid.Members.Where(member => member.Ready && (failedOnly
            ? states.GetValueOrDefault(member.Key, "").StartsWith("Failed:", StringComparison.Ordinal) : checkedPlayers.Contains(member.Key))
            && !results.Any(value => value.Member.Key == member.Key))
            .OrderBy(member => { var index = pendingOrder.ToList().FindIndex(key => key.Equals(member.Key, StringComparison.OrdinalIgnoreCase)); return index < 0 ? int.MaxValue : index; }).ToArray();
        if (membersToRun.Length == 0) { status.Text = failedOnly ? "No failed players need retrying." : "The checked players already have results with these settings. Change precision or difficulty for a new run."; return; }
        queue = new RaidSimulationQueue(membersToRun.Select(member => member.Key));
        BeginWork(); jobCount = membersToRun.Length; jobIndex = 0; elapsed.Restart();
        RefreshLoot(); RefreshStandings(); progress.Value = 0; displayTimer.Start();
        UpdateQueuedStates();
        SaveSession(); RefreshMembers();
        try
        {
            while (await queue.NextAsync(cancellation!.Token) is { } key)
            {
                var member = membersToRun.Single(value => value.Key == key);
                cancellation!.Token.ThrowIfCancellationRequested(); currentPlayer = member.Key;
                states[member.Key] = "Running"; RefreshMembers(); tracker = null; phaseStart = 0; phaseWeight = 1; simulationPhase = "Starting";
                try
                {
                    var plan = catalog.Plan(member, selected.Id, difficulty.Text);
                    var callback = new InlineProgress(text => tracker?.Append(text));
                    var phases = new InlinePhaseProgress(phase => {
                        simulationPhase = phase.Name; phaseStart = phase.StartFraction; phaseWeight = phase.Weight;
                        tracker = new SimulationProgress(phase.Cases + 1);
                        states[member.Key] = $"{phase.Name} • {phase.Cases} candidates • {phase.Iterations:N0} iterations • {options.Threads} CPU threads";
                        RefreshMembers(); RefreshStandings(); UpdateProgress();
                    });
                    var result = await RaidDroptimizer.RunAsync(engine.Text, raid, plan, options, Path.Combine(stateDirectory, "runs"), callback, cancellation.Token,
                        automaticRefinement, phases);
                    AddResult(result); states[member.Key] = automaticRefinement
                        ? $"Complete • {result.ScreenedCases} screened / {result.RefinedCases} refined" : $"Complete • {plan.Cases.Count} tests";
                }
                catch (OperationCanceledException) { states[member.Key] = "Stopped"; throw; }
                catch (Exception error) { states[member.Key] = "Failed: " + error.Message; }
                queue.FinishCurrent(); UpdateQueuedStates();
                jobIndex++; tracker = null; phaseStart = 0; phaseWeight = 1; simulationPhase = ""; SaveSession(); RefreshMembers(); RefreshStandings(); UpdateProgress();
            }
            progress.Value = 1000;
            status.Text = $"Queue finished: {results.Count} player result(s) saved • {elapsed.Elapsed:g}. Use Retry failed for any failed players.";
        }
        catch (OperationCanceledException) { status.Text = $"Queue stopped. Kept {results.Count} completed player result(s)."; }
        finally
        {
            pendingOrder = queue.Unfinished;
            foreach (var key in states.Keys.ToArray()) if (states[key].StartsWith("Queued", StringComparison.Ordinal)) states[key] = "Not run";
            elapsed.Stop(); queue = null; SaveSession(); EndWork(); RefreshMembers(); RefreshLoot(); RefreshStandings();
        }
    }
    private void UpdateQueuedStates()
    {
        if (queue == null) return;
        var position = 0;
        foreach (var key in queue.Pending) states[key] = $"Queued #{++position}";
    }
    private void AddResult(RaidDropResult result)
    {
        if (results.Any(value => value.Member.Key != result.Member.Key && (value.Report.Scope != result.Report.Scope
            || value.Report.FightStyle != result.Report.FightStyle || value.Report.Duration != result.Report.Duration || value.Report.Enemies != result.Report.Enemies)))
            throw new InvalidOperationException("Use the same loot sources, difficulty, fight style, duration and targets for every player in this raid comparison.");
        results.RemoveAll(value => value.Member.Key == result.Member.Key); results.Add(result);
        states[result.Member.Key] = "Complete"; SaveSession(); RefreshMembers(); RefreshLoot(); RefreshComparisonChoices();
    }
    private void UpdateProgress()
    {
        if (jobCount == 0) return;
        if (queue?.IsPaused == true) {
            elapsed.Stop(); status.Text = $"Queue paused · {jobIndex}/{jobCount} players finished · {queue.Pending.Count} waiting. Resume when ready.";
            RefreshActions(); return;
        }
        var fraction = Math.Clamp((jobIndex + phaseStart + phaseWeight * (tracker?.Fraction ?? 0)) / jobCount, 0, 1);
        progress.Maximum = 1000; progress.Value = (int)(fraction * 1000);
        var estimate = fraction > 0.005 && elapsed.Elapsed.TotalSeconds > 1
            ? " • about " + TimeSpan.FromSeconds(elapsed.Elapsed.TotalSeconds * (1 - fraction) / fraction).ToString(@"hh\:mm\:ss") + " left" : " • estimating time remaining…";
        status.Text = $"{jobIndex}/{jobCount} players finished • {currentPlayer} • {simulationPhase} • {fraction:P0}{estimate}"
            + (queue?.PauseRequested == true ? " • Will pause after this player." : " (estimate varies by class and loot count)");
    }
    public async Task StopAsync()
    {
        cancellation?.Cancel(); if (runningTask != null) await runningTask;
    }
    private sealed record SavedSession(string Code, IReadOnlyList<RaidDropResult> Results, Dictionary<string, string> States)
    {
        public IReadOnlyList<string>? CheckedPlayers { get; init; }
        public IReadOnlyList<string>? PendingOrder { get; init; }
        public int SourceId { get; init; }
        public string Difficulty { get; init; } = "Heroic";
    }
    private void SaveSession()
    {
        if (raid == null) return;
        try
        {
            Directory.CreateDirectory(stateDirectory); var path = Path.Combine(stateDirectory, "session.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new SavedSession(raidCode, results, states) {
                CheckedPlayers = checkedPlayers.ToArray(), PendingOrder = queue?.Unfinished ?? pendingOrder,
                SourceId = source.SelectedItem is RaidLootSource sourceValue ? sourceValue.Id : requestedSourceId, Difficulty = difficulty.Text }));
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
            requestedSourceId = saved.SourceId;
            if (difficulty.Items.Contains(saved.Difficulty)) difficulty.SelectedItem = saved.Difficulty;
            foreach (var result in saved.Results.Take(40))
            {
                if (!raid!.Members.Any(member => member.Ready && member.Key == result.Member.Key && member.Guid == result.Member.Guid && member.SpecId == result.Report.SpecId)) continue;
                _ = result.Report.ImportCode(); results.Add(result);
            }
            foreach (var (key, value) in saved.States) states[key] = value.StartsWith("Running", StringComparison.Ordinal)
                || value.StartsWith("Queued", StringComparison.Ordinal) || value.StartsWith("Screening", StringComparison.Ordinal)
                || value.StartsWith("Refining", StringComparison.Ordinal) || value.StartsWith("Simulating", StringComparison.Ordinal) ? "Interrupted — run again" : value;
            if (saved.CheckedPlayers != null) { checkedPlayers.Clear(); checkedPlayers.UnionWith(saved.CheckedPlayers.Where(key => raid!.Members.Any(member => member.Ready && member.Key == key))); }
            pendingOrder = (saved.PendingOrder ?? []).Where(key => raid!.Members.Any(member => member.Ready && member.Key == key)).Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToArray();
            RefreshMembers(); RefreshLoot(); RefreshComparisonChoices(); status.Text = "Restored the last raid, checked players and completed results. Check capture times before using them.";
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
        if (disposing) { displayTimer.Dispose(); layoutHints.Dispose(); reports.Dispose(); engineUpdates.Dispose(); cancellation?.Cancel(); }
        base.Dispose(disposing);
    }
    private sealed class InlineProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }
    private sealed class InlinePhaseProgress(Action<RaidSimulationPhase> callback) : IProgress<RaidSimulationPhase>
    {
        public void Report(RaidSimulationPhase value) => callback(value);
    }
}
