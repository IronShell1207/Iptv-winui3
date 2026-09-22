using System.Net;
using IptvPlayer.Core.Services;
using Xunit;

namespace IptvPlayer.Tests;

/// <summary>
/// Плейлист сначала показывается из кэша, а следом обновляется из сети,
/// поэтому загрузка телепрограммы запускается дважды почти одновременно.
/// </summary>
public class EpgLoadTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "iptv-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ParallelLoadsDoNotCollide()
    {
        var handler = new CountingHandler(TestData.EpgBytes);
        var epg = new EpgService(new JsonStore(_root), new HttpClient(handler));

        var tasks = Enumerable.Range(0, 4)
            .Select(_ => epg.LoadAsync("playlist", "http://epg.local/guide.xml", TimeSpan.FromHours(6)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, Assert.True);
        Assert.Equal(3, epg.ProgrammeCount);

        // первый вызов скачал файл, остальные обошлись свежим кэшем
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task FallsBackToCacheWhenSourceIsDown()
    {
        var root = new JsonStore(_root);
        var working = new EpgService(root, new HttpClient(new CountingHandler(TestData.EpgBytes)));
        await working.LoadAsync("playlist", "http://epg.local/guide.xml", TimeSpan.FromHours(6));

        var broken = new EpgService(root, new HttpClient(new FailingHandler()));
        var loaded = await broken.LoadAsync("playlist", "http://epg.local/guide.xml", TimeSpan.Zero);

        Assert.True(loaded);
        Assert.Equal(3, broken.ProgrammeCount);
    }

    [Fact]
    public async Task LeavesNoTemporaryFiles()
    {
        var epg = new EpgService(new JsonStore(_root), new HttpClient(new CountingHandler(TestData.EpgBytes)));

        await epg.LoadAsync("playlist", "http://epg.local/guide.xml", TimeSpan.Zero);
        await epg.LoadAsync("playlist", "http://epg.local/guide.xml", TimeSpan.Zero);

        var leftovers = Directory.GetFiles(Path.Combine(_root, "epg"), "*.tmp");
        Assert.Empty(leftovers);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* временная папка останется, это не мешает */ }
    }

    private sealed class CountingHandler(byte[] payload) : HttpMessageHandler
    {
        private int _requests;

        public int Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("источник недоступен");
    }
}
