using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WaffleMeter.Replay;

/// <summary>
/// Compact JSON (de)serialization of a <see cref="ReplayRecording"/> for on-disk history and stats-web
/// upload. Points are emitted as flat <c>[t,x,y,z]</c> arrays (not objects) to keep a multi-minute,
/// multi-entity recording small. A binary container is a future optimization (see plan); JSON v1 keeps
/// the web contract trivial to consume.
/// </summary>
public static class ReplaySerializer
{
    public static string Serialize(ReplayRecording rec, bool indented = false)
    {
        var buffer = new ArrayBufferWriter<byte>(64 * 1024);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            w.WriteStartObject();
            w.WriteNumber("schema", rec.Schema);
            w.WriteNumber("epoch", rec.BattleEpoch);
            w.WriteNumber("startMs", rec.StartMs);
            w.WriteNumber("endMs", rec.EndMs);
            w.WriteBoolean("bossDefeated", rec.BossDefeated);
            if (rec.TargetCode is { } tc)
            {
                w.WriteNumber("targetCode", tc);
            }

            if (rec.TargetName is { } tn)
            {
                w.WriteString("targetName", tn);
            }

            w.WriteStartArray("tracks");
            foreach (ReplayTrack t in rec.Tracks)
            {
                w.WriteStartObject();
                w.WriteNumber("uid", t.Uid);
                w.WriteString("nick", t.Nickname ?? "");
                w.WriteNumber("srv", t.Server);
                w.WriteString("job", t.Job ?? "");
                w.WriteBoolean("self", t.IsSelf);
                w.WriteBoolean("target", t.IsTarget);
                w.WriteNumber("slot", t.PartySlot);
                w.WriteString("op", t.SourceOpcode.ToString("X4", CultureInfo.InvariantCulture));
                w.WriteNumber("off", t.SourceOffset);
                w.WriteStartArray("pts");
                foreach (ReplayPoint p in t.Points)
                {
                    w.WriteStartArray();
                    w.WriteNumberValue(p.TMs);
                    w.WriteNumberValue(p.X);
                    w.WriteNumberValue(p.Y);
                    w.WriteNumberValue(p.Z);
                    w.WriteEndArray();
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndArray();

            // Boss mechanics (schema 2). Targets are flat [uid,x,y,z] arrays, same rationale as pts.
            if (rec.Casts.Count > 0)
            {
                w.WriteStartArray("casts");
                foreach (ReplayCast c in rec.Casts)
                {
                    w.WriteStartObject();
                    w.WriteNumber("t", c.TMs);
                    w.WriteNumber("skill", c.SkillCode);
                    w.WriteNumber("face", c.FacingDeg);
                    w.WriteNumber("hp", c.HpFraction);
                    w.WriteStartArray("tgts");
                    foreach (ReplayCastTarget t in c.Targets)
                    {
                        w.WriteStartArray();
                        w.WriteNumberValue(t.Uid);
                        w.WriteNumberValue(t.X);
                        w.WriteNumberValue(t.Y);
                        w.WriteNumberValue(t.Z);
                        w.WriteEndArray();
                    }

                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static ReplayRecording Deserialize(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        var tracks = new List<ReplayTrack>();
        if (root.TryGetProperty("tracks", out JsonElement tracksEl))
        {
            foreach (JsonElement te in tracksEl.EnumerateArray())
            {
                var pts = new List<ReplayPoint>();
                if (te.TryGetProperty("pts", out JsonElement ptsEl))
                {
                    foreach (JsonElement pe in ptsEl.EnumerateArray())
                    {
                        pts.Add(new ReplayPoint(
                            pe[0].GetInt32(),
                            pe[1].GetSingle(),
                            pe[2].GetSingle(),
                            pe[3].GetSingle()));
                    }
                }

                tracks.Add(new ReplayTrack
                {
                    Uid = GetInt(te, "uid"),
                    Nickname = GetStr(te, "nick"),
                    Server = GetInt(te, "srv"),
                    Job = GetStr(te, "job"),
                    IsSelf = GetBool(te, "self"),
                    IsTarget = GetBool(te, "target"),
                    PartySlot = GetInt(te, "slot"),
                    SourceOpcode = te.TryGetProperty("op", out JsonElement opEl)
                        ? int.Parse(opEl.GetString() ?? "0", NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                        : 0,
                    SourceOffset = GetInt(te, "off"),
                    Points = pts,
                });
            }
        }

        var casts = new List<ReplayCast>();
        if (root.TryGetProperty("casts", out JsonElement castsEl))
        {
            foreach (JsonElement ce in castsEl.EnumerateArray())
            {
                var targets = new List<ReplayCastTarget>();
                if (ce.TryGetProperty("tgts", out JsonElement tgtsEl))
                {
                    foreach (JsonElement te in tgtsEl.EnumerateArray())
                    {
                        targets.Add(new ReplayCastTarget(
                            te[0].GetInt32(), te[1].GetSingle(), te[2].GetSingle(), te[3].GetSingle()));
                    }
                }

                casts.Add(new ReplayCast
                {
                    TMs = GetInt(ce, "t"),
                    SkillCode = GetInt(ce, "skill"),
                    FacingDeg = GetFloat(ce, "face", 0f),
                    HpFraction = GetFloat(ce, "hp", -1f),
                    Targets = targets,
                });
            }
        }

        return new ReplayRecording
        {
            Schema = GetInt(root, "schema"),
            BattleEpoch = GetLong(root, "epoch"),
            StartMs = GetLong(root, "startMs"),
            EndMs = GetLong(root, "endMs"),
            BossDefeated = GetBool(root, "bossDefeated"),
            TargetCode = root.TryGetProperty("targetCode", out JsonElement tcEl) ? tcEl.GetInt32() : null,
            TargetName = GetStr(root, "targetName"),
            Tracks = tracks,
            Casts = casts,
        };
    }

    private static float GetFloat(JsonElement e, string n, float fallback)
        => e.TryGetProperty(n, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    private static int GetInt(JsonElement e, string n) => e.TryGetProperty(n, out JsonElement v) ? v.GetInt32() : 0;

    private static long GetLong(JsonElement e, string n) => e.TryGetProperty(n, out JsonElement v) ? v.GetInt64() : 0;

    private static bool GetBool(JsonElement e, string n) => e.TryGetProperty(n, out JsonElement v) && v.GetBoolean();

    private static string? GetStr(JsonElement e, string n)
    {
        if (!e.TryGetProperty(n, out JsonElement v) || v.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string s = v.GetString() ?? "";
        return s.Length == 0 ? null : s;
    }
}
