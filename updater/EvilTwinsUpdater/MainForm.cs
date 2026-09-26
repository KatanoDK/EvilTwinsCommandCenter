using System.Diagnostics;
using System.Text.Json;
using EvilTwinsUpdater.Core;

namespace EvilTwinsUpdater;

internal sealed class Settings
{
    public string RetailFolder { get; set; } = "";
    public string Repository { get; set; } = "KatanoDK/EvilTwinsCommandCenter";
    public bool Automatic { get; set; } = true;
}

internal sealed class MainForm : Form
{
    private readonly string stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EvilTwinsUpdater");
    private readonly TextBox folder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox repository = new() { Dock = DockStyle.Fill };
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
    private Settings settings = new();
    private bool busy;
    private bool pausedAfterRollback;

    public MainForm(bool preview = false)
    {
        Text = "Evil Twins — Addon Updater";
        ClientSize = new Size(690, 390);
        MinimumSize = new Size(710, 430);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 9 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Full(Control control, int row) { layout.Controls.Add(control, 0, row); layout.SetColumnSpan(control, 2); }
        Full(new Label { Text = "EVIL TWINS COMMAND CENTER", Font = new Font(Font, FontStyle.Bold), AutoSize = true }, 0);
        Full(new Label { Text = "Choose your Retail folder once. Updates install when WoW is closed.", AutoSize = true, Margin = new Padding(0, 8, 0, 15) }, 1);
        layout.Controls.Add(folder, 0, 2); layout.Controls.Add(browse, 1, 2);
        Full(new Label { Text = "Public GitHub downloads repository (owner/repository)", AutoSize = true, Margin = new Padding(0, 12, 0, 3) }, 3);
        Full(repository, 4);
        automatic.Margin = new Padding(0, 15, 0, 10); Full(automatic, 5);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        actions.Controls.AddRange([check, local, rollback]); Full(actions, 6);
        installed.Margin = new Padding(0, 12, 0, 5); Full(installed, 7);
        Full(status, 8);
        Controls.Add(layout);
        if (!preview) LoadSettings();
        folder.Text = settings.RetailFolder;
        repository.Text = settings.Repository;
        automatic.Checked = settings.Automatic;
        tray.Visible = !preview;
        browse.Click += (_, _) => ChooseFolder();
        check.Click += async (_, _) => await UpdateAsync();
        local.Click += (_, _) => InstallLocal();
        rollback.Click += (_, _) => Restore();
        automatic.CheckedChanged += (_, _) => { pausedAfterRollback = false; SaveSettings(); };
        repository.Leave += (_, _) => SaveSettings();
        timer.Tick += async (_, _) => { if (automatic.Checked && !pausedAfterRollback) await UpdateAsync(); };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open updater", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) => Close());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowWindow();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        Shown += async (_, _) =>
        {
            RefreshBuild();
            timer.Start();
            if (folder.Text.Length > 0 && automatic.Checked) await UpdateAsync();
            else status.Text = "Updates run while this app is open or minimized to the tray. Closing the app stops checks.";
        };
        FormClosed += (_, _) => { timer.Dispose(); tray.Dispose(); feed.Dispose(); };
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
            settings = new Settings { RetailFolder = folder.Text, Repository = repository.Text.Trim(), Automatic = automatic.Checked };
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
        browse.Enabled = check.Enabled = local.Enabled = repository.Enabled = automatic.Enabled = !value;
        rollback.Enabled = false;
    }

    private async Task UpdateAsync()
    {
        if (busy || folder.Text.Length == 0) return;
        string? download = null;
        try
        {
            SetBusy(true); SaveSettings(); status.Text = "Checking for a new addon build…";
            var build = await feed.LatestAsync(repository.Text);
            var current = AddonInstaller.InstalledBuild(folder.Text);
            if (current?.Commit == build.Commit) { status.Text = "Your addon is up to date."; return; }
            if (GameRunning()) { status.Text = "A new build is available. It will install after WoW closes, at the next check."; return; }
            Directory.CreateDirectory(stateDirectory);
            download = Path.Combine(stateDirectory, "download-" + Guid.NewGuid().ToString("N") + ".zip");
            status.Text = "Downloading and verifying the addon…";
            await feed.DownloadAsync(build, download);
            AddonInstaller.Install(folder.Text, download, GameRunning, build.Commit);
            pausedAfterRollback = false;
            status.Text = "Updated successfully. The previous addon build is available to restore.";
        }
        catch (Exception error) { status.Text = error.Message; }
        finally
        {
            if (download != null && File.Exists(download)) File.Delete(download);
            SetBusy(false); RefreshBuild();
        }
    }

    private void InstallLocal()
    {
        if (folder.Text.Length == 0) { status.Text = "Choose your Retail folder first."; return; }
        using var picker = new OpenFileDialog { Filter = "Addon ZIP (*.zip)|*.zip", Title = "Choose an Evil Twins addon build" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { SetBusy(true); AddonInstaller.Install(folder.Text, picker.FileName, GameRunning); status.Text = "Test ZIP installed successfully."; }
        catch (Exception error) { status.Text = error.Message; }
        finally { SetBusy(false); RefreshBuild(); }
    }

    private void Restore()
    {
        try
        {
            AddonInstaller.Rollback(folder.Text, GameRunning);
            pausedAfterRollback = true;
            automatic.Checked = false;
            SaveSettings(); RefreshBuild();
            status.Text = "Previous build restored. Automatic updates are off so it stays installed.";
        }
        catch (Exception error) { status.Text = error.Message; }
    }
}
