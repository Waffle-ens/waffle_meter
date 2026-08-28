using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Speaks short alert text with an online Korean neural voice (the browser read-aloud endpoint), shared by
/// the alarm reminders and — later — the buff/cooldown and field-boss alerts. Fire-and-forget: requests go
/// on a small bounded queue drained by one background worker; stale requests (older than a few seconds) are
/// dropped so a backlog never reads out old alerts. Synthesized clips are cached by text. When the network
/// path fails it disables itself briefly and falls back to the local chime so an alert is never silent.
///
/// The endpoint is unofficial (it can change without notice), so TTS is opt-in and every failure degrades
/// to the bundled sound.
/// </summary>
public static class TtsSpeech
{
    private const int MaxQueue = 16;

    /// <summary>How stale a non-durable request may be before the worker skips it as "spoken late is worse".
    /// It has to clear the longest a clip can legitimately hold the worker — the pack's longest line runs
    /// ~4.8 s and <see cref="InterClipGapMs"/> follows it — or a single long line ahead in the queue would
    /// drop the alarm behind it as late when nothing was late.</summary>
    private const int MaxRequestAgeMs = 8000;
    private const int CacheLimit = 32;
    private const int RequestTimeoutMs = 3500;

    // Durable = a burst-of-many alert (e.g. several buffs turning on at once) that must be spoken in sequence,
    // not dropped: it bypasses the stale-age skip so later items in the burst still play once the worker reaches
    // them. Non-durable (default, e.g. a time-sensitive alarm reminder) keeps the "spoken late is worse" skip.
    // ChimeFallback = play the bundled chime when the line cannot be spoken at all. Right for an alarm, whose
    // UI promises exactly that and which fires a few times an hour. Wrong for a buff, which fires dozens of
    // times a fight and whose whole point is WHICH buff — a chime cannot say that, so a run of identical
    // chimes is noise standing in for information the user asked for by name.
    private sealed record Request(string Text, double Volume, long EnqueuedMs, bool Durable, bool ChimeFallback);

    private static readonly object Gate = new();
    private static BlockingCollection<Request>? _queue;
    private static Thread? _worker;
    private static readonly ConcurrentDictionary<string, string> _cache = new(); // text → clip file path
    private static readonly ConcurrentQueue<string> _cacheOrder = new();
    private static long _disabledUntilMs; // Environment.TickCount64 when Edge synthesis may be retried

    /// <summary>Voice name for the ONLINE fallback (settable from settings; the Korean female voice).</summary>
    public static string Voice { get; set; } = EdgeTtsProtocol.DefaultVoice;

    private static BakedVoicePack? _pack;

    /// <summary>
    /// Select the shipped voice pack. Changing packs drops the memory cache: it is keyed by text alone, so
    /// clips rendered by the previous pack would otherwise keep playing in the old voice until restart.
    /// </summary>
    public static void SetVoicePack(BakedVoicePack? pack)
    {
        // Armed unconditionally, and ahead of the early return: the online voice follows the SELECTION, not
        // the change. Leaving it behind the guard would let a first call that happens to match the current
        // pack leave the fallback voice at its initial value, silently for as long as the app runs.
        Voice = BakedVoicePack.OnlineVoiceFor(pack?.Pack);

        if (_pack?.Pack == pack?.Pack)
        {
            return;
        }

        _pack = pack;
        _cache.Clear();
        _cacheOrder.Clear();
    }

    /// <summary>Queue <paramref name="text"/> to be spoken at <paramref name="volume"/> (0..1). Returns
    /// immediately; never throws. Set <paramref name="durable"/> for a burst that must all be spoken in order
    /// (buff on/off alerts) — those bypass the stale-age skip so a later item isn't dropped while earlier ones
    /// play.</summary>
    public static void Speak(string text, double volume, bool durable = false, bool chimeFallback = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        EnsureWorker();
        var req = new Request(text.Trim(), Math.Clamp(volume, 0, 1), Environment.TickCount64, durable, chimeFallback);
        // Bounded + newest-wins: if full, drop the oldest so a fresh alert isn't starved by a backlog.
        while (!_queue!.TryAdd(req))
        {
            if (!_queue.TryTake(out _))
            {
                break;
            }
        }
    }

