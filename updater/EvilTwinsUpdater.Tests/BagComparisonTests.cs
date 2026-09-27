using EvilTwinsUpdater.Core;

internal static class BagComparisonTests
{
    private const string Export = "deathknight=\"Fixture\"\nspec=unholy\nhead=,id=1\nchest=,id=2\nfinger1=,id=3\nfinger2=,id=4\n"
        + "### Gear from Bags\n#\n# New helm (300)\n# Hero 1/6\n# head=,id=11,bonus_id=40/50,gem_id=99,enchant_id=88\n"
        + "#\n# New chest\n# chest=,id=12\n#\n# New ring\n# finger1=,id=13\n"
        + "#\n# Duplicate ring\n# finger2=,id=13\n#\n# Weapon\n# main_hand=,id=14\n"
        + "### Weekly Reward Choices\n# head=,id=999\n### Gear from Bank\n# chest=,id=998\n";

    public static async Task Run(LocalSimulator runner, string engine, string runs)
    {
        var items = BagComparisons.ReadItems(Export);
        Assert(items.Count == 4 && items.All(item => item.ItemId is not 999 and not 998), "Only bags are compared; duplicate paired variants collapse.");
        Assert(items[0].Name == "New helm (300)" && items[0].Value.Contains("gem_id=99,enchant_id=88"), "Item name and full variant must survive.");
        Assert(!BagComparisons.ReadItems(Export.Replace("head=,id=1\n", "head=named_item,enchant_id=88,gem_id=99,bonus_id=40/50,id=11\n"))[0].CanCompare,
            "Display-name tokens and field ordering must not make identical equipped gear an alternative.");
        Assert(!items[3].CanCompare && items[3].UnavailableReason.Contains("Weapon"), "Unidentified weapon pairs cannot be guessed.");
        var sets = BagComparisons.CreateSets(Export, [0, 1]);
        Assert(sets.Count == 3 && sets.Count(set => set.Changes.Count == 2) == 1, "Two slots must generate both single swaps and a combined swap.");
        var rings = BagComparisons.CreateSets(Export, [2]);
        Assert(rings.Count == 2 && rings.All(set => set.Changes.Count == 1), "One ring must be tested in each slot, never duplicated across both.");
        var paired = Export.Replace("### Weekly Reward Choices", "#\n# Another ring\n# finger1=,id=15\n### Weekly Reward Choices");
        var pairs = BagComparisons.CreateSets(paired, [2, 4]);
        Assert(pairs.Count == 6 && pairs.Count(set => set.Changes.Count == 2) == 2, "Two rings must include both valid paired arrangements.");
        var replacingOther = Export.Replace("finger1=,id=13", "finger1=,id=4");
        Assert(BagComparisons.CreateSets(replacingOther, [2]).Count == 0, "Paired equipped item IDs cannot be duplicated.");
        Reject(() => BagComparisons.CreateSets(Export, [99]), "Forged selection");
        Reject(() => BagComparisons.CreateSets(Export, [0, 0]), "Duplicate selection");
        Reject(() => BagComparisons.CreateSets(Export, Enumerable.Range(0, 21).ToArray()), "More than 20 selected variants");
        Reject(() => BagComparisons.CreateSets(Export, [3]), "Unavailable selection");
        Reject(() => BagComparisons.ReadItems(Export.Replace("id=11,", "id=11 input=outside.simc,")), "Commented directive injection");
        Reject(() => BagComparisons.ReadItems(Export.Replace("id=11,", "id=11,id=100,")), "Ambiguous item ID");
        var large = "warrior=Fixture\nspec=arms\n### Gear from Bags\n"
            + string.Join('\n', new[] { "head", "neck", "shoulders", "back", "chest", "wrists", "hands", "waist", "legs" }
                .Select((slot, index) => $"#\n# {slot}=,id={100 + index}"));
        Reject(() => BagComparisons.CreateSets(large, Enumerable.Range(0, 9).ToArray()), "More than 500 combinations must fail before spawning a process");
        Assert(BagComparisons.CreateSets(large, Enumerable.Range(0, 8).ToArray()).Count == 255, "Combination limit must preserve smaller pools completely.");
        var result = await runner.RunAsync(engine, Export, new SimulationOptions(1000), runs, selectedBagItems: [0, 1]);
        Assert(result.Comparisons.Count == 3 && result.Comparisons[0].Set.Changes.Count == 2
            && result.Comparisons[0].Gain == 300, "Native report must map, rank and retain combined gear changes.");
        Assert(Math.Abs(result.Comparisons[0].GainError!.Value - Math.Sqrt(19.6 * 19.6 + 400)) < 0.0001,
            "Difference uncertainty must include both baseline and gear set error.");
        var input = File.ReadAllText(Path.Combine(result.Directory, "input.simc")).Replace("\r", "");
        Assert(input.Contains("head=,id=1\n") && input.Contains("chest=,id=2\n") && input.Contains("profileset.gear_2+=head=")
            && !input.Contains("id=999"), "Equipped baseline must stay intact and multi-item changes append to one generated set.");
        foreach (var name in new[] { "FixtureMissing", "FixtureMismatch" })
        {
            try { await runner.RunAsync(engine, Export.Replace("Fixture", name), new SimulationOptions(1000), runs, selectedBagItems: [0, 1]); }
            catch (InvalidDataException) { continue; }
            throw new Exception("Missing or mismatched comparisons must not look successful.");
        }
        var progress = new SimulationProgress(4);
        progress.Append("Engine note\nBaseline\t1\t4\t50");
        Assert(!progress.HasProgress, "Partial pipe lines must not be parsed prematurely.");
        progress.Append("\t100\t100\t1\n");
        Assert(progress.Fraction == 0.125 && progress.CompletedRuns == 0
            && progress.EstimateRemaining(TimeSpan.FromSeconds(10)) == TimeSpan.FromSeconds(70), "Baseline progress must cover its share of the whole batch.");
        progress.Append("Profileset\tgear_0\t2\t4\t100\t100\t100\t1\r\n");
        Assert(progress.Fraction == 0.5 && progress.CompletedRuns == 2, "Completed gear runs must advance batch progress.");
        progress.Append("Profileset\tgear_2\t4\t4\t100\t100\t100\t1\nBaseline\t1\t99\t5\t100\t1\t1\n");
        Assert(progress.Fraction == 1 && progress.CompletedRuns == 4, "Malformed or stale progress must not roll back completion.");
        Console.WriteLine("PASS: bag variants, multi-slot combinations, ring pairs, limits, native results, uncertainty, and chunked progress/ETA.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected rejection: " + message);
    }
}
