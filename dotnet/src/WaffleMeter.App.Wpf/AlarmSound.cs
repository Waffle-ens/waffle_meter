using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Procedural alarm chime. There are NO bundled audio assets — a short three-note sine arpeggio is
/// synthesized into a 16-bit PCM WAV in memory at the requested volume and played via
/// <see cref="SoundPlayer"/> on a worker thread. Volume is baked into the sample amplitude because
/// SoundPlayer has no volume control.
/// </summary>
public static class AlarmSound
{
    private const int SampleRate = 44100;

    /// <summary>Play the chime at <paramref name="volume"/> (0..1). No-op at 0 or if no audio device.</summary>
    public static void Play(double volume)
    {
        double v = Math.Clamp(volume, 0.0, 1.0);
        if (v <= 0.0)
        {
            return;
        }

        byte[] wav = BuildChime(v);

        // PlaySync on a worker so the player + stream stay alive for the whole (short) playback.
        Task.Run(() =>
        {
            try
            {
                using var player = new SoundPlayer(new MemoryStream(wav));
                player.PlaySync();
            }
            catch
            {
                // no audio device / busy — ignore
            }
        });
    }

    private static byte[] BuildChime(double volume)
    {
        (double Freq, int Ms)[] notes = { (880.0, 120), (1174.66, 120), (1567.98, 220) }; // A5 · D6 · G6
        int total = notes.Sum(n => SampleRate * n.Ms / 1000);
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

        double amp = 0.32 * volume * short.MaxValue;
        foreach ((double freq, int ms) in notes)
        {
            int samples = SampleRate * ms / 1000;
            for (int s = 0; s < samples; s++)
            {
                double sample = Math.Sin(2.0 * Math.PI * freq * s / SampleRate) * amp * Fade(s, samples);
                w.Write((short)sample);
            }
        }

        w.Flush();
        return mem.ToArray();
    }

    // Linear attack over the first 8% and release over the last 25% so each note is click-free.
    private static double Fade(int i, int n)
    {
        double attack = n * 0.08, release = n * 0.25;
        if (i < attack)
        {
            return i / attack;
        }

        if (i > n - release)
        {
            return Math.Max(0.0, (n - i) / release);
        }

        return 1.0;
    }
}
