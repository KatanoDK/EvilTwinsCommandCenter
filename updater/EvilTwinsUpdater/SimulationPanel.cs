using System.Diagnostics;
using System.Text;
using EvilTwinsUpdater.Core;

namespace EvilTwinsUpdater;

internal sealed class SimulationPanel : UserControl
{
    private readonly string outputRoot;
    private readonly LocalSimulator simulator = new();
    private readonly TextBox engine = new() { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = "Choose SimulationCraft's simc.exe once" };
    private readonly Button choose = new() { Text = "Choose engine", AutoSize = true };
    private readonly Button download = new() { Text = "Get SimulationCraft", AutoSize = true };
    private readonly TextBox input = new() { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false,
        MaxLength = 100_000, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), PlaceholderText = "Paste the SimC export copied from the addon." };
    private readonly TextBox result = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };
    private readonly ListView bags = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true,
        FullRowSelect = true, ShowItemToolTips = true, HideSelection = false };
    private readonly Button loadBags = new() { Text = "Load bag items", AutoSize = true };
    private readonly Button selectBags = new() { Text = "Select first 20", AutoSize = true, Enabled = false };
    private readonly Button clearBags = new() { Text = "Clear selection", AutoSize = true, Enabled = false };
    private readonly Label bagStatus = new() { Text = "Paste an export, then load its bag items.", AutoSize = true, MaximumSize = new Size(630, 0) };
    private readonly DataGridView comparisons = new() { Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
    private readonly Label comparisonStatus = new() { Text = "Select bag items, then run to compare gear combinations.", AutoSize = true };
    private readonly TabControl inputTabs = new() { Dock = DockStyle.Fill };
    private readonly TabControl resultTabs = new() { Dock = DockStyle.Fill };
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 112 };
    private readonly ComboBox precision = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly ComboBox duration = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 65 };
    private readonly ComboBox targets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 50 };
    private readonly Button run = new() { Text = "Run simulation", AutoSize = true };
    private readonly Button cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly Button report = new() { Text = "Open report", AutoSize = true, Enabled = false };
    private readonly Button files = new() { Text = "Open run folder", AutoSize = true, Enabled = false };
    private readonly Label status = new() { UseMnemonic = false, AutoSize = true, MaximumSize = new Size(620, 0),
        Text = "Patchwerk • raid buffs • default class rotation" };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 6, Visible = false };
    private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 250 };
    private readonly StringBuilder console = new();
    private readonly Stopwatch elapsed = new();
    private CancellationTokenSource? cancellation;
    private Task? runningTask;
    private SimulationResult? lastResult;
    private int threadCount;
    private bool applicationUpdating;
    private bool changingSelection;
    private string? loadedExport;
    private SimulationProgress? runProgress;
    public string EnginePath => engine.Text;
    public bool IsRunning => cancellation != null;
    public event Action? EngineChanged;
    public event Action? RunningChanged;

    public SimulationPanel(string stateDirectory)
    {
        Dock = DockStyle.Fill;
        outputRoot = Path.Combine(stateDirectory, "simulations");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 7 };
        for (var row = 0; row < 7; row++) layout.RowStyles.Add(new RowStyle(row == 3 ? SizeType.Percent : SizeType.AutoSize, row == 3 ? 100 : 0));
        var instructions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        instructions.Controls.Add(new Label { Text = "Download and extract SimulationCraft, then select simc.exe.", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
        instructions.Controls.Add(download);
        layout.Controls.Add(instructions, 0, 0);
        var engineRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
        engineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        engineRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        engineRow.Controls.Add(engine, 0, 0); engineRow.Controls.Add(choose, 1, 0);
        layout.Controls.Add(engineRow, 0, 1);
        mode.Items.AddRange(["Background", "Full speed"]); mode.SelectedIndex = 0;
        precision.Items.AddRange(["Quick: 1,000", "Standard: 5,000", "Detailed: 20,000"]); precision.SelectedIndex = 1;
        duration.Items.AddRange(["180s", "300s", "600s"]); duration.SelectedIndex = 1;
        targets.Items.AddRange(["1", "3", "5"]); targets.SelectedIndex = 0;
        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8) };
        void Option(string label, Control control) {
            options.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            options.Controls.Add(control);
        }
        Option("CPU", mode); Option("Run", precision); Option("Fight", duration); Option("Targets", targets);
        layout.Controls.Add(options, 0, 2);
        var panes = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panes.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); panes.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var exportPage = new TabPage("Character export") { Padding = new Padding(6) };
        exportPage.Controls.Add(input); inputTabs.TabPages.Add(exportPage);
        var bagsPage = new TabPage("Bag gear") { Padding = new Padding(6) };
        var bagLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        bagLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bagLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bagLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bagLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var bagActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        bagActions.Controls.AddRange([loadBags, selectBags, clearBags]);
        bags.Columns.Add("Item / variant", 350); bags.Columns.Add("Slot", 85); bags.Columns.Add("Item ID", 75);
        var bagInstructions = new Label { Text = "Armor, rings and trinkets • Equipped items remain in the pool • Uses existing gems/enchants",
            AutoSize = true, MaximumSize = new Size(780, 0), Margin = new Padding(3, 0, 3, 6) };
        bagLayout.Controls.Add(bagActions, 0, 0); bagLayout.Controls.Add(bagInstructions, 0, 1);
        bagLayout.Controls.Add(bags, 0, 2); bagLayout.Controls.Add(bagStatus, 0, 3);
        bagsPage.Controls.Add(bagLayout); inputTabs.TabPages.Add(bagsPage);
        var outputPage = new TabPage("Result / engine output") { Padding = new Padding(6) };
        outputPage.Controls.Add(result); resultTabs.TabPages.Add(outputPage);
        var comparisonPage = new TabPage("Bag comparisons") { Padding = new Padding(6) };
        comparisons.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Gear combination", FillWeight = 180 });
        foreach (var label in new[] { "Slots", "DPS gain", "Gain %", "Gain ±", "Assessment" })
            comparisons.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = label, FillWeight = label == "Assessment" ? 130 : 70 });
        foreach (DataGridViewColumn column in comparisons.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        comparisons.Columns[0].DefaultCellStyle.WrapMode = comparisons.Columns[1].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        var comparisonLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        comparisonLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); comparisonLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        comparisonLayout.Controls.Add(comparisonStatus, 0, 0); comparisonLayout.Controls.Add(comparisons, 0, 1);
        comparisonPage.Controls.Add(comparisonLayout); resultTabs.TabPages.Add(comparisonPage);
        panes.Controls.Add(inputTabs, 0, 0); panes.Controls.Add(resultTabs, 0, 1);
        layout.Controls.Add(panes, 0, 3);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        actions.Controls.AddRange([run, cancel, report, files]);
        layout.Controls.Add(actions, 0, 4); layout.Controls.Add(status, 0, 5); layout.Controls.Add(progress, 0, 6);
        Controls.Add(layout);
        choose.Click += (_, _) => ChooseEngine();
        download.Click += (_, _) => Open("https://www.simulationcraft.org/download.html");
        run.Click += async (_, _) => { if (!IsRunning && !applicationUpdating) { runningTask = RunAsync(); await runningTask; } };
        cancel.Click += (_, _) => { cancel.Enabled = false; status.Text = "Stopping simulation…"; cancellation?.Cancel(); };
        report.Click += (_, _) => { if (lastResult != null) Open(lastResult.HtmlPath); };
        files.Click += (_, _) => { if (lastResult != null) Open(lastResult.Directory); };
        loadBags.Click += (_, _) => LoadBagItems();
        selectBags.Click += (_, _) => {
            changingSelection = true;
            var count = 0;
            foreach (ListViewItem row in bags.Items) row.Checked = row.Tag is BagItem item && item.CanCompare && count++ < BagComparisons.MaximumSelectedItems;
            changingSelection = false; UpdateBagStatus();
        };
        clearBags.Click += (_, _) => {
            changingSelection = true; foreach (ListViewItem row in bags.Items) row.Checked = false;
            changingSelection = false; UpdateBagStatus();
        };
        bags.ItemCheck += (_, args) => {
            if (changingSelection) return;
            if (args.NewValue == CheckState.Checked && (bags.Items[args.Index].Tag is not BagItem item || !item.CanCompare
                || bags.CheckedItems.Count >= BagComparisons.MaximumSelectedItems))
            {
                args.NewValue = args.CurrentValue;
                bagStatus.Text = "Select up to 20 available armor, ring or trinket variants. Hover a row for details.";
            }
            else BeginInvoke((Action)UpdateBagStatus);
        };
        input.TextChanged += (_, _) => {
            loadedExport = null; bags.Items.Clear(); selectBags.Enabled = clearBags.Enabled = false;
            bagStatus.Text = "Export changed. Load bag items to select comparisons.";
            lastResult = null; report.Enabled = files.Enabled = false; comparisons.Rows.Clear(); result.Clear();
            comparisonStatus.Text = "Export changed. Run again to compare its gear.";
            run.Enabled = !IsRunning && !applicationUpdating;
        };
        displayTimer.Tick += (_, _) => {
            if (cancellation?.IsCancellationRequested != true)
            {
                var estimate = runProgress?.EstimateRemaining(elapsed.Elapsed);
                status.Text = runProgress?.HasProgress == true
                    ? $"{runProgress.Fraction:P0} • {runProgress.CompletedRuns}/{runProgress.TotalRuns} runs complete • "
                        + (runProgress.Fraction >= 1 ? "Writing reports…" : estimate.HasValue ? $"~{FormatTime(estimate.Value)} remaining" : "Estimating time remaining…")
                        + $" • {FormatTime(elapsed.Elapsed)} elapsed"
                    : $"Starting • {threadCount} CPU thread(s) • {FormatTime(elapsed.Elapsed)} elapsed • Estimating time remaining…";
                if (runProgress?.HasProgress == true) { progress.Style = ProgressBarStyle.Continuous; progress.Value = Math.Clamp((int)(runProgress.Fraction * 100), 0, 99); }
            }
            result.Text = console.ToString(); result.SelectionStart = result.TextLength; result.ScrollToCaret();
        };
    }

    private static string FormatTime(TimeSpan time) => time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m"
        : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m {time.Seconds}s" : $"{(int)Math.Ceiling(time.TotalSeconds)}s";

    public void SetEnginePath(string path) => engine.Text = path;

    public void SetApplicationUpdating(bool value)
    {
        applicationUpdating = value;
        run.Enabled = !value && !IsRunning;
        choose.Enabled = !value && !IsRunning;
        loadBags.Enabled = !value && !IsRunning;
        if (!value) UpdateBagStatus();
    }

    private void ChooseEngine()
    {
        using var picker = new OpenFileDialog { Title = "Choose the SimulationCraft command-line engine", Filter = "SimulationCraft engine (simc.exe)|simc.exe" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { engine.Text = LocalSimulator.ValidateEngine(picker.FileName); EngineChanged?.Invoke(); status.Text = "Engine saved. Paste a character export and click Run simulation."; }
        catch (Exception error) { status.Text = error.Message; }
    }

    private void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception error) { status.Text = "Could not open: " + error.Message; }
    }

    private void LoadBagItems()
    {
        try
        {
            var items = BagComparisons.ReadItems(input.Text);
            changingSelection = true; bags.Items.Clear();
            foreach (var item in items)
            {
                var row = new ListViewItem(item.Name) { Tag = item, ForeColor = item.CanCompare ? SystemColors.WindowText : SystemColors.GrayText,
                    ToolTipText = (item.CanCompare ? "Tests: " + string.Join(", ", item.ReplaceSlots) : item.UnavailableReason) + "\n" + item.Value };
                row.SubItems.Add(item.Slot); row.SubItems.Add(item.ItemId.ToString()); bags.Items.Add(row);
            }
            loadedExport = input.Text; selectBags.Enabled = clearBags.Enabled = items.Any(item => item.CanCompare);
            inputTabs.SelectedIndex = 1; UpdateBagStatus();
        }
        catch (Exception error) { loadedExport = null; bags.Items.Clear(); selectBags.Enabled = clearBags.Enabled = false; bagStatus.Text = error.Message; }
        finally { changingSelection = false; }
    }

    private void UpdateBagStatus()
    {
        if (loadedExport == null) return;
        var items = bags.Items.Cast<ListViewItem>().Select(row => (BagItem)row.Tag!).ToArray();
        var selection = bags.CheckedItems.Cast<ListViewItem>().Select(row => ((BagItem)row.Tag!).Key).ToArray();
        string count;
        try { count = $"{BagComparisons.CreateSets(loadedExport, selection).Count} combinations + equipped baseline"; run.Enabled = !IsRunning && !applicationUpdating; }
        catch (InvalidOperationException error) { bagStatus.Text = error.Message; run.Enabled = false; return; }
        bagStatus.Text = $"{bags.CheckedItems.Count}/20 selected • {count}"
            + (items.Any(item => !item.CanCompare) ? " • Gray rows cannot be compared; hover for details." : "")
            + (items.Length == 0 ? " • No gear found in the export's bag section." : "");
    }

    private async Task RunAsync()
    {
        var options = new SimulationOptions(new[] { 1000, 5000, 20_000 }[precision.SelectedIndex],
            new[] { 180, 300, 600 }[duration.SelectedIndex], new[] { 1, 3, 5 }[targets.SelectedIndex],
            mode.SelectedIndex == 0 ? SimulationMode.Background : SimulationMode.FullSpeed);
        using var tokenSource = new CancellationTokenSource();
        cancellation = tokenSource;
        var selected = loadedExport == input.Text ? bags.CheckedItems.Cast<ListViewItem>().Select(row => ((BagItem)row.Tag!).Key).ToArray() : [];
        choose.Enabled = download.Enabled = run.Enabled = input.Enabled = mode.Enabled = precision.Enabled = duration.Enabled = targets.Enabled
            = loadBags.Enabled = selectBags.Enabled = clearBags.Enabled = bags.Enabled = false;
        cancel.Enabled = true; report.Enabled = files.Enabled = false;
        lastResult = null; console.Clear(); result.Clear(); comparisons.Rows.Clear(); resultTabs.SelectedIndex = 0; threadCount = options.Threads;
        comparisonStatus.Text = "Simulation is running. Results appear when all gear combinations finish.";
        runProgress = null; progress.Value = 0;
        progress.Style = ProgressBarStyle.Marquee; progress.Visible = true; elapsed.Restart(); displayTimer.Start();
        RunningChanged?.Invoke();
        try
        {
            runProgress = new SimulationProgress(BagComparisons.CreateSets(input.Text, selected).Count + 1);
            var output = new Progress<string>(text => {
                if (cancellation != tokenSource) return;
                runProgress.Append(text);
                console.Append(text);
                if (console.Length > 16_000) console.Remove(0, console.Length - 16_000);
            });
            lastResult = await simulator.RunAsync(engine.Text, input.Text, options, outputRoot, output, tokenSource.Token, selected);
            displayTimer.Stop();
            result.Text = $"{lastResult.Name} — {lastResult.Specialization}\r\n"
                + $"{lastResult.Dps:N0} DPS"
                + (lastResult.Error.HasValue ? $" ± {lastResult.Error.Value:N0} ({lastResult.Confidence:P0} confidence)" : " • Error unavailable")
                + $"\r\n{lastResult.Iterations:N0} iterations • {lastResult.Threads} CPU thread(s) • {lastResult.Elapsed.TotalSeconds:F1}s\r\n"
                + $"SimulationCraft {lastResult.EngineVersion} • {lastResult.EngineRevision}\r\n"
                + $"Patchwerk • {options.Duration}s average • {options.Targets} target(s) • raid buffs • default rotation\r\n"
                + (lastResult.Comparisons.Count == 0 ? "Equipped gear only. Select bag items to compare alternatives."
                    : $"{lastResult.Comparisons.Count} bag gear combinations, ranked by DPS gain.\r\n"
                        + "Compares selected alternatives together, retaining equipped choices in the pool.\r\n"
                        + "Uses exported gems/enchants. Check unique-equipped and embellishment limits before equipping.\r\n"
                        + "Gain ± combines baseline and swap uncertainty; small gains may be noise.")
                + (lastResult.Warnings.Count > 0 ? "\r\n\r\nEngine notes:\r\n" + string.Join("\r\n", lastResult.Warnings) : "");
            status.Text = lastResult.Warnings.Count > 0 ? "Complete with engine notes. Review the result and full report."
                : "Complete. Open the report for the full ability breakdown and assumptions.";
            report.Enabled = File.Exists(lastResult.HtmlPath); files.Enabled = true;
            comparisonStatus.Text = $"Equipped baseline: {lastResult.Dps:N0} DPS"
                + (lastResult.Error.HasValue ? $" ± {lastResult.Error:N0}" : "")
                + " • Gains are relative to equipped gear.\r\nCheck unique-equipped / embellishment limits before equipping a suggested set.";
            foreach (var comparison in lastResult.Comparisons)
            {
                var assessment = comparison.WithinUncertainty == true ? "Within uncertainty"
                    : comparison.WithinUncertainty == null ? "Error unavailable" : comparison.Gain > 0 ? "Upgrade" : "Lower DPS";
                var index = comparisons.Rows.Add(comparison.Set.Name, comparison.Set.Slots, comparison.Gain.ToString("+#,##0;-#,##0;0"),
                    comparison.Percent?.ToString("+0.00;-0.00;0.00") ?? "—", comparison.GainError?.ToString("N0") ?? "—", assessment);
                var row = comparisons.Rows[index];
                row.Cells[0].ToolTipText = string.Join("\n", comparison.Set.Changes.Select(change => $"{change.Slot}: {change.Item.Name} (#{change.Item.ItemId})\n{change.Item.Value}"))
                    + $"\n{comparison.Dps:N0} DPS • {comparison.Iterations:N0} iterations";
                row.DefaultCellStyle.ForeColor = comparison.WithinUncertainty != false ? SystemColors.GrayText
                    : comparison.Gain > 0 ? Color.DarkGreen : Color.DarkRed;
            }
            if (lastResult.Comparisons.Count > 0) resultTabs.SelectedIndex = 1;
            comparisons.ClearSelection();
        }
        catch (OperationCanceledException) { result.Text = console.ToString(); status.Text = "Simulation cancelled."; }
        catch (Exception error) { result.Text = error.Message; status.Text = "Simulation failed. See the result box for details."; }
        finally
        {
            displayTimer.Stop(); elapsed.Stop(); progress.Visible = false;
            cancellation = null;
            choose.Enabled = download.Enabled = run.Enabled = input.Enabled = mode.Enabled = precision.Enabled = duration.Enabled = targets.Enabled
                = loadBags.Enabled = bags.Enabled = !applicationUpdating;
            selectBags.Enabled = clearBags.Enabled = !applicationUpdating && bags.Items.Cast<ListViewItem>().Any(row => ((BagItem)row.Tag!).CanCompare);
            UpdateBagStatus();
            cancel.Enabled = false; RunningChanged?.Invoke();
        }
    }

    public async Task StopAsync()
    {
        cancellation?.Cancel();
        if (runningTask != null) await runningTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { cancellation?.Cancel(); displayTimer.Dispose(); }
        base.Dispose(disposing);
    }
}
