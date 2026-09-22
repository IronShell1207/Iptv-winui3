using IptvPlayer.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IptvPlayer.Core.Services;

/// <summary>Запись изменилась: началась, обновился размер, завершилась.</summary>
public sealed record RecordingChangedEventArgs(Recording Recording);

/// <summary>
/// Ведёт записи эфира. Поток берётся отдельным соединением к udpxy,
/// поэтому запись не мешает просмотру и переживает переключение канала.
/// </summary>
public sealed class RecordingService
{
    private const string FileName = "recordings.json";

    private readonly JsonStore _store;
    private readonly StreamRecorder _recorder;
    private readonly ILogger<RecordingService> _log;

    private readonly Dictionary<string, CancellationTokenSource> _active = new(StringComparer.Ordinal);
    private readonly List<Recording> _recordings = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public RecordingService(JsonStore store, StreamRecorder recorder, ILogger<RecordingService>? log = null)
    {
        _store = store;
        _recorder = recorder;
        _log = log ?? NullLogger<RecordingService>.Instance;
    }

    /// <summary>Папка для файлов записей. Задаётся при загрузке настроек.</summary>
    public string OutputFolder { get; set; } = string.Empty;

    public IReadOnlyList<Recording> Recordings => _recordings;

    public bool HasActive => _active.Count > 0;

    public event EventHandler<RecordingChangedEventArgs>? Changed;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var saved = await _store.LoadAsync<List<Recording>>(FileName, ct).ConfigureAwait(false)
                    ?? new List<Recording>();

        _recordings.Clear();

        foreach (var recording in saved)
        {
            // запись, прерванная закрытием приложения, больше не идёт
            _recordings.Add(recording.State == RecordingState.Active
                ? recording with { State = RecordingState.Completed, StoppedAt = recording.StoppedAt ?? DateTimeOffset.Now }
                : recording);
        }

        await SaveAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Идёт ли запись этого канала.</summary>
    public bool IsRecording(string? channelKey)
        => channelKey is not null &&
           _recordings.Any(r => r.State == RecordingState.Active && r.ChannelKey == channelKey);

    public Recording? ActiveFor(string? channelKey)
        => channelKey is null
            ? null
            : _recordings.FirstOrDefault(r => r.State == RecordingState.Active && r.ChannelKey == channelKey);

    /// <summary>
    /// Начинает запись. Если задан <paramref name="stopsAt"/>, запись остановится сама —
    /// так работает режим «до конца передачи».
    /// </summary>
    public Recording Start(
        Channel channel,
        string streamUrl,
        string? programmeTitle,
        DateTimeOffset? stopsAt,
        RecordingMode mode)
    {
        if (string.IsNullOrWhiteSpace(OutputFolder))
            throw new InvalidOperationException("Папка для записей не задана.");

        var startedAt = DateTimeOffset.Now;
        var path = Path.Combine(
            OutputFolder,
            StreamRecorder.BuildFileName(startedAt, channel.Name, programmeTitle));

        var recording = new Recording
        {
            Id = Recording.NewId(),
            ChannelName = channel.Name,
            ChannelKey = channel.Key,
            ChannelUrl = channel.Url,
            ProgrammeTitle = programmeTitle,
            FilePath = path,
            StartedAt = startedAt,
            StopsAt = stopsAt,
            Mode = mode,
            State = RecordingState.Active,
        };

        _recordings.Insert(0, recording);

        var cts = new CancellationTokenSource();
        _active[recording.Id] = cts;

        if (stopsAt is { } deadline)
        {
            var delay = deadline - startedAt;
            if (delay > TimeSpan.Zero) cts.CancelAfter(delay);
            else cts.CancelAfter(TimeSpan.FromMinutes(1));
        }

        _ = RunAsync(recording, streamUrl, cts);

        Changed?.Invoke(this, new RecordingChangedEventArgs(recording));
        _ = SaveAsync();

        return recording;
    }

    private async Task RunAsync(Recording recording, string streamUrl, CancellationTokenSource cts)
    {
        var progress = new Progress<long>(bytes => Update(recording.Id, r => r with { SizeBytes = bytes }));

        try
        {
            _log.LogInformation("Запись {Title} началась: {Path}", recording.Title, recording.FilePath);

            var bytes = await _recorder
                .RecordAsync(streamUrl, recording.FilePath, progress, cts.Token)
                .ConfigureAwait(false);

            Update(recording.Id, r => r with
            {
                State = RecordingState.Completed,
                StoppedAt = DateTimeOffset.Now,
                SizeBytes = bytes,
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Запись {Title} прервалась", recording.Title);

            Update(recording.Id, r => r with
            {
                State = RecordingState.Failed,
                StoppedAt = DateTimeOffset.Now,
                Error = ex.Message,
            });
        }
        finally
        {
            _active.Remove(recording.Id);
            cts.Dispose();
            await SaveAsync().ConfigureAwait(false);
        }
    }

    public void Stop(string recordingId)
    {
        if (_active.TryGetValue(recordingId, out var cts)) cts.Cancel();
    }

    public void StopAll()
    {
        foreach (var cts in _active.Values.ToList()) cts.Cancel();
    }

    public async Task DeleteAsync(string recordingId, CancellationToken ct = default)
    {
        Stop(recordingId);

        var recording = _recordings.FirstOrDefault(r => r.Id == recordingId);
        if (recording is null) return;

        _recordings.Remove(recording);

        try
        {
            if (File.Exists(recording.FilePath)) File.Delete(recording.FilePath);
        }
        catch (IOException ex)
        {
            _log.LogWarning(ex, "Файл записи занят: {Path}", recording.FilePath);
        }

        await SaveAsync(ct).ConfigureAwait(false);
        Changed?.Invoke(this, new RecordingChangedEventArgs(recording));
    }

    /// <summary>Актуализирует размер файла для записей, завершённых ранее.</summary>
    public void RefreshSizes()
    {
        for (var i = 0; i < _recordings.Count; i++)
        {
            var recording = _recordings[i];
            if (!File.Exists(recording.FilePath)) continue;

            var size = new FileInfo(recording.FilePath).Length;
            if (size != recording.SizeBytes) _recordings[i] = recording with { SizeBytes = size };
        }
    }

    private void Update(string id, Func<Recording, Recording> mutate)
    {
        var index = _recordings.FindIndex(r => r.Id == id);
        if (index < 0) return;

        var updated = mutate(_recordings[index]);
        _recordings[index] = updated;

        Changed?.Invoke(this, new RecordingChangedEventArgs(updated));
    }

    private async Task SaveAsync(CancellationToken ct = default)
    {
        await _saveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _store.SaveAsync(FileName, _recordings.ToList(), ct).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    /// <summary>
    /// Когда остановить запись передачи: её конец по телепрограмме плюс запас
    /// на то, что эфир всегда немного съезжает.
    /// </summary>
    public static DateTimeOffset? DeadlineFor(EpgProgramme? programme, int paddingMinutes)
    {
        if (programme is null) return null;

        var deadline = programme.Stop.AddMinutes(Math.Max(0, paddingMinutes));
        return deadline <= DateTimeOffset.Now ? DateTimeOffset.Now.AddMinutes(1) : deadline;
    }
}
