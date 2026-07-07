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
    private const int MaxQueue = 4;
    private const int MaxRequestAgeMs = 4000;
    private const int CacheLimit = 32;
    private const int RequestTimeoutMs = 3500;

    private sealed record Request(string Text, double Volume, long EnqueuedMs);

    private static readonly object Gate = new();
    private static BlockingCollection<Request>? _queue;
    private static Thread? _worker;
    private static readonly ConcurrentDictionary<string, byte[]> _cache = new();
    private static readonly ConcurrentQueue<string> _cacheOrder = new();
    private static long _disabledUntilMs; // Environment.TickCount64 when Edge synthesis may be retried

    /// <summary>Voice name (settable from settings; defaults to the Korean female voice).</summary>
    public static string Voice { get; set; } = EdgeTtsProtocol.DefaultVoice;

    /// <summary>Queue <paramref name="text"/> to be spoken at <paramref name="volume"/> (0..1). Returns
    /// immediately; never throws.</summary>
    public static void Speak(string text, double volume)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        EnsureWorker();
        var req = new Request(text.Trim(), Math.Clamp(volume, 0, 1), Environment.TickCount64);
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
            if (Environment.TickCount64 - req.EnqueuedMs > MaxRequestAgeMs)
            {
                continue; // too old — an alert spoken seconds late is worse than skipped
            }

            try
            {
                byte[]? mp3 = GetOrSynthesize(req.Text);
                if (mp3 is { Length: > 0 })
                {
                    Play(mp3, req.Volume);
                }
                else
                {
                    AlarmSound.Play(req.Volume); // network path unavailable — never go silent
                }
            }
            catch
            {
                AlarmSound.Play(req.Volume);
            }
        }
    }

    private static byte[]? GetOrSynthesize(string text)
    {
        if (_cache.TryGetValue(text, out byte[]? hit))
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

            Cache(text, mp3);
            return mp3;
        }
        catch
        {
            DisableFor(120_000); // 2 min back-off so a broken endpoint can't flood retries
            return null;
        }
    }

    private static void Cache(string text, byte[] mp3)
    {
        if (_cache.TryAdd(text, mp3))
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

    // Play an MP3 clip via a MediaPlayer on the UI dispatcher (MediaEnded fires there). The clip is short;
    // Play() returns immediately and the file is deleted after playback (or a hard timeout).
    private static void Play(byte[] mp3, double volume)
    {
        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        string path = Path.Combine(Path.GetTempPath(), "waffle_meter", "tts");
        Directory.CreateDirectory(path);
        string file = Path.Combine(path, Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(file, mp3);

        using var done = new ManualResetEventSlim(false);
        app.Dispatcher.Invoke(() =>
        {
            var player = new MediaPlayer { Volume = Math.Clamp(volume, 0, 1) };
            void Cleanup()
            {
                try { player.Close(); } catch { }
                done.Set();
            }

            player.MediaEnded += (_, _) => Cleanup();
            player.MediaFailed += (_, _) => Cleanup();
            player.Open(new Uri(file));
            player.Play();
        });

        done.Wait(8000); // bound the worker so a stuck clip can't wedge the queue
        try { File.Delete(file); } catch { }
    }
}
