using WaffleMeter.Data;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.Services.Tests;

public sealed class OfficialCharacterLookupTests
{
    private const string SearchJson = """
        {"list":[
          {"name":"<b>Waffle</b>","serverId":3,"characterId":"abc%3D","level":80,"pcId":5},
          {"name":"Waffle","serverId":3,"characterId":"low%3D","level":50,"pcId":5}
        ]}
        """;

    private const string EquipmentJson = """
        {"skill":{"skillList":[
          {"acquired":1,"equip":1,"id":11000001,"skillLevel":3},
          {"acquired":1,"equip":0,"id":11000002,"skillLevel":5},
          {"acquired":0,"equip":1,"id":11000003,"skillLevel":2}
        ]}}
        """;

    private const string InfoJson = """{"profile":{"combatPower":12345}}""";

    private static string Route(string url)
    {
        if (url.Contains("/api/search/character"))
        {
            return SearchJson;
        }

        if (url.Contains("/api/character/equipment"))
        {
            return EquipmentJson;
        }

        if (url.Contains("/api/character/info"))
        {
            return InfoJson;
        }

        throw new InvalidOperationException("unexpected url " + url);
    }

    [Fact]
    public void Resolves_job_power_and_equipped_skills()
    {
        var lookup = new OfficialCharacterLookup(Route, clock: () => 0);

        OfficialCharacterInfo? info = lookup.LookupBlocking("Waffle", 3, fallbackJob: JobClass.CLERIC);

        Assert.NotNull(info);
        Assert.Equal("Waffle", info!.Nickname);
        Assert.Equal(3, info.Server);
        Assert.Equal(JobClass.GLADIATOR, info.Job); // pcId 5 -> GLADIATOR (not the fallback)
        Assert.Equal(12345, info.Power);
        Assert.Equal(new Dictionary<int, int> { [11000001] = 3 }, info.Skills); // only acquired>0 && equip==1
    }

    [Fact]
    public void Caches_hits_and_skips_further_http()
    {
        int calls = 0;
        string Counting(string url)
        {
            calls++;
            return Route(url);
        }

        var lookup = new OfficialCharacterLookup(Counting, clock: () => 1000);

        OfficialCharacterInfo? first = lookup.LookupBlocking("Waffle", 3, null);
        int afterFirst = calls;
        OfficialCharacterInfo? second = lookup.LookupBlocking("Waffle", 3, null);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(3, afterFirst);   // search + equipment + info
        Assert.Equal(3, calls);        // second call served from cache, no new HTTP
    }

    [Fact]
    public void Falls_back_to_job_when_pcId_absent()
    {
        string Search(string url) => url.Contains("/api/search/character")
            ? """{"list":[{"name":"Waffle","serverId":3,"characterId":"abc","level":80}]}"""
            : Route(url);

        var lookup = new OfficialCharacterLookup(Search, clock: () => 0);

        OfficialCharacterInfo? info = lookup.LookupBlocking("Waffle", 3, fallbackJob: JobClass.RANGER);

        Assert.NotNull(info);
        Assert.Equal(JobClass.RANGER, info!.Job); // character.job null -> fallback
    }

    [Fact]
    public void Returns_null_and_caches_miss_when_not_found()
    {
        int calls = 0;
        string Empty(string url)
        {
            calls++;
            return url.Contains("/api/search/character") ? """{"list":[]}""" : Route(url);
        }

        var lookup = new OfficialCharacterLookup(Empty, clock: () => 0);

        Assert.Null(lookup.LookupBlocking("Ghost", 3, null));
        Assert.Equal(1, calls); // only the search call; no equipment/info
        Assert.Null(lookup.LookupBlocking("Ghost", 3, null));
        Assert.Equal(1, calls); // miss cached -> no new HTTP
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData("", 3)]
    [InlineData("   ", 3)]
    [InlineData("Waffle", 0)]
    [InlineData("Waffle", -1)]
    public void Guards_blank_nickname_and_nonpositive_server(string? nickname, int server)
    {
        var lookup = new OfficialCharacterLookup(_ => throw new InvalidOperationException("must not hit HTTP"), clock: () => 0);
        Assert.Null(lookup.LookupBlocking(nickname, server, null));
    }
}
