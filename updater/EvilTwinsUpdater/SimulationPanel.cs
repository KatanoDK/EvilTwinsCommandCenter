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
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 112 };
    private readonly ComboBox precision = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly ComboBox duration = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 65 };
    private readonly ComboBox targets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 50 };
    private readonly Button run = new() { Text = "Run simulation", AutoSize = true };
    private readonly Button cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly Button report = new() { Text = "Open report", AutoSize = true, Enabled = false };
    private readonly Button files = new() { Text = "Open run folder", AutoSize = true, Enabled = false };
    private readonly Label status = new() { UseMnemonic = false, AutoSize = true, MaximumSize = new Size(620, 0),
        Text = "Equipped gear only • Patchwerk • raid buffs • default class rotation" };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 6, Visible = false };
    private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 250 };
    private readonly StringBuilder console = new();
    private readonly Stopwatch elapsed = new();
    private CancellationTokenSource? cancellation;
    private Task? runningTask;
    private SimulationResult? lastResult;
    private int threadCount;
    private bool applicationUpdating;
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
        var inputGroup = new GroupBox { Text = "Character export", Dock = DockStyle.Fill, Padding = new Padding(8, 5, 8, 8) };
        inputGroup.Controls.Add(input);
        var resultGroup = new GroupBox { Text = "Result / engine output", Dock = DockStyle.Fill, Padding = new Padding(8, 5, 8, 8) };
        resultGroup.Controls.Add(result);
        panes.Controls.Add(inputGroup, 0, 0); panes.Controls.Add(resultGroup, 0, 1);
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
        displayTimer.Tick += (_, _) => {
            if (cancellation?.IsCancellationRequested != true)
                status.Text = $"Running • {threadCount} CPU thread(s) • {elapsed.Elapsed.TotalSeconds:F1}s elapsed";
            result.Text = console.ToString(); result.SelectionStart = result.TextLength; result.ScrollToCaret();
        };
    }

    public void SetEnginePath(string path) => engine.Text = path;

    public void SetApplicationUpdating(bool value)
    {
        applicationUpdating = value;
        run.Enabled = !value && !IsRunning;
        choose.Enabled = !value && !IsRunning;
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

    private async Task RunAsync()
    {
        var options = new SimulationOptions(new[] { 1000, 5000, 20_000 }[precision.SelectedIndex],
            new[] { 180, 300, 600 }[duration.SelectedIndex], new[] { 1, 3, 5 }[targets.SelectedIndex],
            mode.SelectedIndex == 0 ? SimulationMode.Background : SimulationMode.FullSpeed);
        using var tokenSource = new CancellationTokenSource();
        cancellation = tokenSource;
        choose.Enabled = download.Enabled = run.Enabled = input.Enabled = mode.Enabled = precision.Enabled = duration.Enabled = targets.Enabled = false;
        cancel.Enabled = true; report.Enabled = files.Enabled = false;
        lastResult = null; console.Clear(); result.Clear(); threadCount = options.Threads;
        progress.Style = ProgressBarStyle.Marquee; progress.Visible = true; elapsed.Restart(); displayTimer.Start();
        RunningChanged?.Invoke();
        try
        {
            var output = new Progress<string>(text => {
                if (cancellation != tokenSource) return;
                console.Append(text);
                if (console.Length > 16_000) console.Remove(0, console.Length - 16_000);
            });
            lastResult = await simulator.RunAsync(engine.Text, input.Text, options, outputRoot, output, tokenSource.Token);
            displayTimer.Stop();
            result.Text = $"{lastResult.Name} — {lastResult.Specialization}\r\n"
                + $"{lastResult.Dps:N0} DPS"
                + (lastResult.Error.HasValue ? $" ± {lastResult.Error.Value:N0} ({lastResult.Confidence:P0} confidence)" : " • Error unavailable")
                + $"\r\n{lastResult.Iterations:N0} iterations • {lastResult.Threads} CPU thread(s) • {lastResult.Elapsed.TotalSeconds:F1}s\r\n"
                + $"SimulationCraft {lastResult.EngineVersion} • {lastResult.EngineRevision}\r\n"
                + $"Patchwerk • {options.Duration}s average • {options.Targets} target(s) • raid buffs • default rotation\r\n"
                + "Equipped gear only; commented bag alternatives are not compared."
                + (lastResult.Warnings.Count > 0 ? "\r\n\r\nEngine notes:\r\n" + string.Join("\r\n", lastResult.Warnings) : "");
            status.Text = lastResult.Warnings.Count > 0 ? "Complete with engine notes. Review the result and full report."
                : "Complete. Open the report for the full ability breakdown and assumptions.";
            report.Enabled = File.Exists(lastResult.HtmlPath); files.Enabled = true;
        }
        catch (OperationCanceledException) { result.Text = console.ToString(); status.Text = "Simulation cancelled."; }
        catch (Exception error) { result.Text = error.Message; status.Text = "Simulation failed. See the result box for details."; }
        finally
        {
            displayTimer.Stop(); elapsed.Stop(); progress.Visible = false;
            cancellation = null;
            choose.Enabled = download.Enabled = run.Enabled = input.Enabled = mode.Enabled = precision.Enabled = duration.Enabled = targets.Enabled = true;
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
