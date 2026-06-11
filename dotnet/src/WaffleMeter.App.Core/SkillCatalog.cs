namespace WaffleMeter.App.Core;

/// <summary>One tracked skill (port of codes.ts SkillMeta).</summary>
public sealed record SkillMeta(int Code, string Job, string Name, bool IsStigma);

/// <summary>Per-job grouping for the skill-settings flyout (port of GroupedJobSkills).</summary>
public sealed record GroupedJobSkills(string Job, IReadOnlyList<int> NormalSkills, IReadOnlyList<int> StigmaSkills);

/// <summary>
/// Port of React constants/codes.ts: the tracked-skill catalog used by the join panel skill badges +
/// settings. SKILLS keeps source order (drives the badge sort); the maps/helpers mirror SKILL_MAP,
/// SKILL_ORDER_MAP, DEFAULT_VISIBLE_SKILL_CODES, normalizeTrackedSkillCode, GROUPED_BY_JOB.
/// </summary>
public static class SkillCatalog
{
    public static readonly IReadOnlyDictionary<string, int> JobPrefix = new Dictionary<string, int>
    {
        ["검성"] = 11, ["수호성"] = 12, ["살성"] = 13, ["궁성"] = 14,
        ["마도성"] = 15, ["정령성"] = 16, ["치유성"] = 17, ["호법성"] = 18,
    };

    public static readonly IReadOnlyList<SkillMeta> Skills = new SkillMeta[]
    {
        new(11800000, "검성", "살기 파열", false),
        new(11750000, "검성", "공격 준비", false),
        new(11780000, "검성", "노련한 반격", false),
        new(11170000, "검성", "내려찍기", false),
        new(11010000, "검성", "절단의 맹타", false),
        new(11240000, "검성", "분노의 파동", true),
        new(11400000, "검성", "돌격 자세", true),
        new(11250000, "검성", "지켈의 축복", true),
        new(11110000, "검성", "집중 막기", true),
        new(11130000, "검성", "균형의 갑옷", true),
        new(11080000, "검성", "칼날 날리기", true),
        new(11380000, "검성", "근성", true),
        new(11340000, "검성", "흡혈의 검", true),
        new(11390000, "검성", "격노 폭발", true),
        new(11410000, "검성", "파동의 갑주", true),
        new(11430000, "검성", "강제 결박", true),
        new(11700000, "검성", "강습 일격", true),
        new(11450000, "검성", "분쇄 돌진", true),
        new(12780000, "수호성", "격앙", false),
        new(12240000, "수호성", "심판", false),
        new(12010000, "수호성", "맹렬한 일격", false),
        new(12040000, "수호성", "연속 난타", false),
        new(12350000, "수호성", "비호의 일격", false),
        new(12310000, "수호성", "주신의 징벌", true),
        new(12320000, "수호성", "네자칸의 방패", true),
        new(12110000, "수호성", "보호의 방패", true),
        new(12120000, "수호성", "도발", true),
        new(12200000, "수호성", "균형의 갑옷", true),
        new(12190000, "수호성", "이중 갑옷", true),
        new(12070000, "수호성", "파멸의 방패", true),
        new(12230000, "수호성", "고결의 갑주", true),
        new(12410000, "수호성", "처형의 검", true),
        new(12250000, "수호성", "전우 보호", true),
        new(12220000, "수호성", "나포", true),
        new(12700000, "수호성", "강습 맹격", true),
        new(12450000, "수호성", "전장의 깃발", true),
        new(13740000, "살성", "배후 강타", false),
        new(13750000, "살성", "강습 자세", false),
        new(13720000, "살성", "빈틈 노리기", false),
        new(13350000, "살성", "심장 찌르기", false),
        new(13130000, "살성", "문양 폭발", false),
        new(13010000, "살성", "빠른 베기", false),
        new(13270000, "살성", "맹수의 송곳니", true),
        new(13390000, "살성", "신속의 계약", true),
        new(13250000, "살성", "연막탄", true),
        new(13080000, "살성", "회피 자세", true),
        new(13280000, "살성", "나선 베기", true),
        new(13180000, "살성", "그림자 보행", true),
        new(13020000, "살성", "암검 투척", true),
        new(13300000, "살성", "트리니엘의 비수", true),
        new(13230000, "살성", "공중 포박", true),
        new(13310000, "살성", "환영 분신", true),
        new(13370000, "살성", "회피의 계약", true),
        new(13700000, "살성", "강습 습격", true),
        new(13140000, "살성", "암영보", true),
        new(14740000, "궁성", "집중의 눈", false),
        new(14750000, "궁성", "사냥꾼의 결의", false),
        new(14020000, "궁성", "저격", false),
        new(14340000, "궁성", "속사", false),
        new(14010000, "궁성", "조준 화살", false),
        new(14050000, "궁성", "송곳 화살", false),
        new(14270000, "궁성", "화살 폭풍", true),
        new(14310000, "궁성", "바이젤의 권능", true),
        new(14220000, "궁성", "축복의 활", true),
        new(14120000, "궁성", "기습 차기", true),
        new(14180000, "궁성", "결박의 덫", true),
        new(14150000, "궁성", "환영 화살", true),
        new(14190000, "궁성", "은신", true),
        new(14160000, "궁성", "봉인 화살", true),
        new(14350000, "궁성", "대자연의 숨결", true),
        new(14060000, "궁성", "그리폰 화살", true),
        new(14360000, "궁성", "폭발 화살", true),
        new(14700000, "궁성", "강습 강타", true),
        new(14380000, "궁성", "지원 사격", true),
        new(15740000, "마도성", "불꽃의 로브", false),
        new(15210000, "마도성", "불꽃 화살", false),
        new(15040000, "마도성", "불꽃 작살", false),
        new(15050000, "마도성", "불꽃 폭발", false),
        new(15310000, "마도성", "집중의 기원", false),
        new(15060000, "마도성", "지옥의 화염", false),
        new(15280000, "마도성", "혹한의 바람", false),
        new(15360000, "마도성", "신성 폭발", true),
        new(15160000, "마도성", "강철 보호막", true),
        new(15400000, "마도성", "원소 강화", true),
        new(15140000, "마도성", "저주: 나무", true),
        new(15230000, "마도성", "빙설의 갑주", true),
        new(15130000, "마도성", "영혼 동결", true),
        new(15200000, "마도성", "냉기 폭풍", true),
        new(15390000, "마도성", "불의 장벽", true),
        new(15300000, "마도성", "루미엘의 공간", true),
        new(15320000, "마도성", "지연 폭발", true),
        new(15120000, "마도성", "빙하 강타", true),
        new(15700000, "마도성", "강습 폭격", true),
        new(15410000, "마도성", "동면", true),
        new(16710000, "정령성", "정령 타격", false),
        new(16010000, "정령성", "냉기 충격", false),
        new(16040000, "정령성", "화염 전소", false),
        new(16300000, "정령성", "원소 융합", false),
        new(16240000, "정령성", "협공: 파멸의 공세", true),
        new(16190000, "정령성", "강화: 정령의 가호", true),
        new(16370000, "정령성", "불길의 축복", true),
        new(16250000, "정령성", "소환: 고대의 정령", true),
        new(16150000, "정령성", "협공: 부식", true),
        new(16360000, "정령성", "카이시넬의 권능", true),
        new(16060000, "정령성", "흡인", true),
        new(16080000, "정령성", "공포의 절규", true),
        new(16220000, "정령성", "저주의 구름", true),
        new(16230000, "정령성", "마법 강탈", true),
        new(16260000, "정령성", "마법 차단", true),
        new(16700000, "정령성", "강습 공포", true),
        new(16170000, "정령성", "명령: 대역", true),
        new(17780000, "치유성", "대지의 은총", false),
        new(17730000, "치유성", "주신의 은총", false),
        new(17120000, "치유성", "쾌유의 광휘", false),
        new(17350000, "치유성", "단죄", false),
        new(17060000, "치유성", "벽력", false),
        new(17010000, "치유성", "대지의 응보", false),
        new(17280000, "치유성", "권능 폭발", true),
        new(17290000, "치유성", "면죄", true),
        new(17160000, "치유성", "생명의 권능", true),
        new(17430000, "치유성", "증폭의 기도", true),
        new(17390000, "치유성", "소환 부활", true),
        new(17400000, "치유성", "대지의 징벌", true),
        new(17270000, "치유성", "구원", true),
        new(17190000, "치유성", "속박", true),
        new(17410000, "치유성", "보호의 빛", true),
        new(17420000, "치유성", "유스티엘의 권능", true),
        new(17300000, "치유성", "파멸의 목소리", true),
        new(17700000, "치유성", "강습 낙인", true),
        new(17440000, "치유성", "고결한 기운", true),
        new(18780000, "호법성", "대지의 약속", false),
        new(18750000, "호법성", "공격 준비", false),
        new(18120000, "호법성", "쾌유의 주문", false),
        new(18100000, "호법성", "암격쇄", false),
        new(18010000, "호법성", "격파쇄", false),
        new(18220000, "호법성", "멸화", true),
        new(18190000, "호법성", "불패의 진언", true),
        new(18140000, "호법성", "집중 방어", true),
        new(18160000, "호법성", "질주의 진언", true),
        new(18130000, "호법성", "분쇄격", true),
        new(18330000, "호법성", "마르쿠탄의 분노", true),
        new(18240000, "호법성", "차단의 권능", true),
        new(18230000, "호법성", "결박의 낙인", true),
        new(18170000, "호법성", "쾌유의 손길", true),
        new(18250000, "호법성", "질풍의 권능", true),
        new(18420000, "호법성", "수호의 축복", true),
        new(18700000, "호법성", "강습 충격", true),
        new(18440000, "호법성", "결계의 주문", true),
    };

