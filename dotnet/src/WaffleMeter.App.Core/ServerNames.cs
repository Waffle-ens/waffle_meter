namespace WaffleMeter.App.Core;

/// <summary>
/// Server id → name, ported from React utils/parser.ts SERVER_NAMES. <see cref="GetServerLabel"/>
/// returns the first two characters (the join panel shows "nickname[label]"), except where
/// <see cref="LabelOverrides"/> says otherwise.
/// </summary>
public static class ServerNames
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [1001] = "시엘", [1002] = "네자칸", [1003] = "바이젤", [1004] = "카이시넬", [1005] = "유스티엘",
        [1006] = "아리엘", [1007] = "프레기온", [1008] = "메스람타에다", [1009] = "히타니에", [1010] = "나니아",
        [1011] = "타하바타", [1012] = "루터스", [1013] = "페르노스", [1014] = "다미누", [1015] = "카사카",
        [1016] = "바카르마", [1017] = "챈가룽", [1018] = "코치룽", [1019] = "이슈타르", [1020] = "티아마트",
        [1021] = "포에타",
        [2001] = "이스라펠", [2002] = "지켈", [2003] = "트리니엘", [2004] = "루미엘", [2005] = "마르쿠탄",
        [2006] = "아스펠", [2007] = "에레슈키갈", [2008] = "브리트라", [2009] = "네몬", [2010] = "하달",
        [2011] = "루드라", [2012] = "울고른", [2013] = "무닌", [2014] = "오다르", [2015] = "젠카카",
        [2016] = "크로메데", [2017] = "콰이링", [2018] = "바바룽", [2019] = "파프니르", [2020] = "인드나흐",
        [2021] = "이스할겐",
    };

    /// <summary>
    /// Abbreviations that are not just the first two characters. 이스라펠(2001)과 이스할겐(2021)은
    /// 앞 두 글자가 똑같아 라벨이 겹쳤고, 2026-08-12 라이브 패치가 이걸 이스/할겐으로 갈랐다.
    /// ⚠️ 클라이언트의 <c>ServerName_&lt;id&gt;_short_desc</c>를 정본 삼아 재생성하지 마라 —
    /// 2026-08-05 빌드에서도 두 서버가 여전히 둘 다 "이스"라 충돌이 남아 있다(라이브가 앞서 있다).
    /// </summary>
    private static readonly Dictionary<int, string> LabelOverrides = new()
    {
        [2021] = "할겐",
    };

    /// <summary>
    /// The server's abbreviation: an entry from <see cref="LabelOverrides"/> when one exists, else the
    /// first two chars of the name, else "" if unknown (React getServerLabel).
    /// </summary>
    public static string GetServerLabel(int server)
    {
        if (server <= 0)
        {
            return string.Empty;
        }

        if (LabelOverrides.TryGetValue(server, out string? label))
        {
            return label;
        }

        if (!Names.TryGetValue(server, out string? name) || string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        return name.Length <= 2 ? name : name[..2];
    }

    /// <summary>Every known server id — lets tests assert that no two servers share a label.</summary>
    internal static IEnumerable<int> KnownServerIds => Names.Keys;
}
