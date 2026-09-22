using IptvPlayer.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IptvPlayer.Core.Services;

/// <summary>Отрезок буфера: файл и время, когда его начали писать.</summary>
public sealed record TimeshiftSegment(string Path, DateTimeOffset StartedAt)
{
    public long Size => File.Exists(Path) ? new FileInfo(Path).Length : 0;
}

/// <summary>
/// Пишет текущий канал в кольцевой буфер на диске, чтобы эфир можно было отмотать назад.
/// Поток берётся отдельным соединением, поэтому просмотр остаётся нетронутым.
/// Буфер режется на отрезки: старые удаляются, как только выходят за выбранную глубину.
/// </summary>
public sealed class TimeshiftService : IDisposable
{
    private const string BufferFolder = "timeshift";

    /// <summary>
    /// Длина отрезка. Меньше отрезок — точнее отмотка, но больше файлов;
    /// пять минут держат баланс и дают разумный шаг перемотки.
    /// </summary>
    public static readonly TimeSpan SegmentLength = TimeSpan.FromMinutes(5);

    private readonly string _folder;
    private readonly StreamRecorder _recorder;
    private readonly ILogger<TimeshiftService> _log;

    private readonly List<TimeshiftSegment> _segments = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public TimeshiftService(JsonStore store, StreamRecorder recorder, ILogger<TimeshiftService>? log = null)
    {
        _folder = Path.Combine(store.RootFolder, BufferFolder);
        _recorder = recorder;
        _log = log ?? NullLogger<TimeshiftService>.Instance;

        Directory.CreateDirectory(_folder);
        ClearFolder();
    }

    /// <summary>Глубина буфера. Ниже одного отрезка опускаться незачем.</summary>
    public TimeSpan Depth { get; set; } = TimeSpan.FromMinutes(30);

    public Channel? Channel { get; private set; }

    public bool IsRunning => _worker is { IsCompleted: false };

    /// <summary>Накопленная глубина: от начала самого старого отрезка до сейчас.</summary>
    public TimeSpan Available => _segments.Count == 0
        ? TimeSpan.Zero
        : DateTimeOffset.Now - _segments[0].StartedAt;

    public IReadOnlyList<TimeshiftSegment> Segments => _segments;

    public event EventHandler? Updated;

    /// <summary>Начинает писать канал, прежний буфер выбрасывается.</summary>
    public void Start(Channel channel, string streamUrl)
    {
        Stop();

        Channel = channel;
        _cts = new CancellationTokenSource();
        _worker = RunAsync(streamUrl, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _worker = null;

        Channel = null;
        _segments.Clear();
        ClearFolder();

        Updated?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync(string streamUrl, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var segment = new TimeshiftSegment(
                Path.Combine(_folder, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}.ts"),
                DateTimeOffset.Now);

            _segments.Add(segment);
            Trim();
            Updated?.Invoke(this, EventArgs.Empty);

            using var segmentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            segmentCts.CancelAfter(SegmentLength);

            try
            {
                await _recorder.RecordAsync(streamUrl, segment.Path, progress: null, segmentCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // отрезок закончился по времени либо буфер остановили
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Буфер отмотки прервался, пробуем снова");
                try { await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Отрезок, содержащий указанный момент, и смещение внутри него.</summary>
    public (TimeshiftSegment Segment, TimeSpan Offset)? Locate(DateTimeOffset moment)
    {
        for (var i = _segments.Count - 1; i >= 0; i--)
        {
            var segment = _segments[i];
            if (segment.StartedAt > moment) continue;
            if (segment.Size == 0) continue;

            return (segment, moment - segment.StartedAt);
        }

        return null;
    }

    /// <summary>Самый старый момент, который ещё можно посмотреть.</summary>
    public DateTimeOffset? Earliest => _segments.Count == 0 ? null : _segments[0].StartedAt;

    private void Trim()
    {
        var limit = DateTimeOffset.Now - Depth - SegmentLength;

        while (_segments.Count > 1 && _segments[0].StartedAt < limit)
        {
            var old = _segments[0];
            _segments.RemoveAt(0);

            try
            {
                if (File.Exists(old.Path)) File.Delete(old.Path);
            }
            catch (IOException ex)
            {
                _log.LogDebug(ex, "Отрезок буфера занят: {Path}", old.Path);
            }
        }
    }

    private void ClearFolder()
    {
        foreach (var file in Directory.EnumerateFiles(_folder, "*.ts"))
        {
            try { File.Delete(file); }
            catch (IOException) { /* файл ещё играет — удалится при следующем запуске */ }
        }
    }

    public void Dispose() => Stop();
}
