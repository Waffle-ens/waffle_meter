using System.Text;
using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// 메모 본문 저장소. 이 파일이 따로 있는 이유가 "설정 파일의 EUC-KR 재디코드가 Latin-1 기호를 깬다"였으므로,
/// 바로 그 기호들과 한글·이모지·두 종류의 줄바꿈이 재시작을 건너 한 글자도 안 바뀌는지를 못박는다.
/// </summary>
public sealed class MemoTextStoreTests : IDisposable
{
    private readonly string _temp;

    public MemoTextStoreTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "wm_memo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    public static IEnumerable<object[]> Texts() => new[]
    {
        new object[] { "버프 순서: 사자 → 늑대 → 독수리" },
        new object[] { "Boss · 3rd" },               // 설정 파일에선 'Boss ?3rd' 로 깨졌다(실측)
        new object[] { "5×3" },                      // '5?'
        new object[] { "25°C" },                     // '25?'
        new object[] { "café" },
        new object[] { "파티 모집 🎯🔥 (2/8)" },      // 서로게이트 쌍
        new object[] { "첫 줄\r\n둘째 줄\r\n" },      // WPF TextBox(AcceptsReturn)가 넣는 CRLF
        new object[] { "first\nsecond\n\nfourth" },  // 손으로 고친 파일의 LF
        new object[] { "섞인\r\n줄\n바꿈\r" },
        new object[] { "  앞뒤 공백  \t" },
        new object[] { string.Empty },
    };

    [Theory]
    [MemberData(nameof(Texts))]
    public void Text_survives_a_restart_byte_for_byte(string text)
    {
        var store = new MemoTextStore(_temp);
        store.Update(text);
        Assert.True(store.Flush());

        Assert.Equal(text, new MemoTextStore(_temp).Text);
    }

