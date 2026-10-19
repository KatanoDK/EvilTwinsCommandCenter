namespace EvilTwinsUpdater.Core;

public sealed record RaidDropKey(int DropId, int InstanceId, int BossId, string Difficulty, int ItemLevel)
{
    public static RaidDropKey From(SimUpgrade item) => new(item.DropId, item.InstanceId, item.BossId, item.Difficulty, item.ItemLevel);
    public string Scope => InstanceId + "/" + Difficulty;
}
public sealed record RaidDropChoice(RaidDropKey Key, string Name, string Boss, string Instance)
{
    public override string ToString() => $"{Name} · {Key.ItemLevel} · {Key.Difficulty.Replace("raid-", "")}";
}
public sealed record RaidDropStanding(RaidMember Member, RaidDropResult? Result, SimUpgrade? Upgrade, string Status);
public static class RaidLootComparison
{
    public static IReadOnlyList<RaidDropChoice> Choices(IEnumerable<SimUpgrade> items) => items
        .GroupBy(RaidDropKey.From).Select(group => {
            var item = group.First();
            return new RaidDropChoice(group.Key, item.Name.Split(" → ", 2)[0], item.Boss, item.Instance);
        }).OrderBy(value => value.Instance).ThenBy(value => value.Boss).ThenBy(value => value.Name).ThenBy(value => value.Key.ItemLevel).ToArray();

    public static IReadOnlyList<RaidDropStanding> Compare(IEnumerable<RaidMember> members, IEnumerable<RaidDropResult> results,
        RaidDropChoice drop, IReadOnlyDictionary<string, DropPlan>? plans, IReadOnlyDictionary<string, string> states)
    {
        var reports = results.Where(value => value.Report.Scope.Split(',').Contains(drop.Key.Scope, StringComparer.Ordinal)).ToArray();
        var reference = reports.FirstOrDefault()?.Report;
        var rows = new List<RaidDropStanding>();
        foreach (var member in members) {
            if (!member.Ready) {
                if (member.Role.Equals("DAMAGER", StringComparison.OrdinalIgnoreCase)) rows.Add(new(member, null, null, "Missing profile · eligibility unknown"));
                continue;
            }
            var result = reports.FirstOrDefault(value => value.Member.Key == member.Key);
            var upgrade = result?.Report.Items.Where(value => RaidDropKey.From(value) == drop.Key && value.Gain > 0).OrderByDescending(value => value.Gain).FirstOrDefault();
            if (result != null && reference != null && (result.Report.FightStyle != reference.FightStyle
                || result.Report.Duration != reference.Duration || result.Report.Enemies != reference.Enemies)) {
                rows.Add(new(member, result, null, "Different fight settings")); continue;
            }
            var plan = plans?.GetValueOrDefault(member.Key);
            if (plan?.Scope != drop.Key.Scope) plan = null;
            if (upgrade == null && plan != null && !plan.Cases.Any(value => RaidDropKey.From(value.Item) == drop.Key)) continue;
            var state = states.GetValueOrDefault(member.Key, "Not simulated");
            if (result == null && state.StartsWith("Complete", StringComparison.Ordinal)) state = "Not simulated for this source / difficulty";
            var status = upgrade != null ? upgrade.WithinError ? "Uncertain gain" : "Positive gain"
                : result != null ? "No positive gain reported" : state;
            if (upgrade == null && plan == null) status += " · eligibility unknown";
            rows.Add(new(member, result, upgrade, status));
        }
        // Unknown/missing data never becomes zero DPS or an invented rank.
        return rows.OrderByDescending(value => value.Upgrade?.Gain ?? double.NegativeInfinity)
            .ThenBy(value => value.Member.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