    private static void EnsureWorker()
    {
        if (_worker is { IsAlive: true })
        {
            return;
        }

        lock (Gate)
        {
            if (_worker is { IsAlive: true })
            {
                return;
            }

            _queue = new BlockingCollection<Request>(MaxQueue);
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "waffle-tts" };
            _worker.Start();
        }
    }

    private static void WorkerLoop()
    {
        foreach (Request req in _queue!.GetConsumingEnumerable())
        {
            if (!req.Durable && Environment.TickCount64 - req.EnqueuedMs > MaxRequestAgeMs)
            {
                continue; // too old — a (non-durable) alert spoken seconds late is worse than skipped
            }

            try
            {
                string? clip = ResolveClipFile(req.Text);
                if (clip is null || !Play(clip, req.Volume))
                {
                    if (req.ChimeFallback)
                    {
                        AlarmSound.Play(req.Volume); // nothing could be spoken — never go silent
                    }
                }
            }
            catch
            {
                if (req.ChimeFallback)
                {
                    AlarmSound.Play(req.Volume);
                }
            }
        }
    }

    /// <summary>The file to play for <paramref name="text"/>, or null when it cannot be spoken at all.</summary>
    private static string? ResolveClipFile(string text)
    {
        // The shipped pack first: it covers every built-in line — every 스킬알림 and 보스알림 takes this path —
        // and needs no network. Played where the installer put it; only a custom alarm's free-text title and
        // lines newer than the installed pack fall past this to the online voice.
        string? baked = _pack?.TryGetPath(text);
        if (baked is not null)
        {
            return baked;
        }

        if (_cache.TryGetValue(text, out string? hit) && File.Exists(hit))
        {
            return hit;
        }

        if (Environment.TickCount64 < Interlocked.Read(ref _disabledUntilMs))
        {
            return null; // still in a cool-off after a recent failure
        }

        try
        {
            byte[] mp3 = SynthesizeAsync(text).GetAwaiter().GetResult();
            if (mp3.Length == 0 || !EdgeTtsProtocol.IsMp3(mp3))
            {
                DisableFor(120_000);
                return null;
            }

            return Store(text, mp3);
        }
        catch
        {
            DisableFor(120_000); // 2 min back-off so a broken endpoint can't flood retries
            return null;
        }
    }

    /// <summary>Write a synthesized clip and hand back its path, or null when the disk refuses. Keyed by voice
    /// as well as text: the same line in the other pack's voice is a different file, so switching packs can
    /// never land on a name a player still holds open. Its own catch, because a full disk or a locked %TEMP% is
    /// not evidence that the endpoint is broken — folded into the caller's catch it would take the online voice
    /// down for two minutes over a local failure.</summary>
    private static string? Store(string text, byte[] mp3)
    {
        try
        {
            string file = Path.Combine(TempDir(), BakedVoicePack.HashOf(Voice + "\n" + text) + ".mp3");
            File.WriteAllBytes(file, mp3);
            Cache(text, file);
            return file;
        }
        catch
        {
            return null;
        }
    }

    private static string? _tempDir;

    /// <summary>Where clips synthesized this session are kept. Swept once when it is first needed rather than
    /// deleted per clip: the old per-clip delete raced the player that was still reading the file, so it failed
    /// silently exactly when it mattered and left nothing to show for it.</summary>
    private static string TempDir()
    {
        if (_tempDir is { } cached)
        {
            return cached;
        }

        string dir = Path.Combine(Path.GetTempPath(), "waffle_meter", "tts");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string stale in Directory.EnumerateFiles(dir, "*.mp3"))
            {
                // Per file: one leftover that will not go (held elsewhere, ACL) must not abandon the rest.
                try { File.Delete(stale); } catch { }
            }
        }
        catch
        {
            // the folder itself would not enumerate — harmless, clips are written by hash and overwrite
        }

        return _tempDir = dir;
    }

    // Evicting a text only forgets the mapping; the file stays until the next session sweeps the folder. It is
    // keyed by hash, so re-synthesizing the same line simply rewrites the same bytes to the same name.
    private static void Cache(string text, string file)
    {
        if (_cache.TryAdd(text, file))
        {
            _cacheOrder.Enqueue(text);
            while (_cache.Count > CacheLimit && _cacheOrder.TryDequeue(out string? oldest))
            {
                _cache.TryRemove(oldest, out _);
            }
        }
    }

    private static void DisableFor(int ms) => Interlocked.Exchange(ref _disabledUntilMs, Environment.TickCount64 + ms);

    private static async Task<byte[]> SynthesizeAsync(string text)
    {
        string connId = Guid.NewGuid().ToString("N");
        string gec = EdgeTtsProtocol.SecMsGecToken(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var uri = new Uri(EdgeTtsProtocol.BuildEndpointUri(connId, gec));

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        ws.Options.SetRequestHeader("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0");
        using var cts = new CancellationTokenSource(RequestTimeoutMs);

        await ws.ConnectAsync(uri, cts.Token).ConfigureAwait(false);

        string ts = DateTimeOffset.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'");
        await SendText(ws, EdgeTtsProtocol.BuildSpeechConfigMessage(ts), cts.Token).ConfigureAwait(false);
        string ssml = EdgeTtsProtocol.BuildSsml(text, Voice);
        await SendText(ws, EdgeTtsProtocol.BuildSsmlMessage(connId, ts, ssml), cts.Token).ConfigureAwait(false);

        var audio = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var frame = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            frame.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, cts.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return audio.ToArray();
                }

                frame.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            byte[] frameBytes = frame.ToArray();
            if (result.MessageType == WebSocketMessageType.Text)
            {
                if (Encoding.UTF8.GetString(frameBytes).Contains("Path:turn.end"))
                {
                    break;
                }
            }
            else
            {
                ReadOnlySpan<byte> payload = EdgeTtsProtocol.ExtractAudioPayload(frameBytes);
                if (payload.Length > 0)
                {
                    audio.Write(payload);
                }
            }
        }

        return audio.ToArray();
    }

    private static Task SendText(ClientWebSocket ws, string message, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct);

    // ── playback ───────────────────────────────────────────────────────────────────────────────────────
    //
    // Two MediaPlayers, made once on the UI thread and held in a static field for the life of the process.
    // Both halves of that sentence are load-bearing.
    //
    //  1) STATIC, THEREFORE ROOTED. Until 2026-08-28 the player was a local inside the dispatcher lambda. The
    //     moment Invoke returned, the only references left to it were the delegates hanging off its own events
    //     — player → MediaEnded → closure → player — and a cycle is not a GC root. Any ephemeral collection
    //     landing inside the clip finalized the media handle, tore the native pipeline down, and the sound
    //     stopped mid-word wherever that collection happened to fall. MediaEnded never fired afterwards, so
    //     the tail grace added in 8883e11 never ran either: it guarded the window AFTER MediaEnded, and the
    //     clip was dying before it. That is why "the end goes missing sometimes" survived a fix aimed straight
    //     at it, why no clip length is a cliff (a longer clip is only more seconds of exposure to the next
    //     gen-0), and why it tracks how busy the app is rather than which line is speaking.
    //     Never let a playing MediaPlayer be a local again.
    //
    //  2) TWO OF THEM, USED IN TURN. Open()ing a new source tears the old topology down, renderer included, so
    //     one player would leave the previous clip's undrained tail at the mercy of how soon the next alert
    //     arrives. Alternating means the clip that just ended keeps draining on its own player while the next
    //     one opens on the other, and nothing here ever closes a player that is still sounding. Measured, that
    //     is worth 10-25 ms of tail — small, because MediaEnded turns out to arrive 20-32 ms AFTER the last
    //     sample reaches the mix, not before it. The words the user was losing were taken by (1), not by this;
    //     what (2) removes is the need to guess a constant at all. Do not fold the two back into one player on
    //     the grounds that the margin is small — the margin is small only while nothing closes a live player.
    private static readonly MediaPlayer?[] Players = new MediaPlayer?[2]; // UI thread only
    private static readonly int[] SlotClipId = new int[2];                // UI thread only
    private static int _slot = 1, _clipIds, _activeClip;                  // UI thread only
    private static DispatcherTimer? _watchdog;                            // UI thread only
    private static volatile bool _clipFailed;                             // set on the UI thread, read by the worker
    private static readonly ManualResetEventSlim ClipDone = new(false);   // one worker ⇒ one clip in flight

    /// <summary>Spacing between consecutive alerts — NOT tail protection, which the two players above make
    /// structural. Set this too low and two alerts overlap; it can no longer cut one short.</summary>
    private const int InterClipGapMs = 250;

    /// <summary>Last-resort bound on the worker's park. The watchdog below is armed off the clip's own
    /// duration and should always beat it; this only catches a clip that never opened at all.</summary>
    private const int ClipWaitCapMs = 10_000;

    /// <summary>Play <paramref name="file"/> and park the worker until it has finished, so the queue is drained
    /// at the speed the alerts are actually spoken. False when the clip could not be played at all — a truncated
    /// or quarantined file in the installed pack, a codec the machine lacks — so the caller can fall back rather
    /// than count a silence as spoken.</summary>
    private static bool Play(string file, double volume)
    {
        Application? app = Application.Current;
        if (app is null)
        {
            return false;
        }

        app.Dispatcher.Invoke(() =>
        {
            // Claim an id first: any late event from the clip before this one is now stale and cannot signal.
            _activeClip = ++_clipIds;
            _clipFailed = false;
            ClipDone.Reset();

            MediaPlayer player = NextPlayer();
            // Closed before it is re-opened, and before the slot is stamped with this clip's id so that nothing
            // Close() might raise can be mistaken for this clip's own event. The order matters: a player does
            // NOT raise MediaOpened for a
            // source it already holds (measured — suppressed from the third play on, both when one line repeats
            // and when two alternate, which is the shape a buff on/off pair makes all fight). No MediaOpened
            // means the watchdog keeps the placeholder below instead of the clip's real length, and unparks the
            // worker mid-clip so the next alert starts over this one. This is the player from TWO clips ago and
            // is long finished; the one that may still be draining is on the other slot, which is not touched.
            try { player.Close(); } catch { }
            SlotClipId[_slot] = _activeClip;
            player.Volume = Math.Clamp(volume, 0, 1);
            player.Open(new Uri(file));
            player.Play();
            ArmWatchdog(3000); // replaced by the real duration once MediaOpened lands
        });

        ClipDone.Wait(ClipWaitCapMs);
        Thread.Sleep(InterClipGapMs);
        return !_clipFailed;
    }

    private static MediaPlayer NextPlayer()
    {
        _slot ^= 1;
        if (Players[_slot] is { } existing)
        {
            return existing;
        }

        int slot = _slot;
        var player = new MediaPlayer();
        player.MediaOpened += (_, _) =>
        {
            if (SlotClipId[slot] != _activeClip)
            {
                return; // a late open from a clip we have already moved past
            }

            Duration d = player.NaturalDuration;
            // Generous on purpose: this is the "MediaEnded never came" net, not the thing that ends the clip.
            ArmWatchdog(d.HasTimeSpan ? (int)d.TimeSpan.TotalMilliseconds + 2000 : 6000);
        };
        player.MediaEnded += (_, _) => FinishClip(SlotClipId[slot]);
        player.MediaFailed += (_, _) =>
        {
            if (SlotClipId[slot] == _activeClip)
            {
                _clipFailed = true; // a pack clip the machine cannot play is not a spoken alert
            }

            FinishClip(SlotClipId[slot]);
        };
        Players[slot] = player;
        return player;
    }

    private static void ArmWatchdog(int ms)
    {
        // DispatcherPriority.Normal, spelled out. The parameterless DispatcherTimer ctor defaults to Background,
        // which sits behind Render and DataBind — a busy dispatcher starves it exactly when it is needed, and a
        // starved net here parks the worker (and every alert behind it) for the full ClipWaitCapMs.
        if (_watchdog is null)
        {
            _watchdog = new DispatcherTimer(DispatcherPriority.Normal);
            // Stopped here rather than only inside FinishClip: that one returns early for a clip we have moved
            // past, and an unstopped DispatcherTimer free-runs for the life of the process.
            _watchdog.Tick += (_, _) => { _watchdog!.Stop(); FinishClip(_activeClip); };
        }

        _watchdog.Stop();
        _watchdog.Interval = TimeSpan.FromMilliseconds(ms);
        _watchdog.Start();
    }

    private static void FinishClip(int id)
    {
        if (id == 0 || id != _activeClip)
        {
            return; // a late event from a clip we have already moved past
        }

        _activeClip = 0;
        _watchdog?.Stop();
        ClipDone.Set();
    }

    /// <summary>Release the players at exit. Each holds its last clip open until it is given another one, and
    /// measured, that handle blocks writing the file and renaming the folder it sits in (deleting or renaming
    /// the file itself still works) — which is what an update does to <c>voice/</c>.</summary>
    public static void Shutdown()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.Dispatcher.Invoke(() =>
        {
            _watchdog?.Stop();
            for (int i = 0; i < Players.Length; i++)
            {
                try { Players[i]?.Close(); } catch { }
                Players[i] = null;
            }

            _activeClip = 0;
            ClipDone.Set(); // never leave the worker parked on a player that is gone
        });
    }
}
