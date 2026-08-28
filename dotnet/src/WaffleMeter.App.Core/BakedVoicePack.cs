using System.Security.Cryptography;
using System.Text;

namespace WaffleMeter.App.Core;

/// <summary>
/// The shipped voice packs: every alert line this build can produce, rendered ahead of time and bundled as
/// MP3 under <c>voice/&lt;pack&gt;/</c>.
///
/// <para><b>Why pre-rendered.</b> The online endpoint the app falls back to is unofficial, and every first
/// utterance of a phrase costs a network round trip that lands inside an 800 ms alert budget. The set of
/// lines is closed and small — field bosses × lead times, the fixed cues, and the buff catalogue on/off —
/// so it can simply be rendered once and shipped. Playback then costs a file read.</para>
///
/// <para><b>Lookup is by content hash.</b> The file name is the first 16 hex characters of the SHA-256 of the
/// exact spoken string, so no name table has to be shipped or kept in sync — the phrase IS the key. That also
/// means a phrase whose wording changes silently stops resolving rather than playing the wrong clip, which is
/// the failure we want: it falls through to the online path instead of lying.</para>
///
/// <para><b>It is allowed to be incomplete.</b> A line the pack has never heard of — a user's custom alarm, a
/// boss added by a patch newer than the installed pack — returns false and the caller degrades. See
/// <c>TtsSpeech</c> for the chain.</para>
/// </summary>
public sealed class BakedVoicePack
{
    /// <summary>Pack ids, which are also the folder names under <c>voice/</c>.</summary>
    public const string Wasuni = "와순이";
    public const string Wabungi = "와붕이";

    public static readonly string[] All = { Wasuni, Wabungi };

    public static bool IsKnown(string? pack) => pack is not null && Array.IndexOf(All, pack) >= 0;

    /// <summary>
    /// The online voice that reads a line this pack never baked. A stand-in, not a match — the packs are
    /// rendered locally from a reference clip and no endpoint voice reproduces them — but it keeps the gender
    /// the user chose. Before this existed, every unbaked line came back in the female voice no matter which
    /// pack was selected, which is what made a miss audible rather than merely late.
    /// </summary>
    public static string OnlineVoiceFor(string? pack) =>
        pack == Wabungi ? EdgeTtsProtocol.MaleVoice : EdgeTtsProtocol.DefaultVoice;

    private readonly string _root;

    public BakedVoicePack(string appDirectory, string pack)
    {
        Pack = IsKnown(pack) ? pack : Wasuni;
        _root = Path.Combine(appDirectory, "voice", Pack);
    }

    public string Pack { get; }

    /// <summary>True when this build actually shipped the pack's files. A publish that dropped the assets
    /// should degrade to the online voice, not go silent.</summary>
    public bool Exists => Directory.Exists(_root);

    /// <summary>The pre-rendered clip for <paramref name="text"/>, or null when the pack has no such line.</summary>
    public byte[]? TryGet(string text)
    {
        string? path = TryGetPath(text);
        try
        {
            return path is null ? null : File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null; // a locked or half-written file is a miss, not a crash
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Where the pre-rendered clip for <paramref name="text"/> lives, or null when the pack has no
    /// such line. Playback hands this path straight to the player instead of copying the bytes through a temp
    /// file: a clip the installer already put on disk has nothing to gain from a second copy, and the copy is
    /// what used to be deleted out from under a player that was still reading it.</summary>
    public string? TryGetPath(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string path = Path.Combine(_root, FileNameFor(text));
        try
        {
            return File.Exists(path) ? path : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The bake script writes exactly this name — keep the two in step.</summary>
    public static string FileNameFor(string text) => HashOf(text) + ".mp3";

    public static string HashOf(string text)
    {
        byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim()));
        return Convert.ToHexStringLower(h)[..16];
    }
}
