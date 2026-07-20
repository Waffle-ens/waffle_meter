using System.IO.Compression;
using System.Text;
using WaffleMeter.Capture.Corpus;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Corpus-reader resilience spec: the gzip switch must not orphan the pre-gzip .jsonl archive
/// (magic-byte sniffing, not extension), and crash-cut .gz sessions — truncated tail, missing
/// trailer, even mid-stream corruption — must stay replayable up to the damage instead of throwing,
/// matching the leniency plain-text crash-cut files always had.
/// </summary>
public class CaptureCorpusReaderTests
{
    private static string TempFile(string name) =>
        Path.Combine(Path.GetTempPath(), "wm_ccr_test_" + Guid.NewGuid().ToString("N") + "_" + name);

    private static readonly string[] SampleLines =
    [
        "{\"type\":\"session_start\",\"at\":1,\"path\":\"x\"}",
        "{\"type\":\"capture\",\"at\":12345,\"ip\":\"10.0.0.1\",\"seq\":7,\"len\":3,\"head\":\"01 02 03\",\"data\":\"AQID\"}",
        "{\"type\":\"session_stop\",\"at\":2}",
    ];

    private static byte[] GzipBytes(IEnumerable<string> lines)
    {
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(new GZipStream(ms, CompressionLevel.Fastest), new UTF8Encoding(false)))
        {
            foreach (string line in lines)
            {
                writer.WriteLine(line);
            }
        }

        return ms.ToArray();
    }

    [Fact]
    public void Plain_jsonl_still_reads_regardless_of_extension()
    {
        string path = TempFile("legacy.jsonl");
        try
        {
            File.WriteAllLines(path, SampleLines, new UTF8Encoding(false));
            Assert.Equal(SampleLines, CaptureCorpusReader.ReadLines(path).ToArray());
            Assert.Single(CaptureCorpusReader.ReadCaptures(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Gzip_file_reads_via_magic_bytes_even_with_wrong_extension()
    {
        string path = TempFile("renamed.jsonl"); // gz content behind a .jsonl name — sniffing must win
        try
        {
            File.WriteAllBytes(path, GzipBytes(SampleLines));
            Assert.Equal(SampleLines, CaptureCorpusReader.ReadLines(path).ToArray());
            Assert.Single(CaptureCorpusReader.ReadCaptures(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Gzip_missing_trailer_yields_all_flushed_lines()
    {
        string path = TempFile("crash.jsonl.gz");
        try
        {
            byte[] full = GzipBytes(SampleLines);
            File.WriteAllBytes(path, full[..^8]); // drop CRC32+ISIZE trailer, like a killed process
            Assert.Equal(SampleLines, CaptureCorpusReader.ReadLines(path).ToArray());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Gzip_cut_mid_stream_yields_prefix_without_throwing()
    {
        string path = TempFile("cut.jsonl.gz");
        try
        {
            byte[] full = GzipBytes(Enumerable.Range(0, 500).Select(i => $"{{\"type\":\"meta\",\"n\":{i}}}"));
            File.WriteAllBytes(path, full[..(full.Length / 2)]);
            string[] lines = CaptureCorpusReader.ReadLines(path).ToArray(); // must not throw
            Assert.True(lines.Length > 0);
            Assert.True(lines.Length < 500);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Gzip_corrupted_mid_stream_ends_enumeration_without_throwing()
    {
        string path = TempFile("corrupt.jsonl.gz");
        try
        {
            string[] expected = Enumerable.Range(0, 500).Select(i => $"{{\"type\":\"meta\",\"n\":{i}}}").ToArray();
            byte[] full = GzipBytes(expected);
            full[full.Length / 2] ^= 0xFF; // bit-flip in the deflate stream
            File.WriteAllBytes(path, full);
            // Depending on where the flip lands, decode either garbles content or dies with
            // InvalidDataException mid-stream; either way enumeration must complete, not throw.
            string[] lines = CaptureCorpusReader.ReadLines(path).ToArray();
            Assert.False(lines.SequenceEqual(expected));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Empty_and_sub_magic_files_do_not_throw()
    {
        string empty = TempFile("empty.jsonl");
        string oneByte = TempFile("one.jsonl.gz");
        try
        {
            File.WriteAllBytes(empty, []);
            File.WriteAllBytes(oneByte, [0x1F]); // shorter than the magic — falls back to plain text
            Assert.Empty(CaptureCorpusReader.ReadLines(empty));
            Assert.Single(CaptureCorpusReader.ReadLines(oneByte));
        }
        finally
        {
            File.Delete(empty);
            File.Delete(oneByte);
        }
    }
}
