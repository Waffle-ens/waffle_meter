using WaffleMeter.Capture;
using WaffleMeter.Capture.Corpus;
using WaffleMeter.Data;

namespace WaffleMeter.App.Core;

/// <summary>
/// DEV BUILDS ONLY. Feeds a recorded packet-debug corpus back through the live pipeline
/// (aligner → assembler → StreamProcessor → DataManager/DpsCalculator) so its battles land in the
/// history panel and the detail window — no dungeon run needed to look at a UI change.
/// </summary>
/// <remarks>
/// Two things are suspended for the duration, and both matter:
/// <list type="bullet">
/// <item><c>Calculator.OnBattleLogged</c> — otherwise every replayed battle would be queued for upload to
/// the live stats site (and written as a movement replay).</item>
/// <item><c>Data.Clock</c> — the corpus carries its own timestamps; a wall clock would make every battle
/// look hours long and skew DPS.</item>
/// </list>
/// The replay also resets the meter's live battle state, so it is not something to run mid-fight.
/// </remarks>
public static class DevPacketLogReplay
{
    /// <summary>True only for a `-dev` build version, which is what gates the tray entry.</summary>
    public static bool IsAvailable(string version) =>
        version.Contains("-dev", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where the app's own packet-debug recorder writes, so the file picker opens there.</summary>
    public static string DefaultLogDirectory()
    {
        string appData = Environment.GetEnvironmentVariable("APPDATA")
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(appData, "waffle_meter.v1.4", "packet-debug-logs");
    }

    /// <summary>Replays <paramref name="corpusPath"/> and returns how many battles were saved.</summary>
    public static int Run(MeterServices services, string corpusPath)
    {
        DataManager data = services.Data;
        DpsCalculator calculator = services.Calculator;

        Func<long> savedClock = data.Clock;
        Action<DpsLog>? savedOnLogged = calculator.OnBattleLogged;

        // One aligner + assembler PER SOURCE IP. A capture log interleaves a dozen streams (game server,
        // the client's own outbound, loopback, unrelated hosts); a single aligner reset on every IP change
        // shreds the TCP reassembly and yields zero battles.
        var streams = new Dictionary<string, (PacketAlignmenter Aligner, StreamAssembler Assembler)>();
        var processor = new StreamProcessor(NullStreamProcessorSink.Instance, data);
        long simNow = 0;

        try
        {
            calculator.OnBattleLogged = null;
            data.Clock = () => simNow;

            foreach (CapturedSegment segment in CaptureCorpusReader.ReadCaptures(corpusPath))
            {
                simNow = segment.ArrivedAtMs;
                if (!streams.TryGetValue(segment.SrcIp, out (PacketAlignmenter Aligner, StreamAssembler Assembler) stream))
                {
                    stream = (new PacketAlignmenter(), new StreamAssembler((p, at) => processor.OnPacketReceived(p, at)));
                    streams[segment.SrcIp] = stream;
                }

                foreach (AlignedChunk chunk in stream.Aligner.Feed(segment.Seq, segment.Payload, segment.ArrivedAtMs))
                {
                    stream.Assembler.ProcessChunk(chunk.Data, chunk.ArrivedAt);
                }

                calculator.GetDps();
            }

            foreach ((PacketAlignmenter aligner, StreamAssembler assembler) in streams.Values)
            {
                assembler.Flush();
                aligner.Reset();
            }

            // Closes out the battle still in progress at the end of the corpus, so it reaches the history.
            calculator.ResetDataStorage();
        }
        finally
        {
            data.Clock = savedClock;
            calculator.OnBattleLogged = savedOnLogged;
        }

        return data.RecentBattleList().Count;
    }
}
