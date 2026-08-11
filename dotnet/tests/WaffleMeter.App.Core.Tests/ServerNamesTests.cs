using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class ServerNamesTests
{
    [Theory]
    [InlineData(2001, "이스")]   // 이스라펠
    [InlineData(2021, "할겐")]   // 이스할겐 — 2026-08-12 패치가 이스라펠과 갈라놓은 축약
    [InlineData(1001, "시엘")]
    [InlineData(2008, "브리")]   // 브리트라
    [InlineData(1008, "메스")]   // 메스람타에다
    public void Label_matches_live_abbreviation(int server, string expected)
    {
        Assert.Equal(expected, ServerNames.GetServerLabel(server));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2022)]    // 클라이언트엔 있지만 라이브에 없는 id
    [InlineData(47200)]   // 2026-07-30 신원 오염이 남긴 쓰레기 서버값
    public void Unknown_server_has_no_label(int server)
    {
        Assert.Equal(string.Empty, ServerNames.GetServerLabel(server));
    }

    /// <summary>
    /// 축약이 겹치면 "닉네임 [이스]"가 두 서버를 가리키게 된다 — 이스라펠/이스할겐이 정확히 그랬고
    /// 그래서 override가 생겼다. 서버가 추가될 때 같은 충돌이 조용히 돌아오는 것을 막는 가드다.
    /// </summary>
    [Fact]
    public void Labels_are_unique_across_servers()
    {
        var byLabel = new Dictionary<string, List<int>>();
        foreach (int id in ServerNames.KnownServerIds)
        {
            string label = ServerNames.GetServerLabel(id);
            Assert.NotEqual(string.Empty, label);
            if (!byLabel.TryGetValue(label, out List<int>? ids))
            {
                ids = [];
                byLabel[label] = ids;
            }

            ids.Add(id);
        }

        var collisions = byLabel.Where(kv => kv.Value.Count > 1)
            .Select(kv => $"{kv.Key} <- {string.Join(", ", kv.Value)}")
            .ToList();
        Assert.Empty(collisions);
    }

    /// <summary>라이브 서버는 진영당 21개다(천족 1001~1021, 마족 2001~2021).</summary>
    [Fact]
    public void Table_covers_the_21_live_servers_per_faction()
    {
        var ids = ServerNames.KnownServerIds.ToHashSet();
        Assert.Equal(42, ids.Count);
        for (int i = 1; i <= 21; i++)
        {
            Assert.Contains(1000 + i, ids);
            Assert.Contains(2000 + i, ids);
        }
    }
}
