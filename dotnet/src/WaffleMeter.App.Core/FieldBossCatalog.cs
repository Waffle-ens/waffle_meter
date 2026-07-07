namespace WaffleMeter.App.Core;

/// <summary>
/// Field-boss code → display name, for the respawn-timer alerts. Codes and names were recovered from the
/// 0x9101 timer broadcast (RE) cross-checked with the client's boss data-mine; the ranges are the standard
/// AION field-boss code bands (elyos 21xxxxx / asmodian 24xxxxx). Unknown codes still get a timer — they
/// just render with a generic label.
/// </summary>
public static class FieldBossCatalog
{
    private static readonly Dictionary<int, string> Names = new()
    {
        // elyos
        [2101217] = "응집된 베레놈",
        [2101218] = "옛 두목 비고르",
        [2101257] = "꺾인 날개 츠바인",
        [2101278] = "탐욕의 이게티스",
        [2101279] = "생명의 신수 수페르비아",
        [2101306] = "썩은 뿌리 멜트림",
        [2101349] = "맹목적인 니호그",
        [2101350] = "최초의 실험체 크티마",
        [2101415] = "세 개의 뿔 마이노",
        [2101416] = "고통의 람푸스",
        [2101600] = "3부대장 카르코티",
        [2101601] = "부군단장 비바츠라",
        // asmodian
        [2406034] = "파르곤",
        [2406035] = "발라크",
        [2406071] = "레눌프",
        [2406093] = "하르칸",
        [2406094] = "글레이시아",
        [2406129] = "피오스",
        [2406131] = "바투",
        [2406132] = "브란트",
        [2406181] = "불라간",
        [2406182] = "그림니르",
        [2406990] = "3부대장 미나사라",
        [2406991] = "부군단장 사르바카",
    };

    /// <summary>Display name for a boss code, or a generic "필드보스" label with the code when unknown.</summary>
    public static string Name(int code) => Names.TryGetValue(code, out string? n) ? n : $"필드보스 {code}";

    /// <summary>True when the code is a known field boss (drives whether an unknown timer is surfaced).</summary>
    public static bool IsKnown(int code) => Names.ContainsKey(code);

    /// <summary>The realm a boss belongs to, from its code band (21xxxxx elyos / 24xxxxx asmodian).</summary>
    public static string Realm(int code) => code / 100000 == 21 ? "천족" : code / 100000 == 24 ? "마족" : "기타";

    /// <summary>Every known boss as (code, name, realm), in catalog order — for the boss-selection picker.</summary>
    public static IReadOnlyList<(int Code, string Name, string Realm)> All()
        => Names.Select(kv => (kv.Key, kv.Value, Realm(kv.Key))).ToList();
}
