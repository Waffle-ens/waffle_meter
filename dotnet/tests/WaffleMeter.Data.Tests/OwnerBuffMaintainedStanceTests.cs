using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// The combat-assist overlay's handling of 폭주 (권성): a maintained stance broadcast with no expiry
/// (duration 0xFFFFFFFF). The parser stamps a short synthetic duration; the live overlay must NOT
/// false-expire the slot on an ordinary held re-broadcast gap, and must flag it Indefinite so the UI
/// draws no countdown and the voice alert never pre-warns its (guessed) end.
/// </summary>
public sealed class OwnerBuffMaintainedStanceTests
{
    private const int PokjuRuntimeCode = 191300401; // 폭주 variant; base 19130000
    private const int NormalBuffCode = 118000071;   // an ordinary job buff (real duration)

    private static DataManager WithExecutor(int uid)
    {
        var dm = new DataManager();
        dm.SaveNickname(uid, "권성", isExecutor: true, server: 3, jobByte: 0);
        return dm;
    }

    [Fact]
    public void Maintained_stance_survives_past_its_synthetic_duration()
    {
        long start = 1_000_000;
        DataManager dm = WithExecutor(7);
        // Parser stamps a 6s synthetic duration for the 0xFFFFFFFF stance.
        dm.SaveUseBuff(7, PokjuRuntimeCode, start, start + 6000, 6000, actorId: 7);

        // 12s later — well past the 6s synthetic duration — the stance is still shown (keep-alive), whereas
        // a real 6s buff would already be gone. This is the "폭주가 유지되는데 꺼졌다고 뜬다" fix.
        OwnerBuffView b = Assert.Single(dm.ActiveOwnerBuffs(start + 12_000));
        Assert.True(b.Indefinite);
    }

    [Fact]
    public void A_normal_buff_is_not_indefinite_and_expires_on_time()
    {
        long start = 1_000_000;
        DataManager dm = WithExecutor(7);
        dm.SaveUseBuff(7, NormalBuffCode, start, start + 6000, 6000, actorId: 7);

        OwnerBuffView active = Assert.Single(dm.ActiveOwnerBuffs(start + 3_000));
        Assert.False(active.Indefinite);

        Assert.Empty(dm.ActiveOwnerBuffs(start + 6_001)); // gone right after its real duration
    }
}