    [Fact]
    public void Missing_file_reads_as_empty()
    {
        var store = new MemoTextStore(_temp);
        Assert.Equal(string.Empty, store.Text);
        Assert.False(store.IsDirty);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Lives_next_to_the_settings_as_memo_txt_in_utf8_without_bom()
    {
        var store = new MemoTextStore(_temp);
        store.Update("한글");
        store.Flush();

        Assert.Equal(Path.Combine(_temp, "memo.txt"), store.FilePath);
        byte[] bytes = File.ReadAllBytes(store.FilePath);
        Assert.Equal(Encoding.UTF8.GetBytes("한글"), bytes);
        Assert.False(File.Exists(store.FilePath + ".tmp")); // 원자 쓰기의 임시 파일이 남지 않는다
    }

    [Fact]
    public void A_file_saved_with_a_bom_by_another_editor_reads_clean()
    {
        File.WriteAllText(Path.Combine(_temp, MemoTextStore.FileName), "메모장으로 고침", new UTF8Encoding(true));
        Assert.Equal("메모장으로 고침", new MemoTextStore(_temp).Text);
    }

    [Fact]
    public void Update_only_marks_dirty_and_flush_writes_once()
    {
        var store = new MemoTextStore(_temp);
        Assert.True(store.Update("a"));
        Assert.True(store.IsDirty);
        Assert.False(File.Exists(store.FilePath)); // 디바운스: Update 는 디스크를 건드리지 않는다

        Assert.True(store.Flush());
        Assert.False(store.IsDirty);
        Assert.True(File.Exists(store.FilePath));

        // 같은 값은 변경이 아니다 — 디바운스 타이머를 다시 걸 이유도, 다시 쓸 이유도 없다.
        Assert.False(store.Update("a"));
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void Null_is_treated_as_empty()
    {
        var store = new MemoTextStore(_temp);
        store.Update("x");
        store.Update(null);
        store.Flush();
        Assert.Equal(string.Empty, new MemoTextStore(_temp).Text);
    }

    [Fact]
    public void Write_creates_the_directory_if_it_is_gone()
    {
        string dir = Path.Combine(_temp, "nested", "dir");
        var store = new MemoTextStore(dir);
        store.Update("살아남는다");
        Assert.True(store.Flush());
        Assert.Equal("살아남는다", new MemoTextStore(dir).Text);
    }

    [Fact]
    public void Overwrites_the_previous_memo()
    {
        var store = new MemoTextStore(_temp);
        store.Update("길고 긴 첫 번째 메모 내용");
        store.Flush();
        store.Update("짧음");
        store.Flush();

        Assert.Equal("짧음", new MemoTextStore(_temp).Text);
    }

    [Fact]
    public void A_missing_file_is_not_a_read_failure_and_leaves_no_side_file()
    {
        var store = new MemoTextStore(_temp);
        Assert.False(store.ReadFailed);
        store.Update("새 메모");
        Assert.True(store.Flush());

        Assert.Equal(new[] { store.FilePath }, Directory.GetFiles(_temp));
    }

    [Fact]
    public void A_memo_locked_at_startup_is_set_aside_not_overwritten_by_the_first_save()
    {
        // 기동 순간 백신·동기화 클라이언트가 memo.txt 를 잡고 있었다 → 빈 메모로 보인다. 사용자가 그걸 보고 새로
        // 적어 저장해도 못 본 원본은 살아 있어야 한다(전에는 첫 Flush 가 그대로 덮어써 원본이 영구 소실됐다).
        string path = Path.Combine(_temp, MemoTextStore.FileName);
        File.WriteAllText(path, "중요한 기존 메모 · 5×3", new UTF8Encoding(false));

        MemoTextStore store;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store = new MemoTextStore(_temp);
            Assert.True(store.ReadFailed);
            Assert.Equal(string.Empty, store.Text);
            Assert.False(store.IsDirty);

            // 아직 잠겨 있는 동안의 저장은 원본을 건드리지 못하고 실패한다 — 더러움이 남아 다음에 다시 시도한다.
            store.Update("새로 친 한 줄");
            Assert.False(store.Flush());
            Assert.True(store.IsDirty);
            Assert.True(store.ReadFailed);
        }

        Assert.True(store.Flush());
        Assert.False(store.ReadFailed);
        Assert.Equal("새로 친 한 줄", new MemoTextStore(_temp).Text);

        string[] setAside = Directory.GetFiles(_temp, MemoTextStore.FileName + MemoTextStore.UnreadSuffix + "*");
        Assert.Single(setAside);
        Assert.Equal("중요한 기존 메모 · 5×3", File.ReadAllText(setAside[0], Encoding.UTF8));

        // 치운 뒤의 memo.txt 는 이 세션이 쓴 파일이다 — 이후 저장은 평범한 덮어쓰기이고 더 치우지 않는다.
        store.Update("두 번째 저장");
        Assert.True(store.Flush());
        Assert.Single(Directory.GetFiles(_temp, MemoTextStore.FileName + MemoTextStore.UnreadSuffix + "*"));
        Assert.Equal("두 번째 저장", new MemoTextStore(_temp).Text);
    }

    [Fact]
    public void A_read_failure_without_an_edit_never_touches_the_original()
    {
        string path = Path.Combine(_temp, MemoTextStore.FileName);
        File.WriteAllText(path, "그대로 둔다", new UTF8Encoding(false));

        MemoTextStore store;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store = new MemoTextStore(_temp);
        }

        // 종료 경로의 Flush — 쓸 게 없으니 원본도 그대로, 옆 파일도 없다.
        Assert.True(store.Flush());
        Assert.Equal("그대로 둔다", File.ReadAllText(path, Encoding.UTF8));
        Assert.Equal(new[] { path }, Directory.GetFiles(_temp));
    }

    [Fact]
    public void An_unread_original_that_vanished_meanwhile_needs_no_side_file()
    {
        string path = Path.Combine(_temp, MemoTextStore.FileName);
        File.WriteAllText(path, "곧 사라짐", new UTF8Encoding(false));

        MemoTextStore store;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store = new MemoTextStore(_temp);
        }

        File.Delete(path);
        store.Update("새 메모");
        Assert.True(store.Flush());
        Assert.Equal(new[] { path }, Directory.GetFiles(_temp));
        Assert.Equal("새 메모", new MemoTextStore(_temp).Text);
    }
}
