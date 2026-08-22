namespace WaffleMeter.App.Wpf;

/// <summary>바깥으로 나가는 링크 모음. 설정창 하단 버튼 줄과 후원 안내창이 함께 쓴다.
/// 통계 웹 주소는 여기에 두지 않는다 — <see cref="WaffleMeter.Stats.StatsApiClient"/>가 이미 갖고 있고,
/// 두 곳에 적어두면 도메인이 바뀔 때 한쪽만 고쳐진다.</summary>
public static class ExternalLinks
{
    /// <summary>버그 제보·문의를 받는 공식 디스코드.</summary>
    public const string Discord = "https://discord.gg/Wdzn7TegzM";

    /// <summary>후원(카카오페이 송금). 계좌번호를 앱에 박아 두지 않고 이 링크만 연다 — 계좌가 바뀌어도
    /// 앱을 다시 배포할 필요가 없고, 배포본에서 계좌가 그대로 읽히지도 않는다.</summary>
    public const string Donate = "https://link.kakaopay.com/__/3yVE6Gy";
}
