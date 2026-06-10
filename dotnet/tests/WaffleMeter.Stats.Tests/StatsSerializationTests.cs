using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

public sealed class StatsSerializationTests
{
    [Fact]
    public void Character_payload_uses_public_key_camelcase_and_omits_null_job()
    {
        var payload = new StatsCharacterPayload(
            IdentityHash: "abc",
            Nickname: "Hero",
            Server: 3,
            Job: null,
            Power: 1000,
            Public: true);

        string json = StatsJson.Serialize(payload);

        Assert.Contains("\"identityHash\":\"abc\"", json);
        Assert.Contains("\"public\":true", json);
        Assert.DoesNotContain("\"Public\"", json);
        Assert.DoesNotContain("\"job\"", json); // null omitted (explicitNulls=false)
    }

    [Fact]
    public void Encounter_payload_omits_null_optionals_but_keeps_set_ones()
    {
        string omitted = StatsJson.Serialize(new StatsEncounterPayload(MobCode: 5, BossName: "Boss"));
        Assert.DoesNotContain("dungeonName", omitted);
        Assert.DoesNotContain("stage", omitted);

        string set = StatsJson.Serialize(new StatsEncounterPayload(5, "Boss", DungeonName: "Abyss", Stage: 2));
        Assert.Contains("\"dungeonName\":\"Abyss\"", set);
        Assert.Contains("\"stage\":2", set);
    }

    [Fact]
    public void Own_character_writes_non_null_defaults()
    {
        // encodeDefaults=true: detected:false and the numeric defaults are written; only nulls drop.
        string json = StatsJson.Serialize(new StatsOwnCharacter(Detected: false));
        Assert.Contains("\"detected\":false", json);
        Assert.Contains("\"server\":-1", json);
        Assert.DoesNotContain("nickname", json); // null
    }

    [Fact]
    public void Consent_status_response_reads_public_key_and_ignores_unknown()
    {
        const string serverJson = """
            {"ok":true,"identityHash":"h","exists":true,"consentState":"accepted",
             "public":true,"consentVersion":"2026-06-04","unexpectedField":42}
            """;

        ConsentStatusResponse response = StatsJson.Deserialize<ConsentStatusResponse>(serverJson);

        Assert.True(response.Ok);
        Assert.True(response.Exists);
        Assert.Equal("accepted", response.ConsentState);
        Assert.True(response.PublicCharacter);
        Assert.Equal("2026-06-04", response.ConsentVersion);
        Assert.Null(response.LastSeenAt); // missing -> default null
    }

    [Fact]
    public void Consent_event_character_serializes_public_key()
    {
        var character = new ConsentEventCharacter(
            IdentityHash: "h",
            Nickname: "Hero",
            Server: 3,
            PublicCharacter: false,
            Job: "검성",
            Power: 1234);

        string json = StatsJson.Serialize(character);

        Assert.Contains("\"public\":false", json);
        Assert.Contains("\"job\":\"검성\"", json);
        Assert.Contains("\"power\":1234", json);
    }
}
