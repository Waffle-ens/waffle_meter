namespace WaffleMeter.App.Core;

/// <summary>
/// How a name should be READ, when that differs from how it is written.
///
/// <para>The synthesiser has no Korean pronunciation dictionary — it reads the glyphs. Two things follow.
/// Some names are simply read wrong: 별동대장 is pronounced [별똥대장] in Korean, but written plainly it comes
/// out as a flat "별-동대장". And some spacing that is correct on screen becomes an audible stumble, because a
/// space invites a prosodic break: "세 개의 뿔" is read with a gap after 세.</para>
///
/// <para>So the display name and the spoken name are separated. The overlay keeps the catalogue's spelling;
/// only the voice sees this. Since the voice packs are keyed on the spoken string, an entry added here
/// changes which clip is looked up — the pack has to be re-rendered for that line, or it falls through to the
/// online voice.</para>
///
/// <para>Additions belong here only when a real listener reported the line reading wrong. This is a
/// pronunciation fix, not a place to reword alerts.</para>
/// </summary>
public static class SpokenName
{
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        // 사이시옷: 별동대(隊)는 [별똥대]로 소리 난다. 표기대로 읽으면 "별-동대장"으로 끊겨 들린다.
        ["별동대장 링크스"] = "별똥대장 링크스",
        // 띄어쓰기가 '세' 뒤에 끊김을 만든다. 붙여 읽어야 한 덩어리로 들린다.
        ["세 개의 뿔 마이노"] = "세개의 뿔 마이노",
    };

    /// <summary>The reading for <paramref name="displayName"/>, or the name itself when it reads correctly.</summary>
    public static string Of(string displayName) =>
        Overrides.TryGetValue(displayName, out string? spoken) ? spoken : displayName;

    /// <summary>Every override, for the bake script's cross-check.</summary>
    public static IReadOnlyDictionary<string, string> All => Overrides;
}
