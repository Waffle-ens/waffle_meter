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
}
