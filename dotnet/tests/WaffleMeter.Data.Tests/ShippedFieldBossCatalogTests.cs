using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Ties <see cref="FieldBossCatalog"/>'s hand-written names back to the datamined asset its own doc comment
/// names as their source (<c>Assets/json/mobs.json</c>). Nothing joined the two before, so when the 2026-09-01
/// datamine pass corrected four names in mobs.json the catalogue kept the old ones and the two halves of the
/// app said different things about the same boss — the respawn alarm one name, the battle title another —
/// while all 1,655 tests stayed green.
///
/// <para>The names are not cosmetic here: the alarm's spoken line is <c>"{name}, {lead}분 뒤 리젠"</c> and the
/// voice packs address clips by a hash of that string, so a drifted name is also a silently orphaned clip
/// (<c>ShippedVoicePackTests</c> catches that half, but only once the catalogue has already moved).</para>
///
/// <para>Read out of the source text rather than through <see cref="FieldBossCatalog.All"/> so the failure
/// message can name the offending line, and for the same reason <c>ShippedVoicePackTests.FieldBossNames</c>
/// parses it: the table is a static initialiser whose literals are the thing under test.</para>
/// </summary>
public sealed class ShippedFieldBossCatalogTests
{
    private static DirectoryInfo RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "dotnet", "Assets", "json", "mobs.json")))
        {
            dir = dir.Parent;
        }

        return dir ?? throw new DirectoryNotFoundException(
            "dotnet/Assets/json/mobs.json not found above " + AppContext.BaseDirectory);
    }

    private static string RepoPath(params string[] parts) => Path.Combine(RepoRoot().FullName, Path.Combine(parts));

    private static Dictionary<int, Mob> ShippedMobs() =>
        ReferenceJson.LoadMobs(RepoPath("dotnet", "Assets", "json", "mobs.json"));

    /// <summary>(code, name) for every row of the catalogue's table, in source order.</summary>
    private static (int Code, string Name)[] CatalogueRows()
    {
        string src = File.ReadAllText(RepoPath("dotnet", "src", "WaffleMeter.Capture", "FieldBossCatalog.cs"));
        string body = src[src.IndexOf("Bosses =", StringComparison.Ordinal)
                          ..src.IndexOf("public static bool HasOwnAlarm", StringComparison.Ordinal)];

        return Regex.Matches(body, @"new\(\s*(\d+),\s*""([^""]+)""")
            .Select(m => (Code: int.Parse(m.Groups[1].Value), Name: m.Groups[2].Value))
            .ToArray();
    }

    [Fact]
    public void The_source_table_parses_into_the_same_rows_the_catalogue_serves()
    {
        (int Code, string Name)[] rows = CatalogueRows();

        Assert.Equal(FieldBossCatalog.All().Count, rows.Length);
        Assert.Equal(
            FieldBossCatalog.All().Select(b => (b.Code, b.Name)).OrderBy(x => x.Code).ToArray(),
            rows.OrderBy(x => x.Code).ToArray());
    }

    [Fact]
    public void Every_field_boss_is_a_mob_the_meter_knows()
    {
        Dictionary<int, Mob> mobs = ShippedMobs();

        string[] unknown = CatalogueRows()
            .Where(r => !mobs.ContainsKey(r.Code))
            .Select(r => $"{r.Code} \"{r.Name}\"")
            .ToArray();

        Assert.True(unknown.Length == 0,
            $"mobs.json 에 없는 필드보스 코드 {unknown.Length}건 — 타이머는 뜨는데 전투는 시작되지 않습니다.\n  "
            + string.Join("\n  ", unknown.Take(10)));
    }

    /// <summary>A field boss that mobs.json does not flag as a boss cannot open a battle window, so the alarm
    /// would announce a fight the meter then refuses to record.</summary>
    [Fact]
    public void Every_field_boss_is_flagged_as_a_boss_in_mobs_json()
    {
        Dictionary<int, Mob> mobs = ShippedMobs();

        string[] notBoss = CatalogueRows()
            .Where(r => mobs.TryGetValue(r.Code, out Mob? m) && !m.Boss)
            .Select(r => $"{r.Code} \"{r.Name}\"")
            .ToArray();

        Assert.True(notBoss.Length == 0,
            $"mobs.json 이 보스로 표시하지 않는 필드보스 {notBoss.Length}건.\n  " + string.Join("\n  ", notBoss.Take(10)));
    }

    /// <summary>The drift this file exists for. Both names are user-facing — mobs.json titles the battle, the
    /// catalogue speaks the alarm — so they have to be the same string, and mobs.json is the datamined one.</summary>
    [Fact]
    public void Every_field_boss_name_matches_mobs_json()
    {
        Dictionary<int, Mob> mobs = ShippedMobs();

        string[] drifted = CatalogueRows()
            .Where(r => mobs.TryGetValue(r.Code, out Mob? m) && !string.Equals(m.Name, r.Name, StringComparison.Ordinal))
            .Select(r => $"{r.Code}: FieldBossCatalog \"{r.Name}\" vs mobs.json \"{mobs[r.Code].Name}\"")
            .ToArray();

        Assert.True(drifted.Length == 0,
            $"필드보스 이름이 mobs.json 과 어긋납니다 {drifted.Length}건 — 알람 문구와 전투 제목이 서로 다른 이름을 말하게 됩니다."
            + "\n  이름을 바꾸면 음성 클립의 해시도 바뀌니 unbaked.txt 갱신 + 재굽기까지 함께 하세요.\n  "
            + string.Join("\n  ", drifted.Take(10)));
    }

    /// <summary>Two rows sharing a name is legitimate (수호신장 나흐마 guards four codes), but two rows sharing a
    /// CODE means one of them is unreachable — the lookup keys by code.</summary>
    [Fact]
    public void No_code_appears_twice()
    {
        string[] dupes = CatalogueRows()
            .GroupBy(r => r.Code)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ({string.Join(" / ", g.Select(x => x.Name))})")
            .ToArray();

        Assert.True(dupes.Length == 0, $"중복 코드 {dupes.Length}건: {string.Join(", ", dupes)}");
    }
}
