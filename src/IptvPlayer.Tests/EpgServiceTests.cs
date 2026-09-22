using IptvPlayer.Core.Parsing;
using IptvPlayer.Core.Services;
using Xunit;

namespace IptvPlayer.Tests;

public class EpgServiceTests
{
    private static readonly TimeSpan Msk = TimeSpan.FromHours(3);

    private static EpgService CreateLoaded()
    {
        var root = Path.Combine(Path.GetTempPath(), "iptv-tests", Guid.NewGuid().ToString("N"));
        var service = new EpgService(new JsonStore(root), new HttpClient());
        service.Apply(XmltvParser.ParseText(TestData.Epg));
        return service;
    }

    private static DateTimeOffset At(int hour, int minute)
        => new(2026, 9, 22, hour, minute, 0, Msk);

    [Fact]
    public void FindsCurrentProgramme()
    {
        var epg = CreateLoaded();

        var now = epg.NowOn("000000538", At(19, 0));

        Assert.NotNull(now);
        Assert.Equal("Новости", now.Title);
    }

    [Fact]
    public void ProgrammeBoundaryBelongsToNextProgramme()
    {
        var epg = CreateLoaded();

        // 19:30 — момент Stop «Новостей» и Start «Вечернего эфира»
        var now = epg.NowOn("000000538", At(19, 30));

        Assert.NotNull(now);
        Assert.Equal("Вечерний эфир", now.Title);
    }

    [Fact]
    public void NothingAiringOutsideSchedule()
    {
        var epg = CreateLoaded();

        Assert.Null(epg.NowOn("000000538", At(9, 0)));
        Assert.Null(epg.NowOn("000000538", At(23, 59)));
    }

    [Fact]
    public void FindsNextProgramme()
    {
        var epg = CreateLoaded();

        var next = epg.NextOn("000000538", At(19, 0));

        Assert.NotNull(next);
        Assert.Equal("Вечерний эфир", next.Title);
    }

    [Fact]
    public void UnknownOrMissingChannelIdYieldsNothing()
    {
        var epg = CreateLoaded();

        Assert.Empty(epg.ForChannel("нет-такого"));
        Assert.Empty(epg.ForChannel(null));
        Assert.Null(epg.NowOn(null, At(19, 0)));
        Assert.Null(epg.NextOn("нет-такого", At(19, 0)));
    }

    [Fact]
    public void DayScheduleIncludesOverlappingProgrammes()
    {
        var epg = CreateLoaded();

        var day = epg.DaySchedule("000000538", new DateTimeOffset(2026, 9, 22, 0, 0, 0, Msk));

        Assert.Equal(2, day.Count);
        Assert.Equal("Новости", day[0].Title);
    }

    [Fact]
    public void DescribesChannelFromXmltv()
    {
        var epg = CreateLoaded();

        Assert.Equal("Россия HD", epg.Describe("000000537")?.DisplayName);
        Assert.Null(epg.Describe("000000999"));
    }

    [Fact]
    public void ProgressIsClampedToUnitRange()
    {
        var epg = CreateLoaded();
        var news = epg.ForChannel("000000538")[0];

        Assert.Equal(0.5, news.ProgressAt(At(19, 0)), 3);
        Assert.Equal(0, news.ProgressAt(At(17, 0)));
        Assert.Equal(1, news.ProgressAt(At(21, 0)));
    }
}
