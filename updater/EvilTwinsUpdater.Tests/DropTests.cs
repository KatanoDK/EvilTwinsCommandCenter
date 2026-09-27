using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EvilTwinsUpdater.Core;

internal static class DropTests
{
    public static async Task Run(string testRoot)
    {
        var fixture = File.ReadAllText("tests/fixtures/droptimizer.json");
        using var document = JsonDocument.Parse(fixture);
        var report = DroptimizerClient.Parse(document.RootElement, "FixtureReport123");
        Assert(report.Items.Count == 3, "Expected three positive upgrades.");
        Assert(report.Items[0].DropId == 268265 && Math.Abs(report.Items[0].Percent - 0.684969193) < 0.000001, "DPS gain calculation is wrong.");
        var tier = report.Items[2];
        Assert(tier.DropId == 268246 && tier.SimulatedId == 271535 && tier.ItemLevel == 298, "Tier conversion must preserve the original drop and level.");
        Assert(report.Gear["head"] == "12345;8001;2/6;3/9;", "Gear fingerprints must normalize gem/bonus ordering.");
        Assert(report.Gear.ContainsKey("main_hand"), "Profile camelCase weapon slots must normalize to SimC slot names.");
        Assert(report.ImportCode() == File.ReadAllText("tests/fixtures/droptimizer-code.txt"), "Lua codec fixture differs from the actual C# export.");
        using var trashDocument = JsonDocument.Parse(File.ReadAllText("tests/fixtures/droptimizer-trash.json"));
        var trashReport = DroptimizerClient.Parse(trashDocument.RootElement, "FixtureTrashReport123");
        Assert(trashReport.Items.Count == 2 && trashReport.Items[1].BossId == -97
            && trashReport.Items[1].Boss == "Trash Drop" && trashReport.Items[1].DropId == 271436,
            "Raid trash loot must retain its signed encounter ID alongside boss loot.");
        Assert(trashReport.ImportCode() == File.ReadAllText("tests/fixtures/droptimizer-trash-code.txt"),
            "Trash loot fixture must match the actual C# import code.");
        var unicode = report with { Title = "Fixture – Épreuve", Name = "Éowyn" };
        var code = unicode.ImportCode();
        var bytes = Convert.FromBase64String(code[5..]);
        var fields = new List<string>();
        var position = 0;
        while (position < bytes.Length)
        {
            var colon = Array.IndexOf(bytes, (byte)':', position);
            var size = int.Parse(Encoding.ASCII.GetString(bytes, position, colon - position));
            fields.Add(Encoding.UTF8.GetString(bytes, colon + 1, size));
            position = colon + 1 + size;
        }
        Assert(fields[1] == "Éowyn" && fields[7] == "Fixture – Épreuve", "Import lengths must count UTF-8 bytes.");
        File.WriteAllText(Path.Combine(testRoot, "droptimizer-code.txt"), report.ImportCode());
        // Shared cross-language fixture is generated from the actual parser, not hand-written Lua data.
        var shared = Path.Combine(Path.GetDirectoryName(testRoot)!, "droptimizer-code.txt");
        File.WriteAllText(shared, report.ImportCode());
        foreach (var invalid in new[] { "http://www.raidbots.com/simbot/report/FixtureReport123", "https://evil.example/simbot/report/FixtureReport123",
            "https://www.raidbots.com.evil.example/simbot/report/FixtureReport123", "https://user@www.raidbots.com/simbot/report/FixtureReport123",
            "https://www.raidbots.com:8443/simbot/report/FixtureReport123", "https://www.raidbots.com/other/FixtureReport123" })
            Reject(() => DroptimizerClient.ReportId(invalid));
        var fake = JsonNode.Parse(fixture)!;
        var alternate = fake["sim"]!["profilesets"]!["results"]![1]!.DeepClone();
        alternate["mean"] = report.Baseline + 10;
        alternate["name"] = alternate["name"]!.GetValue<string>().Replace("/7963/", "/0/");
        fake["sim"]!["profilesets"]!["results"]!.AsArray().Add(alternate);
        var conflictingItem = fake["simbot"]!["meta"]!["itemLibrary"]!.AsArray()
            .First(item => item!["id"]!.GetValue<int>() == 268265)!.DeepClone();
        conflictingItem["name"] = "Wrong source and level";
        conflictingItem["itemLevel"] = 999;
        conflictingItem["encounterId"] = 1;
        fake["simbot"]!["meta"]!["itemLibrary"]!.AsArray().Insert(0, conflictingItem);
        var variants = Parse(fake);
        Assert(variants.Items.Count == 3 && variants.Items[0].Name == report.Items[0].Name
            && variants.Items[1].Gain == report.Items[1].Gain, "Duplicate variants and conflicting source metadata changed the ranked upgrades.");
        fake = JsonNode.Parse(fixture)!;
        fake["simbot"]!["simType"] = "topgear";
        Reject(() => Parse(fake));
        fake = JsonNode.Parse(fixture)!;
        fake["simbot"]!["parentSimId"] = "DifferentReport";
        Reject(() => Parse(fake));
        fake = JsonNode.Parse(fixture)!;
        fake["sim"]!["players"]![0]!["collected_data"]!["dps"]!["mean"] = 0;
        Reject(() => Parse(fake));
        fake = JsonNode.Parse(fixture)!;
        fake["sim"]!["profilesets"]!["results"]![0]!["name"] = "unknown-new-format";
        Reject(() => Parse(fake));
        fake = JsonNode.Parse(fixture)!;
        fake["sim"]!["profilesets"]!["results"]![0]!["mean"] = 0;
        Reject(() => Parse(fake));
        fake = JsonNode.Parse(fixture)!;
        // No positive upgrades is a valid report, and should clear a previous report in that scope.
        foreach (var result in fake["sim"]!["profilesets"]!["results"]!.AsArray()) result!["mean"] = 1;
        Assert(Parse(fake).Items.Count == 0, "An empty upgrade report must be importable.");
        using var client = new DroptimizerClient(new FixtureHandler(request => {
            Assert(request.RequestUri!.AbsoluteUri == "https://www.raidbots.com/reports/FixtureReport123/data.json", "Unexpected fetch URL.");
            return new(HttpStatusCode.OK) { Content = new StringContent(fixture) };
        }));
        Assert((await client.FetchAsync("https://www.raidbots.com/simbot/report/FixtureReport123")).Items.Count == 3, "Fetch did not parse the report.");
        Console.WriteLine("PASS: Droptimizer source/level matching, tier conversion, DPS gains, URL validation, unfinished reports, UTF-8 codec, and anonymous downloads.");
    }

    private static DropReport Parse(JsonNode node)
    {
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return DroptimizerClient.Parse(doc.RootElement, "FixtureReport123");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Expected invalid report rejection.");
    }
}
