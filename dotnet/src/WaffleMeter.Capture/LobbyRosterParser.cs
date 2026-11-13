using System.Text;

namespace WaffleMeter.Capture;

/// <summary>Outcome of reading a character-select roster: the account's characters on the server it logged into.
/// <paramref name="Nicknames"/> leaves out the reserved slot every account carries (a <c>$</c>-prefixed random
/// name that is not a character); <paramref name="Records"/> counts it.</summary>
public readonly record struct LobbyRosterParse(bool Ok, IReadOnlyList<string> Nicknames, int Records)
{
    public static readonly LobbyRosterParse None = new(false, [], 0);
}

/// <summary>
/// Reads the character list the game server sends for character select — <c>Login_RS._characters</c> (0x3615, on
/// every fresh login) and <c>Lobby_RS._characters</c> (0x3626, on an in-game 캐릭터 변경). Both are
/// <c>Array&lt;LobbyCharDesc&gt;</c>, and that list IS the account's roster on that server: it is what lets the meter
/// tell which characters share a 서버 창고 and a 슈고 열쇠 without the account id (which it never reads).
///
/// <para><b>Why a header signature, not a full walk.</b> A record is ~3.3 KB, most of it nested equipment and
/// customizing arrays whose 2026-11-11 layout no longer matches the 1.0.46 schema, so a field-by-field walk would
/// break on the next patch that touches equipment. Only the record HEAD is relied on, in schema order:
/// <c>[_key u32][_nickname len u8 + UTF-8][_pc_id u32][_race u8][_gender u8][_level u32]</c>.
/// Measured on the owner's 2026-11-13 capture (two fresh logins, two accounts on one server) and the 2026-10-07
/// in-game switch: every record matched, nothing else did.</para>
///
/// <para><b>A head's name must be a name.</b> A record ends <c>[… slot u8][_combat_power u32][4 zero bytes]</c>
/// right before the next head, so ten bytes before a real head the previous character's power can line up as a
/// "head" whose length byte is power byte 2 and whose tail is the real head's <c>_pc_id/_race/_gender/_level</c>
/// (adversarial review 2026-11-13, reproduced on the owner's frames: power 1,048,576–1,114,111 before a 2-syllable
/// name). Its "name" swallows the real key and nickname. Taking it would skip the real head with the count still
/// matching, silently dropping a character — so a candidate whose name is neither a character name nor the reserved
/// slot's shape is not a head, and the scan goes on byte by byte.</para>
///
/// <para><b>Fail closed.</b> The byte before the first head is the array count, and the list is accepted only when
/// exactly that many heads are found. A stray match inside a record, or a patch that moved the opcode onto another
/// message, makes the counts disagree and the frame yields nothing — no grouping at all is far better than two
/// accounts wrongly joined. The caller adds a second gate: the character that then enters the world must be in
/// the list. The server-move reconnect sends this same opcode with an empty list (77–79 B, 4 of 4 in the corpus);
/// it has no head and is ignored.</para>
///
/// <para>⚠️ <c>Login_RS</c> also carries <c>_new_account_session_token</c> in clear text after the list. Nothing
/// here reads past the last head, and nothing from this frame may be logged beyond counts.</para>
///
/// Pure and allocation-light; never throws.
/// </summary>
public static class LobbyRosterParser
{
    /// <summary>The array count sits right before the first head: offset 19 in <c>Login_RS</c> (after a 2-byte
    /// prefix and two i64 clocks), offset 3 in <c>Lobby_RS</c>. Anything further in is not this message.</summary>
    private const int MaxHeadOffset = 32;

    private const int MaxRecords = 64;
    private const int MaxNicknameBytes = 48;
    private const int HeadTail = 4 + 1 + 1 + 4; // _pc_id, _race, _gender, _level after the nickname

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static LobbyRosterParse TryParse(byte[] packet, int bodyStart)
    {
        if (packet is null || bodyStart < 0 || bodyStart >= packet.Length)
        {
            return LobbyRosterParse.None;
        }

        var heads = new List<string>();
        int first = -1;
        for (int i = bodyStart; i + 4 + 1 <= packet.Length; i++)
        {
            if (TryHead(packet, i, out string? name, out int next))
            {
                if (first < 0)
                {
                    first = i;
                    if (first - bodyStart < 1 || first - bodyStart > MaxHeadOffset)
                    {
                        return LobbyRosterParse.None; // the first head is not where the array starts
                    }
                }

                heads.Add(name!);
                if (heads.Count > MaxRecords)
                {
                    return LobbyRosterParse.None;
                }

                i = next - 1; // heads never overlap
            }
        }

        if (first < 0 || packet[first - 1] != heads.Count)
        {
            return LobbyRosterParse.None;
        }

        var nicknames = new List<string>(heads.Count);
        foreach (string name in heads)
        {
            if (IsCharacterName(name))
            {
                nicknames.Add(name);
            }
        }

        return new LobbyRosterParse(true, nicknames, heads.Count);
    }

    private static bool TryHead(byte[] b, int i, out string? name, out int next)
    {
        name = null;
        next = i + 1;
        if ((b[i] | b[i + 1] | b[i + 2] | b[i + 3]) == 0)
        {
            return false; // _key is never 0 (a guild name after a zero i64 would otherwise look like a head)
        }

        int len = b[i + 4];
        int o = i + 5 + len;
        if (len < 1 || len > MaxNicknameBytes || o + HeadTail > b.Length)
        {
            return false;
        }

        long pcId = PacketPrimitives.ReadUInt32LeAsLong(b, o);
        int race = b[o + 4];
        int gender = b[o + 5];
        long level = PacketPrimitives.ReadUInt32LeAsLong(b, o + 6);
        if (pcId is < 1 or > 255 || race is < 1 or > 2 || gender is < 1 or > 2 || level is < 1 or > 200)
        {
            return false;
        }

        try
        {
            name = StrictUtf8.GetString(b, i + 5, len);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!IsCharacterName(name) && !IsReservedSlot(name))
        {
            name = null;
            return false; // not a name: a run of the previous record that happens to line up (see the class notes)
        }

        next = o + HeadTail;
        return true;
    }

    /// <summary>The reserved slot every account's list opens with: <c>$</c> and ASCII letters/digits (11 of them in
    /// both 2026-11-13 lists). Counted as a record, never returned as a character.</summary>
    private static bool IsReservedSlot(string s)
    {
        if (s.Length is < 2 or > 24 || s[0] != '$')
        {
            return false;
        }

        for (int k = 1; k < s.Length; k++)
        {
            char c = s[k];
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A real character name: Hangul / Latin letters and digits, at least one letter. The reserved slot's
    /// <c>$</c>-prefixed name fails this, as does anything that is not a name.</summary>
    private static bool IsCharacterName(string s)
    {
        if (s.Length is < 1 or > 16)
        {
            return false;
        }

        bool letter = false;
        foreach (char c in s)
        {
            bool hangul = c >= '가' && c <= '힣';
            bool latin = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
            if (hangul || latin)
            {
                letter = true;
            }
            else if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return letter;
    }
}
