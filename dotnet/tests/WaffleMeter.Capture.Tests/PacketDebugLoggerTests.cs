using System.Text;
using System.Text.RegularExpressions;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Format-parity spec for <see cref="PacketDebugLogger"/>: the on-disk NDJSON must be byte-compatible
/// with the Kotlin <c>PacketDebugLogger</c> so corpora are replayable by DpsReplayCli / generateGolden.
/// Events whose "at" is the passed arrivedAt / packet timestamp are deterministic and matched exactly;
/// wall-clock "at" events are matched after stripping the volatile field.
/// </summary>
public class PacketDebugLoggerTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "wm_pdl_test_" + Guid.NewGuid().ToString("N"));

    private static string[] RunSession(string dir, Action<PacketDebugLogger> body)
    {
        var logger = new PacketDebugLogger(dir);
        logger.Start();
        body(logger);
        logger.Stop();
        string file = Directory.GetFiles(dir, "*.jsonl").Single();
        return File.ReadAllLines(file, Encoding.UTF8).Where(l => l.Length > 0).ToArray();
    }

    [Fact]
    public void Capture_line_is_byte_exact()
    {
        string dir = TempDir();
        try
        {
            string[] lines = RunSession(dir, l => l.Capture("10.0.0.1", 7, new byte[] { 0x01, 0x02, 0x03 }, 12345));
            // session_start, capture, session_stop
            Assert.Equal(
                "{\"type\":\"capture\",\"at\":12345,\"ip\":\"10.0.0.1\",\"seq\":7,\"len\":3,\"head\":\"01 02 03\",\"data\":\"AQID\"}",
                lines[1]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Assembled_line_is_byte_exact()
    {
        string dir = TempDir();
        try
        {
            string[] lines = RunSession(dir, l => l.Assembled(new byte[] { 0xAB, 0xCD }, 999));
            Assert.Equal("{\"type\":\"assembled\",\"at\":999,\"len\":2,\"head\":\"AB CD\"}", lines[1]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Damage_line_is_byte_exact()
    {
        string dir = TempDir();
        try
        {
            var pdp = new ParsedDamagePacket
            {
                ActorId = 10, TargetId = 20, SkillCode = 30, Damage = 40,
                Type = 3 /* crit */, Dot = false, Loop = 0, Timestamp = 777,
            };
            string[] lines = RunSession(dir, l => l.Damage("direct", pdp, saved: true, reason: null, mobCode: null));
            Assert.Equal(
                "{\"type\":\"damage\",\"kind\":\"direct\",\"at\":777,\"saved\":true,\"reason\":null," +
                "\"actor\":10,\"target\":20,\"skill\":30,\"damage\":40,\"crit\":true,\"dot\":false,\"loop\":0,\"mobCode\":null}",
                lines[1]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Meta_preserves_order_raw_korean_lowercase_bool_and_null()
    {
        string dir = TempDir();
        try
        {
            string[] lines = RunSession(dir, l => l.Meta("nickname",
                ("own", true), ("uid", 1144), ("nickname", "플러시"), ("server", 2003), ("job", null)));
            // strip the wall-clock "at" field, then the rest must match exactly (key order, raw Korean,
            // lowercase bool, null literal).
            string stripped = Regex.Replace(lines[1], "\"at\":\\d+,", "");
            Assert.Equal(
                "{\"type\":\"nickname\",\"own\":true,\"uid\":1144,\"nickname\":\"플러시\",\"server\":2003,\"job\":null}",
                stripped);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CompressedPacket_is_logged_as_meta_shape()
    {
        string dir = TempDir();
        try
        {
            string[] lines = RunSession(dir, l => l.CompressedPacket(64, extraFlag: true));
            string stripped = Regex.Replace(lines[1], "\"at\":\\d+,", "");
            Assert.Equal("{\"type\":\"compressed_packet\",\"len\":64,\"extraFlag\":true}", stripped);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Session_start_and_stop_bookend_the_file()
    {
        string dir = TempDir();
        try
        {
            string[] lines = RunSession(dir, _ => { });
            Assert.StartsWith("{\"type\":\"session_start\",\"at\":", lines[0]);
            Assert.Contains("\"path\":", lines[0]);
            Assert.StartsWith("{\"type\":\"session_stop\",\"at\":", lines[^1]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Without_start_every_method_is_a_noop()
    {
        string dir = TempDir();
        try
        {
            var logger = new PacketDebugLogger(dir);
            // No Start(): all of these must be cheap no-ops and write nothing.
            logger.Capture("1.2.3.4", 1, new byte[] { 0x01 }, 1);
            logger.Assembled(new byte[] { 0x02 }, 2);
            logger.Dispatch(0x3804, "Damage", false, 5);
            logger.UnknownOpcode(0x3603, false, 5);
            logger.Meta("nickname", ("uid", 1));
            Assert.False(logger.IsRunning);
            Assert.False(Directory.Exists(dir) && Directory.GetFiles(dir, "*.jsonl").Length > 0);
        }
        finally { if (Directory.Exists(dir)) { Directory.Delete(dir, true); } }
    }
}
