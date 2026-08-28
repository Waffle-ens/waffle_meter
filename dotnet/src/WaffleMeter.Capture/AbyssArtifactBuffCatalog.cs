namespace WaffleMeter.Capture;

/// <summary>
/// The 아티팩트 점령 abnormal the server puts on a character while it is in the abyss — <c>12000261~12000266</c>,
/// named in the shipped <c>buff.json</c> as 에레슈란타 하층/중층 아티팩트 I·II·III with the effect line
/// "하층 아티팩트 N개 점령 효과" and an 어비스 포인트 획득 증가 of 2.5% per artifact.
///
/// <para><b>What it is for here.</b> <see cref="AbyssArtifactParser"/> says which SIDE holds each artifact, but
/// the side index is a slot inside the current server matchup and flips between cycles — measured on one
/// character, one server: slot 1 on 2026-08-23, slot 2 on 2026-08-28. This abnormal is the missing half: it
/// states how many artifacts OUR side holds in a zone, so the slot whose count matches is ours. The two zones
/// have to agree on one slot, which is a free consistency check on both readings.</para>
///
/// <para><b>Why a dedicated intercept rather than the buff store.</b> <c>ParseBuffPacket</c> drops any code
/// outside 110000000~199999999 and 20000000~29999999 (job buffs and the general band), and 12000262 is below
/// both — so these never reached the buff repository and never will. This is the same shape
/// <see cref="TrialAffixCatalog"/> uses for the 시련 어픽스 abnormals, and for the same reason: a code that IS
/// the value needs no decoding, only recognising.</para>
///
/// <para><b>Measured arrival.</b> 2026-08-28 capture: the character loaded 어비스 하층 at +5.64 s and both
/// abnormals were applied 0.30 s later by 0x382A targeting the own uid, then re-applied on the 중층 load. They
/// also ride the own-load 0x3633 buff list. That 0.3 s is why the panel can answer the moment the player walks
/// in, which is what the game itself started doing in the patch that prompted this.</para>
/// </summary>
public static class AbyssArtifactBuffCatalog
{
    /// <summary>하층 (AR1) 1개 점령. The next two codes are 2개 and 3개.</summary>
    public const int LowerFirstCode = 12_000_261;

    /// <summary>중층 (AR3) 1개 점령. The next two codes are 2개 and 3개.</summary>
    public const int MiddleFirstCode = 12_000_264;

    /// <summary>Zone key for 하층 — the zone's first artifact id, matching <see cref="AbyssArtifactZone.ZoneId"/>.</summary>
    public const int LowerZoneId = 1001;

    /// <summary>Zone key for 중층.</summary>
    public const int MiddleZoneId = 2001;

    /// <summary>Whether <paramref name="skillCode"/> is one of the six 아티팩트 점령 abnormals, and if so which
    /// zone it reports and how many artifacts that zone's holder has.</summary>
    /// <param name="count">1, 2 or 3 — never 0. A side holding none simply gets no abnormal, which is why the
    /// caller has to treat "seen a full buff list with none of these" as the zero rather than as silence.</param>
    public static bool TryResolve(long skillCode, out int zoneId, out int count)
    {
        if (skillCode >= LowerFirstCode && skillCode < LowerFirstCode + 3)
        {
            zoneId = LowerZoneId;
            count = (int)(skillCode - LowerFirstCode) + 1;
            return true;
        }

        if (skillCode >= MiddleFirstCode && skillCode < MiddleFirstCode + 3)
        {
            zoneId = MiddleZoneId;
            count = (int)(skillCode - MiddleFirstCode) + 1;
            return true;
        }

        zoneId = 0;
        count = 0;
        return false;
    }
}
