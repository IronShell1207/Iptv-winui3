using System.IO.Compression;
using IptvPlayer.Core.Parsing;
using Xunit;

namespace IptvPlayer.Tests;

public class XmltvParserTests
{
    [Fact]
    public void ParsesChannelsAndProgrammes()
    {
        var doc = XmltvParser.ParseText(TestData.Epg);

        Assert.Equal(2, doc.Channels.Count);
        Assert.Equal(3, doc.Programmes.Count);
        Assert.Equal("Первый канал HD", doc.Channels[0].DisplayName);
        Assert.Equal("http://logo.local/1.png", doc.Channels[0].IconUrl);
    }

    [Fact]
    public void ReadsTitleDescriptionAndCategory()
    {
        var doc = XmltvParser.ParseText(TestData.Epg);

        var news = doc.Programmes[0];
        Assert.Equal("Новости", news.Title);
        Assert.Equal("Информационная программа", news.Description);
        Assert.Equal("Новости", news.Category);
        Assert.Equal(TimeSpan.FromHours(1), news.Duration);
    }

    [Fact]
    public void KeepsDeclaredTimeZone()
    {
        var doc = XmltvParser.ParseText(TestData.Epg);

        var start = doc.Programmes[0].Start;
        Assert.Equal(TimeSpan.FromHours(3), start.Offset);
        Assert.Equal(new DateTime(2026, 9, 22, 18, 30, 0), start.DateTime);
    }

    [Theory]
    [InlineData("20260922183000 +0300", 2026, 9, 22, 18, 30, 0, 3)]
    [InlineData("202609221830 +0300", 2026, 9, 22, 18, 30, 0, 3)]      // без секунд
    [InlineData("20260922183000+0300", 2026, 9, 22, 18, 30, 0, 3)]     // без пробела
    [InlineData("20260922183000 Z", 2026, 9, 22, 18, 30, 0, 0)]
    [InlineData("20260922 +0000", 2026, 9, 22, 0, 0, 0, 0)]            // только дата
    public void ParsesDateForms(string input, int y, int mo, int d, int h, int mi, int s, int offsetHours)
    {
        Assert.True(XmltvParser.TryParseXmltvDate(input, out var result));
        Assert.Equal(new DateTime(y, mo, d, h, mi, s), result.DateTime);
        Assert.Equal(TimeSpan.FromHours(offsetHours), result.Offset);
    }

    [Fact]
    public void DateWithoutOffsetUsesLocalTime()
    {
        Assert.True(XmltvParser.TryParseXmltvDate("20260922183000", out var result));

        var expected = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 22, 18, 30, 0));
        Assert.Equal(expected, result.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("не дата")]
    [InlineData("2026")]
    public void RejectsBadDates(string? input)
        => Assert.False(XmltvParser.TryParseXmltvDate(input, out _));

    [Fact]
    public async Task ReadsGzippedStream()
    {
        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(TestData.EpgBytes);
        }
        compressed.Seek(0, SeekOrigin.Begin);

        var doc = await XmltvParser.ParseAsync(compressed);

        Assert.Equal(3, doc.Programmes.Count);
    }

    [Fact]
    public async Task ReadsPlainStream()
    {
        using var plain = new MemoryStream(TestData.EpgBytes);

        var doc = await XmltvParser.ParseAsync(plain);

        Assert.Equal(3, doc.Programmes.Count);
    }

    [Fact]
    public void ProgrammeWithoutChannelIsSkipped()
    {
        const string xml = """
            <tv>
              <programme start="20260922183000 +0300" stop="20260922193000 +0300"><title>Нет канала</title></programme>
              <programme start="20260922183000 +0300" stop="20260922193000 +0300" channel="1"><title>Есть</title></programme>
            </tv>
            """;

        var doc = XmltvParser.ParseText(xml);

        Assert.Single(doc.Programmes);
        Assert.Equal("Есть", doc.Programmes[0].Title);
    }

    [Fact]
    public void DetectsGzipMagicBytes()
    {
        Assert.True(XmltvParser.LooksGzipped(new byte[] { 0x1f, 0x8b, 0x08 }));
        Assert.False(XmltvParser.LooksGzipped("<?x"u8.ToArray()));
    }
}