    private static readonly Dictionary<int, SkillMeta> Map = Skills.ToDictionary(s => s.Code);
    private static readonly Dictionary<int, int> OrderMap =
        Skills.Select((s, i) => (s.Code, i)).ToDictionary(t => t.Code, t => t.i);

    /// <summary>All tracked skill codes (default = everything visible).</summary>
    public static readonly IReadOnlyList<int> DefaultVisibleCodes = Skills.Select(s => s.Code).ToList();

    public static readonly IReadOnlyList<GroupedJobSkills> GroupedByJob = JobPrefix.Keys
        .Select(job => new GroupedJobSkills(
            job,
            Skills.Where(s => s.Job == job && !s.IsStigma).Select(s => s.Code).ToList(),
            Skills.Where(s => s.Job == job && s.IsStigma).Select(s => s.Code).ToList()))
        .ToList();

    public static SkillMeta? Get(int code) => Map.GetValueOrDefault(code);

    public static string? GetName(int code) => Map.TryGetValue(code, out SkillMeta? m) ? m.Name : null;

    public static int Order(int code) => OrderMap.TryGetValue(code, out int i) ? i : 999;

    /// <summary>Port of normalizeTrackedSkillCode: exact match, else the floor base code, else self.</summary>
    public static int Normalize(int code)
    {
        if (Map.ContainsKey(code))
        {
            return code;
        }

        int baseCode = code / 10000 * 10000;
        return Map.ContainsKey(baseCode) ? baseCode : code;
    }
}
