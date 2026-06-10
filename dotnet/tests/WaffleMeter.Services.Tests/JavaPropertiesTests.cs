using System.Text;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.Services.Tests;

public sealed class JavaPropertiesTests
{
    private static JavaProperties LoadFrom(string text)
    {
        var props = new JavaProperties();
        props.Load(new MemoryStream(Encoding.Latin1.GetBytes(text)));
        return props;
    }

    [Theory]
    [InlineData("a=1", "a", "1")]
    [InlineData("a:2", "a", "2")]
    [InlineData("a 3", "a", "3")]                 // whitespace separator
    [InlineData("  a   =   4  ", "a", "4  ")]      // leading ws trimmed, value keeps trailing
    [InlineData("a=", "a", "")]
    [InlineData("key.with.dots=v", "key.with.dots", "v")]
    public void Parses_separators_and_whitespace(string line, string key, string value)
    {
        Assert.Equal(value, LoadFrom(line).GetProperty(key));
    }

    [Fact]
    public void Skips_comments_and_blank_lines()
    {
        JavaProperties p = LoadFrom("# comment\n! also comment\n\n   \nreal=yes\n");
        Assert.Equal("yes", p.GetProperty("real"));
        Assert.Null(p.GetProperty("# comment"));
        Assert.Single(p.Entries);
    }

    [Fact]
    public void Joins_backslash_continuation_dropping_leading_whitespace()
    {
        JavaProperties p = LoadFrom("a=hello \\\n     world");
        Assert.Equal("hello world", p.GetProperty("a"));
    }

    [Fact]
    public void Unescapes_known_escapes_and_unicode()
    {
        // Build the file text at runtime so backslash sequences are real bytes (no source-escape
        // ambiguity). bs is a single backslash.
        string bs = ((char)92).ToString();
        string text = "a=x" + bs + "ty" + bs + "=z" + bs + ":w" + bs + bs + "q\n"
                    + "k=" + bs + "u0041" + bs + "u00e9"; // A -> 'A', é -> 'é'

        JavaProperties p = LoadFrom(text);

        Assert.Equal("x\ty=z:w" + bs + "q", p.GetProperty("a")); // \t tab, \= =, \: :, \\ \
        Assert.Equal("A" + (char)0x00E9, p.GetProperty("k"));
    }

    [Fact]
    public void Escaped_separator_in_key_is_literal()
    {
        JavaProperties p = LoadFrom(@"a\=b=v");
        Assert.Equal("v", p.GetProperty("a=b"));
    }

    [Fact]
    public void Store_then_load_round_trips_including_korean_and_specials()
    {
        var p = new JavaProperties();
        p.SetProperty("opacity", "0.85");
        p.SetProperty("nick", "가나다");
        p.SetProperty("path", "C:\\waffle meter\\x");
        p.SetProperty("eq", "a=b#c");

        using var ms = new MemoryStream();
        p.Store(ms, "settings");
        ms.Position = 0;

        var reloaded = new JavaProperties();
        reloaded.Load(ms);

        Assert.Equal("0.85", reloaded.GetProperty("opacity"));
        Assert.Equal("가나다", reloaded.GetProperty("nick"));
        Assert.Equal("C:\\waffle meter\\x", reloaded.GetProperty("path"));
        Assert.Equal("a=b#c", reloaded.GetProperty("eq"));
    }

    [Fact]
    public void Store_escapes_korean_as_ascii_unicode_escapes()
    {
        var p = new JavaProperties();
        p.SetProperty("nick", "가");

        using var ms = new MemoryStream();
        p.Store(ms, "settings");
        string text = Encoding.Latin1.GetString(ms.ToArray());

        string bs = ((char)92).ToString();
        Assert.Contains("nick=" + bs + "uAC00", text); // 가 -> 가 (uppercase hex)
        Assert.All(text, ch => Assert.True(ch < 0x80, "store output must be pure ASCII"));
    }
}
