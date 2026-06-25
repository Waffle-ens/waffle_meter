using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Resources;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Alarm sound. Plays the bundled notification WAV (embedded as a Resource) at the requested volume via
/// <see cref="SoundPlayer"/>. SoundPlayer has no volume control, so the 16-bit samples are scaled by the
/// volume before playback. Falls back to a procedurally-synthesized glass-bell chime
/// (<see cref="BuildChime"/>) if the embedded asset can't be loaded.
/// </summary>
public static class AlarmSound
{
    private const int SampleRate = 44100;
    private static byte[]? _bundled; // cached embedded notification.wav bytes
    private static bool _triedLoad;

    /// <summary>Play the alarm at <paramref name="volume"/> (0..1). No-op at 0 or if no audio device.</summary>
    public static void Play(double volume)
    {
        double v = Math.Clamp(volume, 0.0, 1.0);
        if (v <= 0.0)
        {
            return;
        }

        byte[]? bundled = LoadBundled(); // load on the caller (UI) thread; cached after the first call

        // PlaySync on a worker so the player + stream stay alive for the whole (short) playback; the
        // per-sample volume scaling also runs here, off the UI thread.
        Task.Run(() =>
        {
            try
            {
                byte[] wav = bundled != null ? ApplyVolume(bundled, v) : BuildChime(v);
                using var player = new SoundPlayer(new MemoryStream(wav));
                player.PlaySync();
            }
            catch
            {
                // no audio device / busy — ignore
            }
        });
    }

    /// <summary>Load the embedded notification.wav once. Returns null if absent/unreadable (→ synth fallback).</summary>
    private static byte[]? LoadBundled()
    {
        if (_triedLoad)
        {
            return _bundled;
        }

        _triedLoad = true;
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/Sounds/notification.wav");
            StreamResourceInfo? info = Application.GetResourceStream(uri);
            if (info?.Stream is { } stream)
            {
                using (stream)
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    _bundled = ms.ToArray();
                }
            }
        }
        catch
        {
            _bundled = null;
        }

        return _bundled;
    }

    /// <summary>Scale a 16-bit PCM WAV's samples by <paramref name="volume"/> (SoundPlayer can't set volume).
    /// Scans the RIFF chunks for "data" so it tolerates a LIST/INFO chunk before it (ffmpeg writes one).</summary>
    private static byte[] ApplyVolume(byte[] wav, double volume)
    {
        double v = Math.Clamp(volume, 0.0, 1.0);
        if (v >= 0.999 || wav.Length < 12)
        {
            return wav; // full volume (or not a WAV) — play as-is
        }

        byte[] bytes = (byte[])wav.Clone();
        int i = 12; // skip RIFF(4) + size(4) + WAVE(4)
        while (i + 8 <= bytes.Length)
        {
            int size = BitConverter.ToInt32(bytes, i + 4);
            if (bytes[i] == (byte)'d' && bytes[i + 1] == (byte)'a' && bytes[i + 2] == (byte)'t' && bytes[i + 3] == (byte)'a')
            {
                int start = i + 8;
                int end = Math.Min(start + size, bytes.Length);
                for (int p = start; p + 1 < end; p += 2)
                {
                    short s = (short)(bytes[p] | (bytes[p + 1] << 8));
                    short scaled = (short)Math.Clamp(s * v, (double)short.MinValue, short.MaxValue);
                    bytes[p] = (byte)(scaled & 0xFF);
                    bytes[p + 1] = (byte)((scaled >> 8) & 0xFF);
                }

                break;
            }

            if (size < 0)
            {
                break;
            }

            i += 8 + size + (size & 1); // chunks are word-aligned
        }

        return bytes;
    }

    private static byte[] BuildChime(double volume)
    {
        // A soft glassy bell arpeggio: an A-major triad (A5 · C#6 · E6) where each note is additive
        // (fundamental + a few harmonics, the top one slightly inharmonic for a glassy shimmer) with an
        // exponential decay, and the notes OVERLAP so they ring into a chord. Fully synthesized — no audio
        // assets, no licensing.
        (double Freq, int StartMs)[] notes = { (880.0, 0), (1108.73, 95), (1318.51, 190) };
        const int ringMs = 620;
        (double Mult, double Amp, double DecayMul)[] partials =
        {
            (1.00, 1.00, 1.0),
            (2.00, 0.45, 1.7),
            (3.00, 0.22, 2.6),
            (4.02, 0.10, 3.6), // slightly inharmonic top partial = glassy shimmer
        };
        const double baseTau = 0.36;  // fundamental decay time-constant (s)
        const double attackS = 0.004; // 4ms soft attack (click-free)

        int total = SampleRate * (notes.Max(n => n.StartMs) + ringMs) / 1000;
        var buf = new double[total];
        int ring = SampleRate * ringMs / 1000;
        foreach ((double freq, int startMs) in notes)
        {
            int start = SampleRate * startMs / 1000;
            for (int i = 0; i < ring && start + i < total; i++)
            {
                double t = (double)i / SampleRate;
                double attack = Math.Min(1.0, t / attackS);
                double s = 0;
                foreach ((double mult, double amp, double decayMul) in partials)
                {
                    s += amp * Math.Sin(2.0 * Math.PI * freq * mult * t) * Math.Exp(-t * decayMul / baseTau);
                }

                buf[start + i] += s * attack;
            }
        }

        // Normalize (the summed notes/partials can exceed 1.0), then apply volume.
        double peak = 0;
        for (int i = 0; i < total; i++)
        {
            peak = Math.Max(peak, Math.Abs(buf[i]));
        }

        double scale = (peak > 0 ? 0.85 / peak : 0) * Math.Clamp(volume, 0.0, 1.0) * short.MaxValue;

        int dataBytes = total * 2;
        using var mem = new MemoryStream();
        using var w = new BinaryWriter(mem);
        // RIFF / WAVE 16-bit mono PCM header.
        w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        w.Write(36 + dataBytes);
        w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
        w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        w.Write(16);              // fmt chunk size
        w.Write((short)1);        // PCM
        w.Write((short)1);        // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);  // byte rate (mono, 16-bit)
        w.Write((short)2);        // block align
        w.Write((short)16);       // bits per sample
        w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        w.Write(dataBytes);

        for (int i = 0; i < total; i++)
        {
            w.Write((short)Math.Clamp(buf[i] * scale, (double)short.MinValue, short.MaxValue));
        }

        w.Flush();
        return mem.ToArray();
    }
}
