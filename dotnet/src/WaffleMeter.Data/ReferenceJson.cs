using System.Globalization;
using System.Text.Json;
using WaffleMeter.Capture;

namespace WaffleMeter.Data;

/// <summary>
/// Loaders for the reference JSON assets (mobs.json / skills.json), mirroring Kotlin
/// DataManager.loadMobJson / loadSkillJson. In the WPF app these ship as embedded resources; here
/// they load from a path so tests and the replay CLI can point at the existing resources.
/// </summary>
public static class ReferenceJson
{
    /// <summary>mobs.json: array of { "code": int, "name": string, "boss": bool }.</summary>
    public static Dictionary<int, Mob> LoadMobs(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var mobs = new Dictionary<int, Mob>();
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            int code = el.GetProperty("code").GetInt32();
            string name = el.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
            bool boss = el.TryGetProperty("boss", out JsonElement b) && b.GetBoolean();
            mobs[code] = new Mob(code, name, boss); // last write wins (matches Kotlin HashMap.put)
        }

        return mobs;
    }

    /// <summary>skills.json: array of { "code": int, "name": string }. Returns the code set.</summary>
    public static HashSet<long> LoadSkillCodes(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var codes = new HashSet<long>();
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            if (el.TryGetProperty("code", out JsonElement c))
            {
                codes.Add(c.GetInt64());
            }
        }

        return codes;
    }

    /// <summary>skills.json: array of { "code": int, "name": string? } -> Skill list (with names).</summary>
    public static List<Skill> LoadSkills(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var skills = new List<Skill>();
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            long code = el.GetProperty("code").GetInt64();
            string? name = el.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()
                : null;
            skills.Add(new Skill(code, name));
        }

        return skills;
    }

    /// <summary>
    /// buff.json / buff_custom.json: object keyed by code -> { name, summary, effect, ... }. A Buff
    /// is created only when BOTH summary and effect are present (Kotlin loadBuffJson). Last write wins.
    /// </summary>
    public static List<Buff> LoadBuffs(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var buffs = new List<Buff>();
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            JsonElement obj = prop.Value;
            if (obj.TryGetProperty("effect", out JsonElement eff) && eff.ValueKind == JsonValueKind.String
                && obj.TryGetProperty("summary", out JsonElement sum) && sum.ValueKind == JsonValueKind.String)
            {
                string name = obj.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() ?? ""
                    : "";
                buffs.Add(new Buff(int.Parse(prop.Name, CultureInfo.InvariantCulture), name, sum.GetString() ?? "", eff.GetString() ?? ""));
            }
        }

        return buffs;
    }

    /// <summary>buff_names.json: object keyed by base skill code -> { "n": name, "j": job }. Used to label
    /// the per-job buff picker offline (no live packet needed).</summary>
    public static List<(int Code, string Name, string Job)> LoadBuffNames(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new List<(int, string, string)>();
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
            {
                continue;
            }

            string name = prop.Value.TryGetProperty("n", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
            string job = prop.Value.TryGetProperty("j", out JsonElement j) && j.ValueKind == JsonValueKind.String ? j.GetString() ?? "" : "";
            result.Add((code, name, job));
        }

        return result;
    }

    /// <summary>buff_catalog.json: { "buffs": { code: { "n": name, "j": job } }, "defaultOff": [code, ...] }.
    /// Curated self-buff bases (datamine-verified) the picker lists up front + the default-off toggle subset.</summary>
    public static (List<(int Code, string Name, string Job)> Catalog, List<int> DefaultOff) LoadBuffCatalog(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var catalog = new List<(int, string, string)>();
        var defaultOff = new List<int>();
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("buffs", out JsonElement buffs) && buffs.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in buffs.EnumerateObject())
            {
                if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                {
                    continue;
                }

                string name = prop.Value.TryGetProperty("n", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                string job = prop.Value.TryGetProperty("j", out JsonElement j) && j.ValueKind == JsonValueKind.String ? j.GetString() ?? "" : "";
                catalog.Add((code, name, job));
            }
        }

        if (root.TryGetProperty("defaultOff", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in arr.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int c))
                {
                    defaultOff.Add(c);
                }
            }
        }

        return (catalog, defaultOff);
    }

    /// <summary>buff_blacklist.json: { "blacklist": [int, ...] }.</summary>
    public static List<int> LoadBuffBlacklist(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new List<int>();
        if (doc.RootElement.TryGetProperty("blacklist", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in arr.EnumerateArray())
            {
                result.Add(e.GetInt32());
            }
        }

        return result;
    }
}
