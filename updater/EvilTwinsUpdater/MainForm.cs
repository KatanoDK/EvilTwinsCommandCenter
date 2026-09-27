using System.Diagnostics;
using System.Text.Json;
using EvilTwinsUpdater.Core;
using Velopack;
using Velopack.Sources;

namespace EvilTwinsUpdater;

internal sealed class Settings
{
    public string RetailFolder { get; set; } = "";
    public bool Automatic { get; set; } = true;
    public string SimulatorPath { get; set; } = "";
}

internal sealed class MainForm : Form
{
    private const string Repository = "KatanoDK/EvilTwinsCommandCenter";
    private readonly string stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EvilTwinsUpdater");
    private readonly TextBox folder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly CheckBox automatic = new() { Text = "Automatically check and install updates every 15 minutes", AutoSize = true };
    private readonly Label installed = new() { AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(720, 0), UseMnemonic = false, Tag = "muted" };
    private readonly Button browse = new ModernButton { Text = "Choose folder", AutoSize = true };
    private readonly Button check = new ModernButton { Text = "Check / update now", AutoSize = true, Tag = "primary" };
    private readonly Button local = new ModernButton { Text = "Install ZIP", AutoSize = true };
    private readonly Button rollback = new ModernButton { Text = "Restore previous build", AutoSize = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 * 60 * 1000 };
    private readonly GitHubFeed feed = new();
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "Evil Twins Updater" };
    private readonly UpdateManager appUpdates = new(new SimpleWebSource("https://github.com/KatanoDK/EvilTwinsCommandCenter/releases/download/updater/"));
    private readonly Label appStatus = new() { UseMnemonic = false, AutoSize = true, MaximumSize = new Size(550, 0), Tag = "muted" };
    private readonly Button updateApp = new ModernButton { Text = "Update app && restart", AutoSize = true, Enabled = false, Tag = "primary" };
    private UpdateInfo? availableApp;
    private readonly DroptimizerClient droptimizer = new();
    private readonly TextBox reportLink = new() { Dock = DockStyle.Fill, PlaceholderText = "Paste a completed Droptimizer report link" };
    private readonly Button fetchReport = new ModernButton { Text = "Import report", AutoSize = true, Tag = "primary" };
    private readonly Button copyReport = new ModernButton { Text = "Copy addon import code", AutoSize = true, Enabled = false, Tag = "primary" };
    private readonly TextBox reportPreview = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label reportStatus = new() { UseMnemonic = false, AutoSize = true, MaximumSize = new Size(750, 0), Tag = "muted" };
    private readonly DataGridView reportItems = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill };
    private readonly Label pageTitle = AppTheme.Label("", 23, true);
    private readonly Label pageDescription = AppTheme.Label("", 10, muted: true);
    private readonly ModernButton[] navigation;
    private readonly Control[] pages;
    private DropReport? importedReport;
    private Settings settings = new();
    private bool busy;
    private bool pausedAfterRollback;
    private readonly RaidPanel simulation;

    public MainForm(bool preview = false)
    {
        Text = "Evil Twins — Guild Tools";
        ClientSize = new Size(1200, 920);
        MinimumSize = new Size(1080, 820);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        simulation = new RaidPanel(stateDirectory, preview);
        simulation.EngineChanged += SaveSettings;
        simulation.RunningChanged += () => updateApp.Enabled = !busy && !simulation.IsRunning && availableApp != null;
        pages = [CreateUpdatesPage(), CreateReportsPage(), simulation];
        foreach (var page in pages) { page.Dock = DockStyle.Fill; pageHost.Controls.Add(page); }
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var sidebar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 30, 18, 24) };
        var brand = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        brand.Controls.Add(AppTheme.Label("ET", 28, true), 0, 0);
        brand.Controls.Add(AppTheme.Label("EVIL TWINS", 12, true), 0, 1);
        brand.Controls.Add(AppTheme.Label("GUILD TOOLS", 8, muted: true), 0, 2);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, AutoSize = true,
            WrapContents = false, Padding = new Padding(0, 26, 0, 0) };
        navigation = [new ModernButton { Text = "Updates" }, new ModernButton { Text = "Droptimizer" }, new ModernButton { Text = "Raid upgrades" }];
        for (var index = 0; index < navigation.Length; index++)
        {
            var target = index; var button = navigation[index]; button.Tag = "navigation";
            button.Size = new Size(154, 46); button.TextAlign = ContentAlignment.MiddleLeft; button.Margin = new Padding(0, 0, 0, 8);
            button.Click += (_, _) => SelectPage(target); nav.Controls.Add(button);
        }
        var sidebarTop = new Panel { Dock = DockStyle.Top, Height = 310 };
        sidebarTop.Controls.Add(nav); sidebarTop.Controls.Add(brand);
        sidebar.Controls.Add(sidebarTop);
        var sidebarFooter = AppTheme.Label("COMMAND CENTER\nGuild companion · 0.1.0", 8, muted: true);
        sidebarFooter.Dock = DockStyle.Bottom; sidebar.Controls.Add(sidebarFooter);
        shell.Controls.Add(sidebar, 0, 0);
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 26, 28, 22), ColumnCount = 1, RowCount = 3 };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize)); main.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 0, 0, 18) };
        heading.Controls.Add(pageTitle, 0, 0); heading.Controls.Add(pageDescription, 0, 1);
        main.Controls.Add(heading, 0, 0); main.Controls.Add(pageHost, 0, 1);
        var appBar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 18, 0, 0), ColumnCount = 2 };
        appBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        appBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        appBar.Controls.Add(appStatus, 0, 0); appBar.Controls.Add(updateApp, 1, 0);
        appStatus.Text = appUpdates.IsInstalled ? $"Updater {appUpdates.CurrentVersion}" : "Portable copy — install the Setup application to enable app updates.";
        main.Controls.Add(appBar, 0, 2); shell.Controls.Add(main, 1, 0); Controls.Add(shell);
        AppTheme.Apply(this); brand.Controls[0].ForeColor = AppTheme.Accent;
        SelectPage(0);
        HandleCreated += (_, _) => AppTheme.DarkTitleBar(this);
        updateApp.Click += async (_, _) => await UpdateApplicationAsync();
        fetchReport.Click += async (_, _) => await ImportReportAsync();
        copyReport.Click += (_, _) => {
            if (importedReport == null) return;
            try { Clipboard.SetText(importedReport.ImportCode()); reportStatus.Text = "Copied. In the addon, open Wishlist & upgrades → Import report and paste the code."; }
            catch (Exception error) { reportStatus.Text = error.Message; }
        };
        if (!preview) LoadSettings();
        simulation.SetEnginePath(settings.SimulatorPath);
        folder.Text = settings.RetailFolder;
        automatic.Checked = settings.Automatic;
        tray.Visible = !preview;
        browse.Click += (_, _) => ChooseFolder();
        check.Click += async (_, _) => await UpdateAsync();
        local.Click += (_, _) => InstallLocal();
        rollback.Click += (_, _) => Restore();
        automatic.CheckedChanged += (_, _) => { pausedAfterRollback = false; SaveSettings(); };
        timer.Tick += async (_, _) => { if (automatic.Checked && !pausedAfterRollback) await UpdateAsync(); };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open updater", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) => Close());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowWindow();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        Shown += async (_, _) =>
        {
            if (preview) return;
            RefreshBuild();
            timer.Start();
            if (automatic.Checked) await UpdateAsync();
            else status.Text = "Updates run while this app is open or minimized to the tray. Closing the app stops checks.";
        };
        FormClosed += (_, _) => { timer.Dispose(); tray.Dispose(); feed.Dispose(); droptimizer.Dispose(); };
        FormClosing += async (_, args) => {
            if (!simulation.IsRunning) return;
            args.Cancel = true;
            await simulation.StopAsync();
            Close();
        };
        if (preview)
        {
            RefreshBuild();
            status.Text = "Updates run while this app is open or minimized to the tray. Closing the app stops checks.";
        }
    }

    public void SelectPage(int index)
    {
        if (index < 0 || index >= pages.Length) return;
        var titles = new[] { "Addon updates", "Droptimizer reports", "Raid upgrades" };
        var descriptions = new[] { "Keep the guild addon ready for your next session.",
            "Bring completed Raidbots upgrade reports into the command center.", "Collect raid DPS profiles and find who benefits most from each drop." };
        pageTitle.Text = titles[index]; pageDescription.Text = descriptions[index];
        for (var page = 0; page < pages.Length; page++)
        {
            pages[page].Visible = page == index; navigation[page].Selected = page == index; navigation[page].Invalidate();
        }
        pages[index].BringToFront();
    }

    private static TableLayoutPanel Stack(params Control[] controls)
    {
        var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = controls.Length };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < controls.Length; index++) { stack.RowStyles.Add(new RowStyle(SizeType.AutoSize)); stack.Controls.Add(controls[index], 0, index); }
        return stack;
    }

    private static CardPanel Card(params Control[] controls)
    {
        var card = new CardPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        card.Controls.Add(Stack(controls)); return card;
    }

    private static TableLayoutPanel InputRow(Control input, Control action)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.Margin = new Padding(0, 7, 12, 0); action.Margin = new Padding(0);
        row.Controls.Add(input, 0, 0); row.Controls.Add(action, 1, 0); return row;
    }

    private Control CreateUpdatesPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 3; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var game = Card(AppTheme.Label("World of Warcraft", 13, true), AppTheme.Label("Retail installation folder", muted: true),
            InputRow(folder, browse), AppTheme.Label("Updates can install while WoW is open. Use /reload in-game afterward.", 9, muted: true));
        automatic.Margin = new Padding(0, 2, 0, 16);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 0) };
        actions.Controls.Add(check);
        var updates = Card(AppTheme.Label("Stay up to date", 13, true), automatic, actions);
        installed.Font = new Font("Segoe UI", 12, FontStyle.Bold); installed.Margin = new Padding(0, 0, 0, 12);
        status.Margin = new Padding(0, 0, 0, 18);
        var extras = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        extras.Controls.AddRange([local, rollback]);
        var current = Card(AppTheme.Label("Addon status", 9, muted: true), installed, status, extras);
        layout.Controls.Add(game, 0, 0); layout.Controls.Add(updates, 0, 1); layout.Controls.Add(current, 0, 2);
        return layout;
    }

    private Control CreateReportsPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(Card(AppTheme.Label("Completed report", 13, true),
            AppTheme.Label("Paste a Droptimizer link to see the character's simulated upgrades.", 9, muted: true), InputRow(reportLink, fetchReport)), 0, 0);
        var resultsCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 0, 0, 16) };
        var results = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        results.RowStyles.Add(new RowStyle(SizeType.AutoSize)); results.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); results.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        results.Controls.Add(AppTheme.Label("Simulated upgrades", 13, true), 0, 0);
        reportPreview.BorderStyle = BorderStyle.None; reportPreview.PlaceholderText = "Import a completed report to view the character and ranked upgrades.";
        results.Controls.Add(reportPreview, 0, 1);
        foreach (var (title, weight) in new[] { ("Item", 190), ("Source", 140), ("Difficulty", 80), ("ilvl", 45), ("DPS gain", 70), ("Gain %", 65) })
            reportItems.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        reportItems.Columns[0].DefaultCellStyle.WrapMode = reportItems.Columns[1].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        results.Controls.Add(reportItems, 0, 2); resultsCard.Controls.Add(results); layout.Controls.Add(resultsCard, 0, 1);
        var footer = Stack(copyReport, reportStatus); copyReport.Margin = new Padding(0, 0, 0, 12);
        reportStatus.Text = "Copy the import code, then paste it into Wishlist & upgrades → Import report in the addon.";
        layout.Controls.Add(footer, 0, 2); return layout;
    }

    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private static bool GameRunning()
    {
        foreach (var name in new[] { "Wow", "Wow-64", "WowB", "WowT" })
        {
            var processes = Process.GetProcessesByName(name);
            var found = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (found) return true;
        }
        return false;
    }

    private void ReportInstallation(string message)
    {
        var running = GameRunning();
        status.Text = message + (running ? " Type /reload in WoW to load it when you're ready." : "");
        if (running)
            tray.ShowBalloonTip(10000, "Evil Twins addon files updated", "Type /reload in WoW when you're ready to load the installed build.", ToolTipIcon.Info);
    }

    private void LoadSettings()
    {
        try
        {
            var path = Path.Combine(stateDirectory, "settings.json");
            if (File.Exists(path)) settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new();
        }
        catch { settings = new(); }
    }

    private void SaveSettings()
    {
        try
        {
            settings = new Settings { RetailFolder = folder.Text, Automatic = automatic.Checked, SimulatorPath = simulation.EnginePath };
            Directory.CreateDirectory(stateDirectory);
            var path = Path.Combine(stateDirectory, "settings.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception error) { status.Text = "Could not save settings: " + error.Message; }
    }

    private void ChooseFolder()
    {
        using var picker = new FolderBrowserDialog { Description = "Select World of Warcraft's _retail_ folder", UseDescriptionForTitle = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            folder.Text = AddonInstaller.ValidateRetail(picker.SelectedPath);
            pausedAfterRollback = false;
            SaveSettings(); RefreshBuild();
            status.Text = "Folder saved. Click Check / update now to install the latest public build.";
        }
        catch (Exception error) { status.Text = error.Message; }
    }

    private void RefreshBuild()
    {
        if (folder.Text.Length == 0) { installed.Text = "Choose a Retail folder to get started."; rollback.Enabled = false; return; }
        try
        {
            var build = AddonInstaller.InstalledBuild(folder.Text);
            installed.Text = build == null ? "No managed build installed." : $"Installed: {build.Version} • build {build.Commit[..Math.Min(8, build.Commit.Length)]}";
            rollback.Enabled = !busy && AddonInstaller.HasBackup(folder.Text);
        }
        catch (Exception error) { installed.Text = error.Message; rollback.Enabled = false; }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        browse.Enabled = check.Enabled = local.Enabled = automatic.Enabled = fetchReport.Enabled = !value;
        updateApp.Enabled = !value && !simulation.IsRunning && availableApp != null;
        rollback.Enabled = false;
    }

    private async Task UpdateAsync()
    {
        if (busy) return;
        string? download = null;
        try
        {
            SetBusy(true); SaveSettings();
            if (folder.Text.Length == 0) { status.Text = "Choose a Retail folder to enable addon updates."; return; }
            status.Text = "Checking for a new addon build…";
            var build = await feed.LatestAsync(Repository);
            var current = AddonInstaller.InstalledBuild(folder.Text);
            if (current?.Commit == build.Commit) { status.Text = "Your addon is up to date."; return; }
            Directory.CreateDirectory(stateDirectory);
            download = Path.Combine(stateDirectory, "download-" + Guid.NewGuid().ToString("N") + ".zip");
            status.Text = "Downloading and verifying the addon…";
            await feed.DownloadAsync(build, download);
            AddonInstaller.Install(folder.Text, download, build.Commit);
            pausedAfterRollback = false;
            ReportInstallation("Updated successfully. The previous addon build is available to restore.");
        }
        catch (Exception error) { status.Text = error.Message; }
        finally
        {
            if (download != null && File.Exists(download)) File.Delete(download);
            await CheckApplicationAsync();
            SetBusy(false); RefreshBuild();
        }
    }

    private async Task CheckApplicationAsync()
    {
        if (!appUpdates.IsInstalled) return;
        try
        {
            availableApp = await appUpdates.CheckForUpdatesAsync();
            appStatus.Text = availableApp == null ? $"Updater {appUpdates.CurrentVersion} — up to date."
                : $"App update available: {availableApp.TargetFullRelease.Version}";
        }
        catch (Exception error) { appStatus.Text = "App update check failed: " + error.Message; }
    }

    private async Task UpdateApplicationAsync()
    {
        if (busy || simulation.IsRunning || availableApp == null || !appUpdates.IsInstalled) return;
        try
        {
            simulation.SetApplicationUpdating(true);
            SetBusy(true); SaveSettings();
            appStatus.Text = "Downloading and verifying the app update…";
            await appUpdates.DownloadUpdatesAsync(availableApp);
            appStatus.Text = "Restarting the updater…";
            appUpdates.ApplyUpdatesAndRestart(availableApp);
        }
        catch (Exception error) { appStatus.Text = "App update failed: " + error.Message; }
        finally { simulation.SetApplicationUpdating(false); SetBusy(false); RefreshBuild(); }
    }

    private async Task ImportReportAsync()
    {
        if (busy) return;
        try
        {
            SetBusy(true); importedReport = null; copyReport.Enabled = false; reportItems.Rows.Clear(); reportPreview.Clear();
            reportStatus.Text = "Reading the completed report…";
            var report = await droptimizer.FetchAsync(reportLink.Text);
            _ = report.ImportCode();
            importedReport = report;
            ShowImportedReport(report);
            copyReport.Enabled = true;
            reportStatus.Text = $"{report.Items.Count} positive upgrades (maximum 100). Copy the code and import it into the addon. Results apply to this report's build and assumptions.";
        }
        catch (Exception error) { reportPreview.Clear(); reportStatus.Text = "Could not import: " + error.Message; }
        finally { SetBusy(false); RefreshBuild(); }
    }

    private void ShowImportedReport(DropReport report)
    {
        reportPreview.Text = $"{report.Name} — {report.Realm} ({report.Region.ToUpperInvariant()})\r\n{report.Title}\r\n"
            + $"{report.FightStyle} • {report.Duration}s • {report.Enemies} target(s) • {report.Assumptions}"
            + (report.Items.Count == 0 ? "\r\nNo positive upgrades in this report." : "");
        reportItems.Rows.Clear();
        foreach (var item in report.Items)
        {
            var difficulty = item.Difficulty.Replace("raid-", "");
            if (difficulty.Length > 0) difficulty = char.ToUpperInvariant(difficulty[0]) + difficulty[1..];
            var index = reportItems.Rows.Add(item.Name, item.Boss, difficulty, item.ItemLevel, $"+{item.Gain:N0}", $"+{item.Percent:F2}");
            var row = reportItems.Rows[index]; row.Cells[4].Style.ForeColor = item.WithinError ? AppTheme.Muted : AppTheme.Success;
            row.Cells[0].ToolTipText = (item.DropId != item.SimulatedId ? $"Convert to tier ({item.SimulatedId}). " : "")
                + (item.WithinError ? "Within simulation uncertainty. " : "") + $"Drop item #{item.DropId}.";
        }
        reportItems.ClearSelection();
    }

    private void InstallLocal()
    {
        if (folder.Text.Length == 0) { status.Text = "Choose your Retail folder first."; return; }
        using var picker = new OpenFileDialog { Filter = "Addon ZIP (*.zip)|*.zip", Title = "Choose an Evil Twins addon build" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { SetBusy(true); AddonInstaller.Install(folder.Text, picker.FileName); ReportInstallation("Test ZIP installed successfully."); }
        catch (Exception error)
        {
            status.Text = error.Message;
            try
            {
                Directory.CreateDirectory(stateDirectory);
                File.WriteAllText(Path.Combine(stateDirectory, "last-install-error.txt"),
                    $"Time: {DateTimeOffset.Now:O}\nZIP: {picker.FileName}\nRetail: {folder.Text}\n{error}\n");
            }
            catch { /* Keep the original installation error visible if the log cannot be written. */ }
        }
        finally { SetBusy(false); RefreshBuild(); }
    }

    private void Restore()
    {
        try
        {
            AddonInstaller.Rollback(folder.Text);
            pausedAfterRollback = true;
            automatic.Checked = false;
            SaveSettings(); RefreshBuild();
            ReportInstallation("Previous build restored. Automatic updates are off so it stays installed.");
        }
        catch (Exception error) { status.Text = error.Message; }
    }
}
