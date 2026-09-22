using IptvPlayer.Core.Parsing;
using Xunit;

namespace IptvPlayer.Tests;

public class M3uParserTests
{
    [Fact]
    public void ReadsHeaderEpgUrl()
    {
        var doc = M3uParser.Parse(TestData.Playlist);

        Assert.Equal("http://streamer.mkpnet.ru/epg_service.xml", doc.EpgUrl);
        Assert.Equal("300", doc.HeaderAttributes["cache"]);
    }

    [Fact]
    public void ParsesAllChannelsWithSequentialNumbers()
    {
        var doc = M3uParser.Parse(TestData.Playlist);

        Assert.Equal(4, doc.Channels.Count);
        Assert.Equal([1, 2, 3, 4], doc.Channels.Select(c => c.Number));
    }

    [Fact]
    public void ParsesUnquotedAndQuotedAttributes()
    {
        var doc = M3uParser.Parse(TestData.Playlist);

        var first = doc.Channels[0];
        Assert.Equal("000000538", first.TvgId);          // без кавычек
        Assert.Equal("information", first.Group);        // в кавычках
        Assert.Equal("Первый канал HD", first.Name);
        Assert.Equal("http://192.168.3.1:4022/udp/239.195.65.33:1234", first.Url);
    }

    [Fact]
    public void KeepsCommasInsideChannelName()
    {
        var doc = M3uParser.Parse(TestData.Playlist);

        var match = doc.Channels.Single(c => c.TvgId == "000000101");
        Assert.Equal("Матч! Игра, повтор", match.Name);
        Assert.Equal("sports", match.Group);
        Assert.Equal("http://logo.local/match.png", match.LogoUrl);
    }

    [Fact]
    public void MissingLogoIsNull()
    {
        var doc = M3uParser.Parse(TestData.Playlist);

        Assert.Null(doc.Channels[0].LogoUrl);
    }

    [Fact]
    public void ChannelWithoutTvgIdUsesUrlAsKey()
    {
        var doc = M3uParser.Parse(TestData.Playlist);

        var carousel = doc.Channels.Single(c => c.Name == "Карусель");
        Assert.Null(carousel.TvgId);
        Assert.Equal("childrens", carousel.Group);
        Assert.Equal(carousel.Url, carousel.Key);
    }

    [Fact]
    public void HandlesCrLfAndBlankLines()
    {
        var text = "#EXTM3U\r\n\r\n#EXTINF:-1 tvg-id=1,Канал А\r\nhttp://a/1\r\n\r\n" +
                   "#EXTINF:-1 tvg-id=2,Канал Б\r\nhttp://b/2\r\n";

        var doc = M3uParser.Parse(text);

        Assert.Equal(2, doc.Channels.Count);
        Assert.Equal("Канал А", doc.Channels[0].Name);
        Assert.Equal("http://b/2", doc.Channels[1].Url);
    }

    [Fact]
    public void ExtGrpSetsGroupWhenAttributeMissing()
    {
        var text = "#EXTM3U\n#EXTINF:-1,Канал\n#EXTGRP:Музыка\nhttp://x/1\n";

        var doc = M3uParser.Parse(text);

        Assert.Equal("Музыка", doc.Channels[0].Group);
    }

    [Fact]
    public void UnknownAttributesArePreserved()
    {
        var text = "#EXTM3U\n#EXTINF:-1 tvg-id=5 catchup=\"shift\" " +
                   "catchup-source=\"http://cu/?t={start}\",Канал\nhttp://x/1\n";

        var doc = M3uParser.Parse(text);

        var attrs = doc.Channels[0].Attributes;
        Assert.Equal("shift", attrs["catchup"]);
        Assert.Equal("http://cu/?t={start}", attrs["catchup-source"]);
    }

    [Fact]
    public void FallsBackToTvgNameWhenTitleEmpty()
    {
        var text = "#EXTM3U\n#EXTINF:-1 tvg-name=\"Резервное имя\",\nhttp://x/1\n";

        var doc = M3uParser.Parse(text);

        Assert.Equal("Резервное имя", doc.Channels[0].Name);
    }

    [Fact]
    public void StripsUtf8Bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(System.Text.Encoding.UTF8.GetBytes("#EXTM3U\n#EXTINF:-1,Канал\nhttp://x/1\n"))
            .ToArray();

        var doc = M3uParser.ParseBytes(bytes);

        Assert.Single(doc.Channels);
        Assert.Equal("Канал", doc.Channels[0].Name);
    }

    [Fact]
    public void EmptyPlaylistYieldsNoChannels()
    {
        var doc = M3uParser.Parse("#EXTM3U\n");

        Assert.Empty(doc.Channels);
    }
}
