using IptvPlayer.Core.Models;
using IptvPlayer.Core.Services;
using Xunit;

namespace IptvPlayer.Tests;

public class RecordingTests
{
    private static readonly TimeSpan Msk = TimeSpan.FromHours(3);

    [Fact]
    public void FileNameCarriesDateChannelAndProgramme()
    {
        var name = StreamRecorder.BuildFileName(
            new DateTimeOffset(2026, 9, 22, 19, 5, 0, Msk), "Первый канал HD", "Новости");

        Assert.Equal("2026-09-22 19-05 Первый канал HD - Новости.ts", name);
    }

    [Fact]
    public void FileNameFallsBackToChannelWithoutProgramme()
    {
        var name = StreamRecorder.BuildFileName(
            new DateTimeOffset(2026, 9, 22, 19, 5, 0, Msk), "Матч ТВ", null);

        Assert.Equal("2026-09-22 19-05 Матч ТВ.ts", name);
    }

    [Fact]
    public void FileNameDropsCharactersForbiddenInPaths()
    {
        var name = StreamRecorder.BuildFileName(
            new DateTimeOffset(2026, 9, 22, 19, 5, 0, Msk), "ТВ/Центр", "Кто? Где: Когда");

        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name[10..]);   // двоеточий нет и в части с названием
        Assert.DoesNotContain('?', name);
        Assert.Equal(-1, name.IndexOf("  ", StringComparison.Ordinal));
    }

    [Fact]
    public void LongTitleIsTrimmedToFitFileSystem()
    {
        var name = StreamRecorder.BuildFileName(
            DateTimeOffset.Now, new string('Ы', 200), new string('Я', 200));

        Assert.True(name.Length < 120, $"имя длиной {name.Length} не влезает в разумный путь");
    }

    [Fact]
    public void DeadlineAddsPaddingAfterProgramme()
    {
        var programme = new EpgProgramme
        {
            ChannelId = "1",
            Start = DateTimeOffset.Now.AddMinutes(-10),
            Stop = DateTimeOffset.Now.AddMinutes(20),
            Title = "Передача",
        };

        var deadline = RecordingService.DeadlineFor(programme, 3);

        Assert.NotNull(deadline);
        Assert.Equal(programme.Stop.AddMinutes(3), deadline.Value);
    }

    [Fact]
    public void DeadlineForFinishedProgrammeStillLeavesTimeToRecord()
    {
        var programme = new EpgProgramme
        {
            ChannelId = "1",
            Start = DateTimeOffset.Now.AddHours(-3),
            Stop = DateTimeOffset.Now.AddHours(-2),
            Title = "Уже прошла",
        };

        var deadline = RecordingService.DeadlineFor(programme, 3);

        Assert.NotNull(deadline);
        Assert.True(deadline.Value > DateTimeOffset.Now, "запись должна успеть начаться");
    }

    [Fact]
    public void WithoutProgrammeThereIsNoDeadline()
        => Assert.Null(RecordingService.DeadlineFor(null, 3));

    [Fact]
    public void RecordingKnowsItsTitleAndDuration()
    {
        var started = DateTimeOffset.Now.AddMinutes(-15);
        var recording = new Recording
        {
            Id = "1",
            ChannelName = "Матч ТВ",
            ProgrammeTitle = "Футбол",
            FilePath = "x.ts",
            StartedAt = started,
            StoppedAt = started.AddMinutes(15),
        };

        Assert.Equal("Матч ТВ — Футбол", recording.Title);
        Assert.Equal(TimeSpan.FromMinutes(15), recording.Duration);
    }
}
