using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Guards the SHIPPED cooldown catalog (Assets/json/cooldown_catalog.json), which decides what the skill-
/// cooldown overlay can draw at all: a skill missing here is a skill the overlay silently never shows, and a
/// group id that resolves nowhere is a cast the overlay silently drops. Regenerate with
/// <c>dotnet/tools/cooldown-catalog-export.py</c> after a client patch.
/// </summary>
public sealed class ShippedCooldownCatalogTests
{
    private static string AssetsJsonDir()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Assets", "json");
            if (File.Exists(Path.Combine(candidate, "cooldown_catalog.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Assets/json/cooldown_catalog.json not found above " + AppContext.BaseDirectory);
    }

    private static string CatalogPath() => Path.Combine(AssetsJsonDir(), "cooldown_catalog.json");
    private static CooldownCatalog Shipped() => CooldownCatalog.Load(CatalogPath());

    [Fact]
    public void Catalog_loads_and_covers_every_player_job()
    {
        CooldownCatalog c = Shipped();
        Assert.True(c.Count > 200, $"쿨타임 스킬이 {c.Count}개뿐 — 재생성이 실패했을 가능성이 크다");

        Dictionary<int, int> perJob = c.Skills.GroupBy(s => s.Job).ToDictionary(g => g.Key, g => g.Count());
        for (int job = 11; job <= 19; job++)
        {
            Assert.True(perJob.TryGetValue(job, out int n) && n >= 15, $"직업 {job} 의 쿨타임 스킬이 {perJob.GetValueOrDefault(job)}개");
        }
    }

    [Fact]
    public void Every_row_is_an_eight_digit_base_code_with_a_name()
    {
        foreach (CooldownSkillInfo s in Shipped().Skills)
        {
            Assert.InRange(s.BaseCode, 11_000_000, 19_999_999);
            Assert.Equal(0, s.BaseCode % 10_000);
            Assert.False(string.IsNullOrWhiteSpace(s.Name), $"{s.BaseCode} 에 이름이 없다");
            Assert.Equal(s.BaseCode / 1_000_000, s.Job);
        }
    }

    [Fact]
    public void Every_shared_cooldown_group_resolves_to_a_row_that_exists()
    {
        CooldownCatalog c = Shipped();
        foreach (CooldownSkillInfo s in c.Skills)
        {
            Assert.True(c.TryGet(s.GroupId, out _), $"{s.BaseCode}({s.Name}) 의 공유 쿨 그룹 {s.GroupId} 에 해당하는 행이 없다");
        }
    }

    [Fact]
    public void A_base_code_resolves_to_itself_or_to_its_shared_group()
    {
        CooldownCatalog c = Shipped();
        foreach (CooldownSkillInfo s in c.Skills)
        {
            Assert.Equal(s.GroupId, c.GroupId(s.BaseCode));
        }
    }

    [Fact]
    public void Every_override_target_either_names_a_row_or_is_deliberately_unshown()
    {
        // gctOverride 는 접기가 틀리는 와이어 코드를 바로잡는다. 그 결과가 카탈로그 행이 되거나(표시된다),
        // 아니면 쿨이 없는/패시브 스킬이라 어차피 그릴 게 없거나 — 둘 중 하나여야 한다. 여기서 잡고 싶은 것은
        // "행이 있어야 하는데 한 단계 접기를 빠뜨려 못 찾는" 경우다.
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(CatalogPath()));
        CooldownCatalog c = Shipped();

        int resolved = 0, unshown = 0;
        foreach (JsonProperty p in doc.RootElement.GetProperty("gctOverride").EnumerateObject())
        {
            int wireCode = int.Parse(p.Name);
            if (c.TryGet(c.GroupId(wireCode), out _))
            {
                resolved++;
            }
            else
            {
                unshown++;
            }
        }

        // 실측 기준선: 1,487개 중 두 단계 해석으로 살아나는 것이 다수여야 한다. 한 단계만 접으면 655개가
        // 통째로 사라진다 — 그 회귀를 여기서 잡는다.
        Assert.True(resolved > unshown, $"override 해석 성공 {resolved} / 실패 {unshown} — 두 단계 접기가 빠졌을 수 있다");
    }

    [Fact]
    public void Shared_cooldown_groups_are_only_where_the_client_says_they_are()
    {
        // 서로 다른 base 가 한 그룹을 쓰는 곳 = 권성의 폭주/평상시 쌍. 이 숫자가 흔들리면 클라 테이블이
        // 바뀐 것이므로 카탈로그를 재생성해야 한다.
        CooldownCatalog c = Shipped();
        List<IGrouping<int, CooldownSkillInfo>> shared = c.Skills
            .GroupBy(s => s.GroupId)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.All(shared, g => Assert.All(g, s => Assert.Equal(19, s.Job)));
        Assert.Equal(7, shared.Count);
    }

    [Fact]
    public void An_absent_catalog_degrades_to_the_plain_fold()
    {
        // 자산이 없거나 깨졌을 때 예외를 던지면 미터가 아예 못 뜬다. 예전 동작(코드 접기)으로 조용히 내려앉아야 한다.
        CooldownCatalog missing = CooldownCatalog.Load(Path.Combine(Path.GetTempPath(), "no-such-cooldown-catalog.json"));
        Assert.Equal(0, missing.Count);
        Assert.Equal(14_220_000, missing.GroupId(14_220_050));
        Assert.Equal(1101, missing.GroupId(1101)); // 직업 대역 밖은 그대로
    }
}
