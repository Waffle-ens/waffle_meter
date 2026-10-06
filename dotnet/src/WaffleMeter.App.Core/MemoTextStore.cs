using System.Text;

namespace WaffleMeter.App.Core;

/// <summary>
/// 메모 오버레이의 본문. <c>settings.properties</c> 가 아니라 앱 데이터 폴더의 UTF-8 파일 <c>memo.txt</c>
/// 하나에 둔다.
/// <para><b>왜 설정 파일이 아닌가 (실측).</b> 설정 값은 읽을 때마다 Latin-1 → EUC-KR 재디코드를 탄다
/// (<c>PropertyHandler.GetProperty</c>) — 한글이 한 글자도 없고 Latin-1 기호만 있는 값이 깨진다:
/// <c>'Boss · 3rd'</c> → <c>'Boss ?3rd'</c>, <c>'5×3'</c> → <c>'5?'</c>, <c>'25°C'</c> → <c>'25?'</c>.
/// 게다가 키 하나를 쓸 때마다 파일 전체를 다시 쓴다. 메모는 타자 칠 때마다 바뀌는 값이라 둘 다 맞지 않는다.</para>
/// <para>설정 코드(백업·공유)에도 실리지 않는다 — 남에게 붙여 넣는 문자열에 개인 메모가 따라가면 안 된다.</para>
/// <para>저장은 디바운스한다: <see cref="Update"/> 는 메모리만 바꾸고 더러움 표시를 세우며, 쓰는 쪽(창)이
/// <see cref="SaveDebounceMs"/> 만큼 조용해진 뒤 <see cref="Flush"/> 를 부른다. 종료·업데이트 재시작 경로에서도
/// <see cref="Flush"/> 를 불러야 마지막 몇 글자를 잃지 않는다. 스레드 안전하지 않다 — UI 스레드 전용.</para>
/// </summary>
public sealed class MemoTextStore
{
    public const string FileName = "memo.txt";

    /// <summary>마지막 입력 뒤 이만큼 조용하면 디스크에 쓴다.</summary>
    public const int SaveDebounceMs = 700;

    // BOM 없이 쓴다. 읽을 때는 BOM 이 있어도(메모장 등으로 손댄 파일) 걷어 낸다.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public MemoTextStore(string directory)
    {
        FilePath = Path.Combine(directory, FileName);
        Text = Read(FilePath);
    }

    /// <summary><c>&lt;앱 데이터&gt;\memo.txt</c>.</summary>
    public string FilePath { get; }

    /// <summary>지금 메모 본문(메모리). 파일이 없거나 못 읽었으면 빈 문자열.</summary>
    public string Text { get; private set; }

    /// <summary>메모리 값이 아직 파일에 안 쓰였는가.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>본문을 바꾼다(메모리만). 실제로 바뀌었으면 true — 호출자가 디바운스 타이머를 다시 건다.</summary>
    public bool Update(string? text)
    {
        text ??= string.Empty;
        if (string.Equals(text, Text, StringComparison.Ordinal))
        {
            return false;
        }

        Text = text;
        IsDirty = true;
        return true;
    }

    /// <summary>더러우면 파일에 쓴다. 쓸 게 없었거나 썼으면 true, 디스크가 거절했으면 false(더러움 유지 —
    /// 다음 Flush 가 다시 시도한다).</summary>
    public bool Flush()
    {
        if (!IsDirty)
        {
            return true;
        }

        if (!Write(FilePath, Text))
        {
            return false;
        }

        IsDirty = false;
        return true;
    }

    /// <summary>파일을 읽는다. 없거나 못 읽으면 빈 문자열 — 메모 하나 때문에 기동이 멈추면 안 된다.
    /// 줄바꿈은 손대지 않는다(CRLF 는 CRLF 로, LF 는 LF 로 돌아온다).</summary>
    public static string Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 원자적 교체: 임시 파일에 쓰고 바꿔 끼운다. 덮어쓰기 도중 프로세스가 죽어도(종료 직전 flush 가 정확히 그
    /// 순간이다) 반쯤 쓰인 메모가 남지 않는다. 임시 파일이 막히면(백신 검사 등) 제자리 쓰기로 물러선다 —
    /// <c>PropertyHandler.Save</c> 와 같은 순서.
    /// </summary>
    public static bool Write(string path, string text)
    {
        string temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, text, Utf8NoBom);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // best effort
            }

            try
            {
                File.WriteAllText(path, text, Utf8NoBom);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
