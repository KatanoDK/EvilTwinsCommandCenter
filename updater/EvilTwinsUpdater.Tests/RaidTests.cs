using System.Text;
using System.Text.Json;
using EvilTwinsUpdater.Core;

internal static class RaidTests
{
    internal static string Code(params RaidMember[] members) => Pack(members);
    private static string Pack(IEnumerable<RaidMember> members)
    {
        var rows = members.ToArray();
        var fields = new List<string> { "1", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "us", rows.Length.ToString() };
        foreach (var member in rows) fields.AddRange([member.Key, member.Guid, member.Role, member.SpecId.ToString(), member.Updated.ToString(), member.Profile, member.State]);
        return "ETR1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Concat(fields.Select(value => Encoding.UTF8.GetByteCount(value) + ":" + value))));
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { return; }
        throw new Exception("Expected raid rejection: " + message);
    }
    internal static RaidLootCatalog Catalog()
    {
        using var json = JsonDocument.Parse(File.ReadAllText("tests/fixtures/raid-loot.json"));
        return new RaidLootCatalog(json.RootElement.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.Clone()));
    }
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var member = new RaidMember("Fixture-Realm", "Player-1", "DAMAGER", 252, now,
            "deathknight=\"Fixture\"\nspec=unholy\nlevel=90\ntalents=FixtureTalent\nmain_hand=,id=268198\nhead=,id=271474,bonus_id=12845\n", "Ready");
        var missing = new RaidMember("Missing-Realm", "Player-2", "DAMAGER", 0, 0, "", "Missing profile");
        var tank = new RaidMember("Tank-Realm", "Player-3", "TANK", 250, now, "", "Skipped: tank/healer");
        var raid = RaidExport.Parse(Code(member, missing, tank));
        var luaRaid = RaidExport.Parse(File.ReadAllText("tests/fixtures/raid-export.txt"));
        Assert(luaRaid.Members.Count == 4 && luaRaid.Members.Count(value => value.Ready) == 2, "The desktop app must parse a real Lua-generated clipboard export.");
        Assert(raid.Members.Count == 3 && raid.Members.Count(value => value.Ready) == 1, "Only ready DPS exports can run.");
        var utf8 = member with { Key = "Étoile-Quel’Thalas", Profile = member.Profile.Replace("Fixture\"", "Étoile\"") };
        Assert(RaidExport.Parse(Code(utf8)).Members[0].Key == utf8.Key, "Raid codec uses UTF-8 byte lengths.");
        var forty = Enumerable.Range(1, 40).Select(i => member with { Key = "Player" + i + "-Realm", Guid = "Player-" + i, Profile = member.Profile.Replace("Fixture\"", "Player" + i + "\"") }).ToArray();
        Assert(RaidExport.Parse(Code(forty)).Members.Count == 40, "A full raid must fit.");
        Reject(() => RaidExport.Parse(Code(forty.Concat([missing]).ToArray())), "41 members");
        Reject(() => RaidExport.Parse(Code(member, member)), "Duplicate members");
        Reject(() => RaidExport.Parse(Code(member with { SpecId = 250 })), "Tank profile marked ready");
        Reject(() => RaidExport.Parse(Code(member with { Key = "Other-Realm" })), "Identity mismatch");
        Reject(() => RaidExport.Parse(Code(member with { Updated = now + 1000 })), "Future timestamp");
        Reject(() => RaidExport.Parse(Code(member with { Profile = member.Profile + "include=private.txt\n" })), "File include injection");
        Reject(() => RaidExport.Parse("ETR1:bad"), "Bad base64");
        var catalog = Catalog(); var plan = catalog.Plan(member, 1320, "Heroic");
        Assert(plan.Cases.Count is > 20 and <= 500, "Current raid must produce a bounded candidate list.");
        var neck = plan.Cases.Single(value => value.Item.SimulatedId == 268265);
        Assert(neck.Item.ItemLevel == 315 && neck.Item.BonusIds.Contains("12844"), "Last-boss Heroic loot must use its exact drop level and track bonus.");
        Assert(plan.Cases.Any(value => value.Item.BossId == -97 && value.Item.ItemLevel == 305), "Trash drops must be included at entry loot level.");
        Assert(plan.Cases.Any(value => value.Item.DropId == 270913 && value.Item.SimulatedId == 271475 && value.Item.ItemLevel == 308), "Tier tokens must resolve to this player's reward and boss level.");
        Assert(plan.Cases.Any(value => value.Item.DropId == 270909 && value.Item.SimulatedId == 271474 && value.Item.ItemLevel == 311), "Omni tokens must apply per-slot sequence offsets.");
        Assert(!plan.Cases.Any(value => value.Item.SimulatedId == 268200), "Unholy must not simulate hunter weapons.");
        var normal = catalog.Plan(member, 1320, "Normal");
        Assert(normal.Cases.Single(value => value.Item.SimulatedId == 268265).Item.ItemLevel == 302, "Normal must use the Champion track.");
        var priest = member with { Key = "Priest-Realm", SpecId = 258,
            Profile = "priest=\"Priest\"\nspec=shadow\nlevel=90\ntalents=FixtureTalent\nmain_hand=,id=268205\n" };
        var priestPlan = catalog.Plan(priest, 1320, "Heroic");
        Assert(!priestPlan.Cases.Any(value => value.Item.SimulatedId == 271474 || value.Item.SimulatedId == 268198), "Class, armor and primary-stat restrictions must exclude unusable loot.");
        var twoHand = catalog.Plan(member with { Profile = member.Profile + "off_hand=,id=268201\n" }, 1320, "Heroic");
        Assert(twoHand.Cases.Any(value => value.Item.SimulatedId == 268198 && value.Set.Changes.Count == 2 && value.Set.Changes[1].Item.Value == "none"), "Two-handed swaps must empty the off hand.");
        Console.WriteLine($"PASS: raid codec, full raid, DPS eligibility, missing members, UTF-8, identity, injection limits, current loot/levels, trash, tier tokens, and weapon restrictions ({plan.Cases.Count} Unholy cases).");
    }
    internal static async Task VerifyReal(string engine, string profilePath, string output)
    {
        var profile = File.ReadAllText(profilePath).Replace("MID2_Death_Knight_Unholy_Rider", "Fixture");
        var member = new RaidMember("Fixture-Realm", "Player-1", "DAMAGER", 252, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), profile, "Ready");
        var raid = RaidExport.Parse(Code(member)); var plan = Catalog().Plan(member, 1320, "Heroic");
        var tracker = new SimulationProgress(plan.Cases.Count + 1);
        var result = await RaidDroptimizer.RunAsync(engine, raid, plan, new SimulationOptions(1000), output, new InlineProgress(tracker.Append));
        Assert(result.Report.Baseline > 0 && tracker.Fraction == 1, "Real local Droptimizer must complete all candidates.");
        Assert(RaidDroptimizer.Matches(member, result.Report), "Local report must match the captured build.");
        File.WriteAllText(Path.Combine(output, "local-report-code.txt"), result.Report.ImportCode());
        Console.WriteLine($"PASS: real local Droptimizer, {plan.Cases.Count} candidates, {result.Report.Items.Count} positive drops, baseline {result.Report.Baseline:F0} DPS, complete progress, compatible addon report.");
        Console.WriteLine(result.Directory);
        foreach (var warning in result.Warnings) Console.WriteLine(warning);
    }
    private sealed class InlineProgress(Action<string> callback) : IProgress<string> { public void Report(string value) => callback(value); }
}
