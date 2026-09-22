using IptvPlayer.Core.Models;
using IptvPlayer.Core.Services;
using Xunit;

namespace IptvPlayer.Tests;

public class StreamUrlResolverTests
{
    private const string UdpxyBase = "http://192.168.3.1:4022";

    [Theory]
    [InlineData("http://192.168.3.1:4022/udp/239.195.65.33:1234", "udp://@239.195.65.33:1234")]
    [InlineData("http://192.168.3.1:4022/rtp/239.195.65.33:1234", "rtp://@239.195.65.33:1234")]
    [InlineData("https://router.local/udp/224.0.0.1:5000", "udp://@224.0.0.1:5000")]
    public void UdpxyToMulticast(string input, string expected)
        => Assert.Equal(expected, StreamUrlResolver.ToMulticast(input));

    [Theory]
    [InlineData("udp://@239.195.65.33:1234", "http://192.168.3.1:4022/udp/239.195.65.33:1234")]
    [InlineData("udp://239.195.65.33:1234", "http://192.168.3.1:4022/udp/239.195.65.33:1234")]
    [InlineData("rtp://@239.195.65.33:1234", "http://192.168.3.1:4022/rtp/239.195.65.33:1234")]
    public void MulticastToUdpxy(string input, string expected)
        => Assert.Equal(expected, StreamUrlResolver.ToUdpxy(input, UdpxyBase));

    [Fact]
    public void RoundTripKeepsGroupAndPort()
    {
        const string original = "http://192.168.3.1:4022/udp/239.195.65.33:1234";

        var multicast = StreamUrlResolver.ToMulticast(original);
        var back = StreamUrlResolver.ToUdpxy(multicast, UdpxyBase);

        Assert.Equal(original, back);
    }

    [Fact]
    public void PlainHttpStreamIsLeftAlone()
    {
        const string url = "http://cdn.example.com/live/stream.m3u8";

        Assert.Equal(url, StreamUrlResolver.ToMulticast(url));
        Assert.Equal(url, StreamUrlResolver.ToUdpxy(url, UdpxyBase));
    }

    [Fact]
    public void UdpxyBaseWithoutSchemeIsNormalized()
        => Assert.Equal(
            "http://192.168.3.1:4022/udp/239.195.65.33:1234",
            StreamUrlResolver.ToUdpxy("udp://@239.195.65.33:1234", "192.168.3.1:4022/"));

    [Fact]
    public void EmptyUdpxyBaseLeavesMulticastUrl()
    {
        const string url = "udp://@239.195.65.33:1234";

        Assert.Equal(url, StreamUrlResolver.ToUdpxy(url, string.Empty));
    }

    [Fact]
    public void ResolveFollowsSelectedMode()
    {
        const string udpxy = "http://192.168.3.1:4022/udp/239.195.65.33:1234";

        Assert.Equal(udpxy,
            StreamUrlResolver.Resolve(udpxy, StreamSourceMode.Udpxy, UdpxyBase));
        Assert.Equal("udp://@239.195.65.33:1234",
            StreamUrlResolver.Resolve(udpxy, StreamSourceMode.DirectMulticast, UdpxyBase));
    }

    [Fact]
    public void DetectsUrlKind()
    {
        Assert.True(StreamUrlResolver.IsUdpxyUrl("http://192.168.3.1:4022/udp/239.195.65.33:1234"));
        Assert.False(StreamUrlResolver.IsUdpxyUrl("udp://@239.195.65.33:1234"));
        Assert.True(StreamUrlResolver.IsMulticastUrl("rtp://@239.195.65.33:1234"));
        Assert.False(StreamUrlResolver.IsMulticastUrl("http://cdn/live.m3u8"));
    }

    [Fact]
    public void ExtractsUdpxyBaseFromChannelUrl()
        => Assert.Equal("http://192.168.3.1:4022",
            StreamUrlResolver.ExtractUdpxyBase("http://192.168.3.1:4022/udp/239.195.65.33:1234"));
}
