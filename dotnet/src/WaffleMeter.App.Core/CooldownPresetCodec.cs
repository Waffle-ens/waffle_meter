using System.Text;
using System.Text.Json;

namespace WaffleMeter.App.Core;

/// <summary>
/// 쿨타임 프리셋 슬롯을 Base64(UTF-8(JSON)) 한 값으로 인코딩한다. <see cref="BuffPresetCodec"/> 와 같은
/// 방식이고 이유도 같다: 슬롯 이름은 사용자가 한글로 적는데 <c>PropertyHandler.GetProperty</c> 가 모든 값을
/// Latin-1 → EUC-KR 로 재디코드해 비-Latin-1 문자를 전부 '?' 로 바꾼다. Base64 출력은 순수 ASCII 라 그
/// 경로를 그대로 통과한다.
/// </summary>
public static class CooldownPresetCodec
{
    public static string Encode(CooldownPresetSet set)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(set)));

    /// <summary>저장된 값을 디코드한다. 없거나 깨졌거나 구조가 안 맞으면 null — 호출자가 재시드하며, 설정
    /// 로드 경로 밖으로 예외를 던지지 않는다(설정 파일 손상이 앱 기동을 인질로 잡으면 안 된다).</summary>
    public static CooldownPresetSet? Decode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(raw);
            return JsonSerializer.Deserialize<CooldownPresetSet>(Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            return null; // 깨졌거나 손으로 고친 값 — 던지지 말고 재시드한다
        }
    }
}
