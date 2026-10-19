using EvilTwinsUpdater.Core;

internal static class RaidWorkflowTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task Run()
    {
        var queue = new RaidSimulationQueue(["One", "Two", "Three", "Two"]);
        Assert(await queue.NextAsync() == "One", "Start the first player once.");
        queue.PauseAfterCurrent();
        Assert(!queue.IsPaused && queue.Current == "One", "Pausing must let the active player finish.");
        Assert(!queue.MoveNext("One") && queue.MoveNext("Three"), "Only waiting players can move next.");
        Assert(queue.Unfinished.SequenceEqual(["One", "Three", "Two"]), "Save the active player and reordered waiting players.");
        queue.FinishCurrent();
        var waiting = queue.NextAsync();
        Assert(queue.IsPaused && !waiting.IsCompleted, "Pause at the player boundary without starting another engine.");
        queue.Resume();
        Assert(await waiting.WaitAsync(TimeSpan.FromSeconds(2)) == "Three", "Resume in the chosen order.");
        queue.FinishCurrent(); queue.PauseAfterCurrent();
        using (var stop = new CancellationTokenSource()) {
            var stopped = queue.NextAsync(stop.Token); stop.Cancel();
            try { await stopped.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Paused queue ignored cancellation."); }
            catch (OperationCanceledException) { }
        }
        Assert(queue.Pending.SequenceEqual(["Two"]), "Cancelling a wait must retain unfinished players.");
        queue.Resume(); Assert(await queue.NextAsync() == "Two", "Keep every waiting player after pause cancellation.");
        queue.PauseAfterCurrent(); queue.FinishCurrent();
        Assert(await queue.NextAsync() == null && !queue.IsPaused, "Finish without hanging when a pause is requested on the last player.");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        RaidMember Member(string name) => new(name + "-Realm", "Player-" + name, "DAMAGER", 252, now,
            $"deathknight=\"{name}\"\nspec=unholy\nlevel=90\ntalents=FixtureTalent\nmain_hand=,id=268198\nhead=,id=271474,bonus_id=12845\n", "Ready");
        var one = Member("One"); var two = Member("Two"); var three = Member("Three");
        var missing = new RaidMember("Missing-Realm", "Player-Missing", "DAMAGER", 0, 0, "", "Missing profile");
        var healer = missing with { Key = "Healer-Realm", Guid = "Player-Healer", Role = "HEALER" };
        var plan = RaidTests.Catalog().Plan(one, 1320, "Heroic"); var item = plan.Cases.First().Item;
        RaidDropResult Result(RaidMember member, SimUpgrade[] items) => new(member,
            new DropReport(member.Key.Split('-')[0], "Realm", "us", member.SpecId, "Fixture" + member.Key, now,
                "Fixture", "Patchwerk", 300, 1, 10000, plan.Scope, "FixtureTalent", "Fixture assumptions", new Dictionary<string, string>(), items), "", []);
        var low = Result(one, [item with { Gain = 100, Percent = 1, WithinError = false }]);
        var high = Result(two, [item with { Gain = 500, Percent = 5, WithinError = true }]);
        var choices = RaidLootComparison.Choices([item, item with { SimulatedId = item.SimulatedId + 1, Name = item.Name + " → Alternative" },
            item with { ItemLevel = item.ItemLevel + 3 }, item with { Difficulty = "raid-mythic" }]);
        Assert(choices.Count == 3, "Group conversion variants while keeping difficulty and item level separate.");
        var drop = choices.Single(value => value.Key == RaidDropKey.From(item));
        var plans = new Dictionary<string, DropPlan> { [one.Key] = plan, [two.Key] = plan with { Member = two }, [three.Key] = plan with { Member = three } };
        var rows = RaidLootComparison.Compare([one, two, three, missing, healer], [low, high], drop, plans, new Dictionary<string, string> { [three.Key] = "Failed: fixture" });
        Assert(rows.Count == 4 && rows[0].Member == two && rows[1].Member == one && rows[0].Status == "Uncertain gain", "Rank gains and retain uncertainty, failures and missing DPS; exclude tanks/healers.");
        Assert(rows.Single(value => value.Member == missing).Upgrade == null && rows.Single(value => value.Member == three).Status.StartsWith("Failed:"), "Missing data must remain unranked and explain why.");
        var noGain = Result(three, []);
        rows = RaidLootComparison.Compare([three], [noGain], drop, plans, new Dictionary<string, string>());
        Assert(rows.Single().Upgrade == null && rows.Single().Status == "No positive gain reported", "Do not turn omitted/limited report entries into zero DPS.");
        plans[three.Key] = plan with { Member = three, Cases = [] };
        Assert(RaidLootComparison.Compare([three], [], drop, plans, new Dictionary<string, string>()).Count == 0, "Exclude players whose known candidate plan cannot use the selected drop.");
        var differentFight = high with { Report = high.Report with { Duration = 180 } };
        rows = RaidLootComparison.Compare([one, two], [low, differentFight], drop, plans, new Dictionary<string, string>());
        Assert(rows.Single(value => value.Member == two).Upgrade == null && rows.Single(value => value.Member == two).Status == "Different fight settings", "Do not rank incompatible fight settings.");
        var foreign = high with { Report = high.Report with { Scope = "other/raid-heroic" } };
        Assert(RaidLootComparison.Compare([two], [foreign], drop, plans, new Dictionary<string, string>()).Single().Upgrade == null, "Do not rank results for a different source.");
        var multiple = high with { Report = high.Report with { Scope = plan.Scope + ",9999/raid-mythic" } };
        Assert(RaidLootComparison.Compare([two], [multiple], drop, plans, new Dictionary<string, string>()).Single().Upgrade?.Gain == 500, "Rank the matching drop from an imported report covering multiple sources.");
        var unknown = RaidLootComparison.Compare([two], [], drop, null, new Dictionary<string, string> { [two.Key] = "Complete" }).Single();
        Assert(unknown.Upgrade == null && unknown.Status.Contains("Not simulated for this source") && unknown.Status.Contains("eligibility unknown"), "Another source's completed result must not claim this drop was simulated or eligibility is known.");
        Console.WriteLine("PASS: player-boundary pause/resume, pending priority, cancellation, saved order, boss/drop grouping, ranked gains, uncertainty, missing/failed profiles, eligibility and incompatible results.");
    }
}
