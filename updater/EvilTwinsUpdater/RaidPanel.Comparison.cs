using EvilTwinsUpdater.Core;

namespace EvilTwinsUpdater;

internal sealed partial class RaidPanel
{
    private sealed record BossChoice(int InstanceId, int BossId, string Name)
    {
        public override string ToString() => Name;
    }
    private void RefreshComparisonChoices()
    {
        if (refreshingComparison) return;
        var selectedBoss = bossFilter.SelectedItem as BossChoice;
        var items = results.SelectMany(value => value.Report.Items);
        if (raid != null && catalog != null && source.SelectedItem is RaidLootSource selected) {
            foreach (var member in raid.Members.Where(value => value.Ready)) {
                if (comparisonPlans.ContainsKey(member.Key) || comparisonErrors.ContainsKey(member.Key)) continue;
                try { comparisonPlans[member.Key] = catalog.Plan(member, selected.Id, difficulty.Text); }
                catch (Exception error) when (error is InvalidOperationException or InvalidDataException) { comparisonErrors[member.Key] = error.Message; }
            }
            // Keep imported results discoverable even when local controls select another source/difficulty.
            items = items.Concat(comparisonPlans.Values.SelectMany(value => value.Cases.Select(candidate => candidate.Item)));
        }
        comparisonChoices = RaidLootComparison.Choices(items);
        refreshingComparison = true;
        try {
            bossFilter.Items.Clear(); bossFilter.Items.Add("All bosses");
            foreach (var boss in comparisonChoices.GroupBy(value => (value.Key.InstanceId, value.Key.BossId))
                .Select(group => new BossChoice(group.Key.InstanceId, group.Key.BossId, group.First().Boss + " · " + group.First().Instance)).OrderBy(value => value.Name)) bossFilter.Items.Add(boss);
            bossFilter.SelectedItem = selectedBoss;
            if (bossFilter.SelectedIndex < 0) bossFilter.SelectedIndex = 0;
        }
        finally { refreshingComparison = false; }
        PopulateDrops();
    }
    private void PopulateDrops()
    {
        var previous = (dropFilter.SelectedItem as RaidDropChoice)?.Key;
        var boss = bossFilter.SelectedItem as BossChoice;
        refreshingComparison = true;
        try {
            dropFilter.Items.Clear();
            foreach (var drop in comparisonChoices.Where(value => boss == null || value.Key.InstanceId == boss.InstanceId && value.Key.BossId == boss.BossId)) dropFilter.Items.Add(drop);
            dropFilter.SelectedItem = dropFilter.Items.Cast<RaidDropChoice>().FirstOrDefault(value => value.Key == previous);
            if (dropFilter.SelectedIndex < 0 && dropFilter.Items.Count > 0) dropFilter.SelectedIndex = 0;
            bossFilter.Enabled = comparisonChoices.Count > 0; dropFilter.Enabled = dropFilter.Items.Count > 0;
        }
        finally { refreshingComparison = false; }
        RefreshStandings();
    }
    private void RefreshStandings()
    {
        var selectedPlayer = (standings.CurrentRow?.Tag as RaidDropStanding)?.Member.Key;
        standings.Rows.Clear();
        if (raid == null || dropFilter.SelectedItem is not RaidDropChoice drop) {
            comparisonHint.Text = raid == null ? "Import a raid export to compare players for a drop."
                : !raid.Members.Any(value => value.Ready) ? "No ready DPS profiles. Refresh the raid export in WoW, then import it here."
                : "Load loot data or import completed results to choose a boss and drop.";
            RefreshActions(); return;
        }
        var rows = RaidLootComparison.Compare(raid.Members, results, drop, catalog == null ? null : comparisonPlans, states);
        var position = 0; var rank = 0; double? previousGain = null;
        foreach (var standing in rows) {
            var item = standing.Upgrade;
            if (item != null) { position++; if (previousGain != item.Gain) rank = position; previousGain = item.Gain; }
            var row = standings.Rows[standings.Rows.Add(item != null ? rank.ToString() : "—", standing.Member.Key,
                item != null ? $"+{item.Gain:N0}" : "—", item != null ? $"+{item.Percent:F2}%" : "—",
                standing.Status.StartsWith("Failed:", StringComparison.Ordinal) ? "Simulation failed · open Details" : standing.Status,
                item?.Name ?? "—")];
            row.Tag = standing;
            row.Cells[4].ToolTipText = standing.Status;
            row.DefaultCellStyle.ForeColor = item == null || item.WithinError ? AppTheme.Muted : AppTheme.Text;
            if (item != null) row.Cells[2].Style.ForeColor = item.WithinError ? AppTheme.Accent : AppTheme.Success;
            if (standing.Member.Key == selectedPlayer) standings.CurrentCell = row.Cells[1];
        }
        comparisonHint.MaximumSize = new Size(Math.Max(300, standings.Width - 10), 0);
        comparisonHint.Text = $"{drop.Name} · {drop.Boss} · {rows.Count(value => value.Upgrade != null)} reported gains / {rows.Count} listed DPS. "
            + "Ranks are estimates; uncertain gains may overlap. Unlisted gains are not zero."
            + (comparisonErrors.Count > 0 ? $" Eligibility unavailable for {comparisonErrors.Count} player(s)." : "");
        layoutHints.SetToolTip(comparisonHint, string.Join(Environment.NewLine, comparisonErrors.Select(value => value.Key + ": " + value.Value)));
        RefreshActions();
    }
}
