using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// One dungeon/raid map: its background image plus the world-coordinate rectangle the image spans, so a
/// captured world (X, Y) can be projected onto the picture. Data-mined from the client (see
/// docs/replay-feature-plan.md); the projection is <c>u=(X-worldMinX)/(worldMaxX-worldMinX)·W</c>,
/// <c>v=(Y-worldMinY)/(worldMaxY-worldMinY)·H</c> — note world +Y maps DOWN the image.
/// </summary>
public sealed class ReplayMapInfo
{
    [JsonPropertyName("key")] public string Key { get; init; } = "";

    [JsonPropertyName("nameKo")] public string NameKo { get; init; } = "";

    [JsonPropertyName("image")] public string Image { get; init; } = "";

    [JsonPropertyName("imageWidth")] public int ImageWidth { get; init; }

    [JsonPropertyName("imageHeight")] public int ImageHeight { get; init; }

    [JsonPropertyName("worldMinX")] public double WorldMinX { get; init; }

    [JsonPropertyName("worldMinY")] public double WorldMinY { get; init; }

    [JsonPropertyName("worldMaxX")] public double WorldMaxX { get; init; }

    [JsonPropertyName("worldMaxY")] public double WorldMaxY { get; init; }

    [JsonPropertyName("bossCodes")] public int[] BossCodes { get; init; } = [];

    /// <summary>Absolute path to the map image beside the manifest (set at load time).</summary>
    [JsonIgnore] public string ImagePath { get; set; } = "";
}

/// <summary>
/// Loads the bundled dungeon-map manifest (<c>Maps/map-manifest.json</c> beside the exe) and resolves a
/// battle's boss <c>mobCode</c> to its map. Absent/partial data is non-fatal: the replay just falls back
/// to the relative auto-fit plot with no background.
/// </summary>
public sealed class ReplayMapCatalog
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<int, ReplayMapInfo> _byBoss = new();

    public int MapCount { get; private set; }

    private ReplayMapCatalog()
    {
    }

    /// <summary>Load from <c>{baseDir}/Maps/map-manifest.json</c>. Never throws — returns an empty catalog
    /// if the folder/file is missing or malformed.</summary>
    public static ReplayMapCatalog Load(string? baseDir = null)
    {
        var catalog = new ReplayMapCatalog();
        try
        {
            string dir = Path.Combine(baseDir ?? AppContext.BaseDirectory, "Maps");
            string manifest = Path.Combine(dir, "map-manifest.json");
            if (!File.Exists(manifest))
            {
                return catalog;
            }

            ReplayMapInfo[]? maps = JsonSerializer.Deserialize<ReplayMapInfo[]>(File.ReadAllText(manifest), Options);
            if (maps is null)
            {
                return catalog;
            }

            foreach (ReplayMapInfo map in maps)
            {
                map.ImagePath = Path.Combine(dir, map.Image);
                if (!File.Exists(map.ImagePath))
                {
                    continue; // manifest lists a map whose image wasn't shipped — skip it
                }

                catalog.MapCount++;
                foreach (int code in map.BossCodes)
                {
                    catalog._byBoss[code] = map;
                }
            }
        }
        catch
        {
            // a missing/corrupt catalog must never break opening a replay
        }

        return catalog;
    }

    /// <summary>The map whose boss list contains <paramref name="mobCode"/>, or null.</summary>
    public ReplayMapInfo? ForBoss(int? mobCode)
        => mobCode is { } c && _byBoss.TryGetValue(c, out ReplayMapInfo? m) ? m : null;
}
