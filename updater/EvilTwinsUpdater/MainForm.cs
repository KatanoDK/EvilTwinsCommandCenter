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
}

internal sealed class MainForm : Form
{
    private const string Repository = "KatanoDK/EvilTwinsCommandCenter";
    private readonly string stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EvilTwinsUpdater");
    private readonly TextBox folder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly CheckBox automatic = new() { Text = "Automatically check and install updates every 15 minutes", AutoSize = true };
    private readonly Label installed = new() { AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(620, 0) };
    private readonly Button browse = new() { Text = "Choose folder", AutoSize = true };
    private readonly Button check = new() { Text = "Check / update now", AutoSize = true };
    private readonly Button local = new() { Text = "Install ZIP", AutoSize = true };
    private readonly Button rollback = new() { Text = "Restore previous build", AutoSize = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 * 60 * 1000 };
    private readonly GitHubFeed feed = new();
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "Evil Twins Updater" };
    private readonly UpdateManager appUpdates = new(new SimpleWebSource("https://github.com/KatanoDK/EvilTwinsCommandCenter/releases/download/updater/"));
    private readonly Label appStatus = new() { UseMnemonic = false, AutoSize = true, MaximumSize = new Size(480, 0) };
    private readonly Button updateApp = new() { Text = "Update app && restart", Size = new Size(180, 32), Enabled = false };
    private UpdateInfo? availableApp;
    private readonly DroptimizerClient droptimizer = new();
    private readonly TextBox reportLink = new() { Dock = DockStyle.Fill, PlaceholderText = "Paste a completed Droptimizer report link" };
    private readonly Button fetchReport = new() { Text = "Import report", AutoSize = true };
    private readonly Button copyReport = new() { Text = "Copy addon import code", AutoSize = true, Enabled = false };
    private readonly TextBox reportPreview = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label reportStatus = new() { UseMnemonic = false, AutoSize = true, MaximumSize = new Size(620, 0) };
    private DropReport? importedReport;
    private Settings settings = new();
    private bool busy;
    private bool pausedAfterRollback;

    public MainForm(bool preview = false)
    {
        Text = "Evil Twins — Addon Updater";
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(740, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Full(Control control, int row) { layout.Controls.Add(control, 0, row); layout.SetColumnSpan(control, 2); }
        Full(new Label { Text = "EVIL TWINS COMMAND CENTER", Font = new Font(Font, FontStyle.Bold), AutoSize = true }, 0);
        Full(new Label { Text = "Updates can install while WoW is open. Type /reload in-game afterward.", AutoSize = true, Margin = new Padding(0, 8, 0, 15) }, 1);
        layout.Controls.Add(folder, 0, 2); layout.Controls.Add(browse, 1, 2);
        automatic.Margin = new Padding(0, 15, 0, 10); Full(automatic, 3);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        actions.Controls.AddRange([check, local, rollback]); Full(actions, 4);
        installed.Margin = new Padding(0, 12, 0, 5); Full(installed, 5);
        Full(status, 6);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var addonTab = new TabPage("Addon updates");
        addonTab.Controls.Add(layout);
        tabs.TabPages.Add(addonTab);
        var reportsTab = new TabPage("Droptimizer");
        var reports = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
        reports.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        reports.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        reports.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        reports.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        reports.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        reports.Controls.Add(new Label { Text = "Import simulated upgrades for the guild's leaders.", AutoSize = true, Margin = new Padding(0, 0, 0, 12) }, 0, 0);
        var reportActions = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        reportActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        reportActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        reportActions.Controls.Add(reportLink, 0, 0); reportActions.Controls.Add(fetchReport, 1, 0);
        reports.Controls.Add(reportActions, 0, 1);
        reports.Controls.Add(reportPreview, 0, 2);
        reports.Controls.Add(copyReport, 0, 3);
        reports.Controls.Add(reportStatus, 0, 4);
        reportStatus.Text = "Copy the import code, then paste it into Wishlist & upgrades in the addon. The addon saves and shares imported reports with online guild leaders.";
        reportsTab.Controls.Add(reports); tabs.TabPages.Add(reportsTab);
        var appBar = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(20, 12, 20, 12), ColumnCount = 2 };
        appBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        appBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        appBar.Controls.Add(appStatus, 0, 0); appBar.Controls.Add(updateApp, 1, 0);
        appStatus.Text = appUpdates.IsInstalled ? $"Updater {appUpdates.CurrentVersion}" : "Portable copy — install the Setup application to enable app updates.";
        Controls.Add(tabs); Controls.Add(appBar);
        updateApp.Click += async (_, _) => await UpdateApplicationAsync();
        fetchReport.Click += async (_, _) => await ImportReportAsync();
        copyReport.Click += (_, _) => {
            if (importedReport == null) return;
            try { Clipboard.SetText(importedReport.ImportCode()); reportStatus.Text = "Copied. In the addon, open Wishlist & upgrades → Import report and paste the code."; }
            catch (Exception error) { reportStatus.Text = error.Message; }
        };
        if (!preview) LoadSettings();
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
        if (preview)
        {
            RefreshBuild();
            status.Text = "Updates run while this app is open or minimized to the tray. Closing the app stops checks.";
        }
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
            settings = new Settings { RetailFolder = folder.Text, Automatic = automatic.Checked };
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
        updateApp.Enabled = !value && availableApp != null;
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
        if (busy || availableApp == null || !appUpdates.IsInstalled) return;
        try
        {
            SetBusy(true); SaveSettings();
            appStatus.Text = "Downloading and verifying the app update…";
            await appUpdates.DownloadUpdatesAsync(availableApp);
            appStatus.Text = "Restarting the updater…";
            appUpdates.ApplyUpdatesAndRestart(availableApp);
        }
        catch (Exception error) { appStatus.Text = "App update failed: " + error.Message; }
        finally { SetBusy(false); RefreshBuild(); }
    }

    private async Task ImportReportAsync()
    {
        if (busy) return;
        try
        {
            SetBusy(true); importedReport = null; copyReport.Enabled = false;
            reportStatus.Text = "Reading the completed report…";
            var report = await droptimizer.FetchAsync(reportLink.Text);
            _ = report.ImportCode();
            importedReport = report;
            reportPreview.Text = $"{report.Name} — {report.Realm} ({report.Region.ToUpperInvariant()})\r\n{report.Title}\r\n"
                + $"{report.FightStyle} • {report.Duration}s • {report.Enemies} target(s)\r\n{report.Assumptions}\r\n\r\n"
                + (report.Items.Count == 0 ? "No positive upgrades in this report."
                : string.Join("\r\n\r\n", report.Items.Select((item, index) =>
                    $"{index + 1}. {item.Name} — ilvl {item.ItemLevel}\r\n"
                    + $"   {item.Boss} • {item.Difficulty} • +{item.Gain:N0} DPS (+{item.Percent:F2}%)"
                    + (item.DropId != item.SimulatedId ? $" • Convert to tier ({item.SimulatedId})" : "")
                    + (item.WithinError ? " • Within sim uncertainty" : ""))));
            copyReport.Enabled = true;
            reportStatus.Text = $"{report.Items.Count} positive upgrades (maximum 100). Copy the code and import it into the addon. Results apply to this report's build and assumptions.";
        }
        catch (Exception error) { reportPreview.Clear(); reportStatus.Text = "Could not import: " + error.Message; }
        finally { SetBusy(false); RefreshBuild(); }
    }

    private void InstallLocal()
    {
        if (folder.Text.Length == 0) { status.Text = "Choose your Retail folder first."; return; }
        using var picker = new OpenFileDialog { Filter = "Addon ZIP (*.zip)|*.zip", Title = "Choose an Evil Twins addon build" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { SetBusy(true); AddonInstaller.Install(folder.Text, picker.FileName); ReportInstallation("Test ZIP installed successfully."); }
        catch (Exception error) { status.Text = error.Message; }
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
