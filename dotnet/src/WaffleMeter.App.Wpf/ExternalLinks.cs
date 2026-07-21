namespace WaffleMeter.App.Wpf;

/// <summary>바깥으로 나가는 링크 모음. 설정창 하단 버튼 줄과 후원 안내창이 함께 쓴다.
/// 통계 웹 주소는 여기에 두지 않는다 — <see cref="WaffleMeter.Stats.StatsApiClient"/>가 이미 갖고 있고,
/// 두 곳에 적어두면 도메인이 바뀔 때 한쪽만 고쳐진다.</summary>
public static class ExternalLinks
{
    /// <summary>버그 제보·문의를 받는 공식 디스코드.</summary>
    public const string Discord = "https://discord.gg/Wdzn7TegzM";

    /// <summary>후원(투네이션). 계좌를 직접 노출하지 않고 이 플랫폼 링크만 연다.</summary>
    public const string Donate = "https://toon.at/donate/waffle";
}
