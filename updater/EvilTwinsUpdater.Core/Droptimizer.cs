using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvilTwinsUpdater.Core;

public sealed record SimUpgrade(int DropId, int SimulatedId, string Name, string Instance,
    int InstanceId, int BossId, string Boss, string Difficulty, int ItemLevel, string Slot,
    double Gain, double Percent, bool WithinError, string BonusIds);

public sealed record DropReport(string Name, string Realm, string Region, int SpecId, string ReportId,
    long Timestamp, string Title, string FightStyle, int Duration, int Enemies, double Baseline,
    string Scope, string Talents, string Assumptions, IReadOnlyDictionary<string, string> Gear,
    IReadOnlyList<SimUpgrade> Items)
{
    public string ImportCode()
    {
        string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
        var fields = new List<string> { "1", Name, Realm, Region, SpecId.ToString(), ReportId,
            Timestamp.ToString(), Title, FightStyle, Duration.ToString(), Enemies.ToString(),
            Number(Baseline), Scope, Talents, Assumptions, Gear.Count.ToString(), Items.Count.ToString() };
        foreach (var (slot, signature) in Gear.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            fields.AddRange([slot, signature]);
        foreach (var item in Items)
            fields.AddRange([item.DropId.ToString(), item.SimulatedId.ToString(), item.Name,
                item.Instance, item.InstanceId.ToString(), item.BossId.ToString(), item.Boss,
                item.Difficulty, item.ItemLevel.ToString(), item.Slot, Number(item.Gain),
                Number(item.Percent), item.WithinError ? "1" : "0", item.BonusIds]);
        var packed = string.Concat(fields.Select(value => Encoding.UTF8.GetByteCount(value) + ":" + value));
        if (Encoding.UTF8.GetByteCount(packed) > 90000) throw new InvalidDataException("Report is too large to share in-game.");
        return "ETD1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(packed));
    }
}

public sealed partial class DroptimizerClient : IDisposable
{
    private readonly HttpClient client;
    public DroptimizerClient(HttpMessageHandler? handler = null)
    {
        client = handler == null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EvilTwinsUpdater/0.1.0");
    }

    [GeneratedRegex("^[A-Za-z0-9]{10,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    public static string ReportId(string input)
    {
        input = input.Trim();
        if (IdPattern().IsMatch(input)) return input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || (uri.Host != "www.raidbots.com" && uri.Host != "raidbots.com"))
            throw new InvalidDataException("Paste a completed Raidbots Droptimizer report link.");
        var pieces = uri.AbsolutePath.Trim('/').Split('/');
        if (pieces.Length != 3 || pieces[0] != "simbot" || pieces[1] != "report" || !IdPattern().IsMatch(pieces[2]))
            throw new InvalidDataException("The link must point to a Raidbots report.");
        return pieces[2];
    }

    public async Task<DropReport> FetchAsync(string input)
    {
        var id = ReportId(input);
        using var response = await client.GetAsync($"https://www.raidbots.com/reports/{id}/data.json", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        const int maximum = 30 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Report exceeds the download limit.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var inputStream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var data = new MemoryStream();
        var buffer = new byte[65536];
        int read;
        while ((read = await inputStream.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (data.Length + read > maximum) throw new InvalidDataException("Report exceeds the download limit.");
            data.Write(buffer, 0, read);
        }
        using var json = JsonDocument.Parse(data.ToArray(), new JsonDocumentOptions { MaxDepth = 80 });
        return Parse(json.RootElement, id);
    }

    private static JsonElement Field(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : default;
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static string Text(JsonElement value, string name) => Text(Field(value, name));
    private static double Numeric(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : 0;
    private static int Integer(JsonElement value, string name) => (int)Numeric(Field(value, name));
    private static IEnumerable<JsonElement> Array(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static string Clean(string value, int maximum = 200)
    {
        value = value.Replace("|", "").Replace("\r", " ").Replace("\n", " ");
        if (value.Length > maximum) value = value[..maximum];
        return value;
    }

    // These are exactly the gear properties used by the addon's native SimC exporter.
    public static string GearSignature(string encoded)
    {
        var keys = new[] { "id", "enchant_id", "gem_id", "bonus_id", "crafted_stats" };
        var properties = encoded.Split(',').Where(part => part.Contains('='))
            .Select(part => part.Split('=', 2)).ToDictionary(part => part[0].Trim(), part => part[1].Trim());
        return string.Join(";", keys.Select(key => {
            if (!properties.TryGetValue(key, out var value)) return "";
            return string.Join("/", value.Split('/').Select(part => int.TryParse(part, out var n) ? n : 0)
                .Where(n => n > 0).OrderBy(n => n));
        }));
    }

    public static DropReport Parse(JsonElement root, string id)
    {
        var simbot = Field(root, "simbot");
        var meta = Field(simbot, "meta");
        var sim = Field(root, "sim");
        if (Text(simbot, "simType") != "droptimizer" || Text(meta, "type") != "droptimizer")
            throw new InvalidDataException("This is not a completed Droptimizer report.");
        var parent = Text(simbot, "parentSimId");
        if ((parent.Length > 0 ? parent : Text(simbot, "simId")) != id)
            throw new InvalidDataException("The report identity does not match its link.");
        var profiles = Field(sim, "profilesets");
        if (Text(profiles, "metric") != "Damage per Second") throw new InvalidDataException("Only DPS Droptimizer reports are supported.");
        var player = Array(Field(sim, "players")).FirstOrDefault();
        var dps = Field(Field(Field(player, "collected_data"), "dps"), "mean");
        var baseline = Numeric(dps);
        if (!double.IsFinite(baseline) || baseline <= 0) throw new InvalidDataException("The completed report has no DPS baseline.");
        var form = Field(meta, "rawFormData");
        var profile = Field(form, "profile");
        var identity = Field(profile, "identity");
        var name = Text(identity, "name");
        var realm = Text(identity, "realm");
        var region = Text(identity, "region").ToLowerInvariant();
        var spec = Integer(identity, "specId");
        if (name.Length == 0 || realm.Length == 0 || spec <= 0 || !new[] { "us", "eu", "kr", "tw", "cn" }.Contains(region))
            throw new InvalidDataException("Report lacks character, realm, region, or specialization data.");
        var items = Array(Field(meta, "itemLibrary")).ToArray();
        var positive = new List<SimUpgrade>();
        var scope = new SortedSet<string>(StringComparer.Ordinal);
        var baseError = Numeric(Field(Field(Field(player, "collected_data"), "dps"), "mean_std_dev")) * 1.96;
        var results = Array(Field(profiles, "results")).ToArray();
        if (results.Length == 0) throw new InvalidDataException("No completed item simulations were found.");
        foreach (var result in results)
        {
            var parts = Text(result, "name").Split('/');
            if (parts.Length < 7 || !int.TryParse(parts[0], out var instance) || !int.TryParse(parts[1], out var boss)
                || !int.TryParse(parts[3], out var simulated) || !int.TryParse(parts[4], out var level))
                throw new InvalidDataException("Raidbots changed its result format; this report cannot be imported safely.");
            scope.Add(instance + "/" + parts[2]);
            var mean = Numeric(Field(result, "mean"));
            if (!double.IsFinite(mean) || mean <= 0) throw new InvalidDataException("The report contains an unfinished item simulation.");
            var gain = mean - baseline;
            if (gain <= 0) continue;
            var drop = parts.Length > 10 && int.TryParse(parts[10], out var original) && original > 0 ? original : simulated;
            JsonElement Match(int itemId) => items.FirstOrDefault(item => Integer(item, "id") == itemId
                && Integer(item, "instanceId") == instance && Integer(item, "encounterId") == boss
                && Text(item, "difficulty") == parts[2] && Integer(item, "itemLevel") == level);
            var candidate = Match(simulated);
            var originalItem = Match(drop);
            if (candidate.ValueKind != JsonValueKind.Object || originalItem.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("A simulated item could not be matched to its source and item level.");
            var instanceName = Text(Field(originalItem, "instance"), "name");
            var bossName = Text(Field(originalItem, "encounter"), "name");
            var error = Numeric(Field(result, "mean_error"));
            positive.Add(new(drop, simulated, Clean(Text(originalItem, "name")), Clean(instanceName), instance, boss,
                Clean(bossName), Clean(parts[2], 40), level, Clean(parts[6], 30), gain, gain / baseline * 100,
                gain <= Math.Sqrt(error * error + baseError * baseError), Text(candidate, "bonus_id")));
        }
        // A ring can be simulated in both slots. Keep the strongest result for each exact drop/source/level.
        var upgrades = positive.GroupBy(item => (item.DropId, item.InstanceId, item.BossId, item.Difficulty, item.ItemLevel))
            .Select(group => group.OrderByDescending(item => item.Gain).First()).OrderByDescending(item => item.Gain).Take(100).ToArray();
        var gear = new Dictionary<string, string>();
        // Prefer the original input, because simulation options may upgrade equipped gear before simming.
        var equipped = Field(profile, "equipped");
        if (equipped.ValueKind == JsonValueKind.Object)
            foreach (var slot in equipped.EnumerateObject())
            {
                var values = new List<string>();
                foreach (var key in new[] { "id", "enchant_id", "gem_id", "bonus_id", "crafted_stats" })
                {
                    var value = Field(slot.Value, key);
                    if (value.ValueKind == JsonValueKind.String) values.Add(key + "=" + value.GetString());
                    else if (value.ValueKind == JsonValueKind.Number) values.Add(key + "=" + value.GetRawText());
                }
                var slotName = slot.Name switch { "mainHand" => "main_hand", "offHand" => "off_hand", _ => slot.Name };
                gear[slotName] = GearSignature(string.Join(',', values));
            }
        var options = Field(form, "droptimizer");
        var assumptions = "Equipped upgrades: " + (Text(options, "upgradeEquippedMode") is { Length: > 0 } mode ? mode : "unknown")
            + "; conversions: " + (Field(options, "includeConversions").ValueKind == JsonValueKind.True ? "included" : "not included");
        var timestamp = (long)Numeric(Field(root, "timestamp"));
        if (timestamp <= 0) timestamp = (long)(Numeric(Field(simbot, "date")) / 1000);
        if (timestamp <= 0 || timestamp > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300) throw new InvalidDataException("Report has an invalid timestamp.");
        return new(Clean(name, 60), Clean(realm, 100), region, spec, id, timestamp, Clean(Text(meta, "title")),
            Clean(Text(simbot, "fightStyle"), 50), Integer(simbot, "fightLength"), Integer(simbot, "enemyCount"),
            baseline, string.Join(',', scope), Text(player, "talents"), assumptions, gear, upgrades);
    }

    public void Dispose() => client.Dispose();
}
