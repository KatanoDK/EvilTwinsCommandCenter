using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EvilTwinsUpdater.Core;

public sealed record RaidMember(string Key, string Guid, string Role, int SpecId, long Updated, string Profile, string State)
{
    public bool Ready => State == "Ready" && Profile.Length > 0 && DpsSpecs.All.ContainsKey(SpecId);
}
public sealed record RaidExport(long Captured, string Region, IReadOnlyList<RaidMember> Members)
{
    public static RaidExport Parse(string code)
    {
        code = code.Trim();
        if (!code.StartsWith("ETR1:", StringComparison.Ordinal) || code.Length > 4_800_010)
            throw new InvalidDataException("Copy a Raid DPS export from the addon's Build & SimC tab.");
        byte[] data;
        try { data = Convert.FromBase64String(code[5..]); }
        catch (FormatException error) { throw new InvalidDataException("The raid export is incomplete or damaged.", error); }
        if (data.Length > 3_600_000) throw new InvalidDataException("The raid export exceeds the size limit.");
        var fields = new List<string>(); var offset = 0; var utf8 = new UTF8Encoding(false, true);
        while (offset < data.Length)
        {
            var colon = Array.IndexOf(data, (byte)':', offset);
            if (colon < offset + 1 || colon - offset > 7 || fields.Count >= 284
                || !int.TryParse(Encoding.ASCII.GetString(data, offset, colon - offset), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var length) || length > 89_000 || length > data.Length - colon - 1)
                throw new InvalidDataException("The raid export contains an invalid field.");
            try { fields.Add(utf8.GetString(data, colon + 1, length)); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid text in the raid export.", error); }
            offset = colon + 1 + length;
        }
        long Number(string value, long min, long max)
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < min || number > max)
                throw new InvalidDataException("The raid export contains an invalid number.");
            return number;
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (fields.Count < 4 || fields[0] != "1" || !new[] { "us", "eu", "kr", "tw", "cn" }.Contains(fields[2]))
            throw new InvalidDataException("Unsupported raid export version or region.");
        var captured = Number(fields[1], 1, now + 300);
        var count = (int)Number(fields[3], 1, 40);
        if (fields.Count != 4 + count * 7) throw new InvalidDataException("The raid export is missing members.");
        var members = new List<RaidMember>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var guids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 4; index < fields.Count; index += 7)
        {
            var key = fields[index]; var guid = fields[index + 1]; var role = fields[index + 2];
            var spec = (int)Number(fields[index + 3], 0, 10_000);
            var updated = Number(fields[index + 4], 0, Math.Min(now + 300, captured + 300));
            var profile = fields[index + 5]; var state = fields[index + 6];
            if (key.Length is < 3 or > 160 || !key.Contains('-') || key.Any(c => char.IsControl(c) || c == '|')
                || guid.Length is < 1 or > 64 || !Regex.IsMatch(guid, "^[A-Za-z0-9-]+$")
                || !names.Add(key) || !guids.Add(guid) || !new[] { "DAMAGER", "NONE", "TANK", "HEALER" }.Contains(role)
                || state.Length > 100 || state.Any(c => char.IsControl(c) || c == '|'))
                throw new InvalidDataException("The raid export contains invalid or duplicate members.");
            if (state == "Ready")
            {
                if (updated == 0 || role is "TANK" or "HEALER" || !DpsSpecs.All.TryGetValue(spec, out var identity))
                    throw new InvalidDataException("A ready member does not have a DPS profile.");
                var prepared = LocalSimulator.PrepareProfile(profile);
                var values = prepared.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim().Split('=', 2)).ToArray();
                if (!values.Any(pair => pair[0].Replace("_", "") == identity.ClassToken && pair[1].Trim('"') == key.Split('-', 2)[0])
                    || values.Count(pair => pair[0] == "spec") != 1 || !values.Any(pair => pair[0] == "spec" && pair[1] == identity.Token))
                    throw new InvalidDataException($"{key}'s export does not match its character or specialization.");
            }
            else if (profile.Length > 0) throw new InvalidDataException("Only ready DPS members can contain profiles.");
            members.Add(new(key, guid, role, spec, updated, profile, state));
        }
        return new(captured, fields[2], members);
    }
}

public sealed record DpsSpec(int ClassId, string ClassToken, string Token, int Armor, int PrimaryStat);
public static class DpsSpecs
{
    public static readonly IReadOnlyDictionary<int, DpsSpec> All = new Dictionary<int, DpsSpec> {
        [71] = new(1,"warrior","arms",4,4), [72] = new(1,"warrior","fury",4,4), [70] = new(2,"paladin","retribution",4,4),
        [253] = new(3,"hunter","beast_mastery",3,3), [254] = new(3,"hunter","marksmanship",3,3), [255] = new(3,"hunter","survival",3,3),
        [259] = new(4,"rogue","assassination",2,3), [260] = new(4,"rogue","outlaw",2,3), [261] = new(4,"rogue","subtlety",2,3),
        [258] = new(5,"priest","shadow",1,5), [251] = new(6,"deathknight","frost",4,4), [252] = new(6,"deathknight","unholy",4,4),
        [262] = new(7,"shaman","elemental",3,5), [263] = new(7,"shaman","enhancement",3,3),
        [62] = new(8,"mage","arcane",1,5), [63] = new(8,"mage","fire",1,5), [64] = new(8,"mage","frost",1,5),
        [265] = new(9,"warlock","affliction",1,5), [266] = new(9,"warlock","demonology",1,5), [267] = new(9,"warlock","destruction",1,5),
        [269] = new(10,"monk","windwalker",2,3), [102] = new(11,"druid","balance",2,5), [103] = new(11,"druid","feral",2,3),
        [577] = new(12,"demonhunter","havoc",2,3), [1480] = new(12,"demonhunter","devourer",2,5),
        [1467] = new(13,"evoker","devastation",3,5), [1473] = new(13,"evoker","augmentation",3,5),
    };
}
