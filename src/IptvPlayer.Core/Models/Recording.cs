namespace IptvPlayer.Core.Models;

public enum RecordingState
{
    /// <summary>Идёт запись.</summary>
    Active,
    /// <summary>Запись завершена и файл готов.</summary>
    Completed,
    /// <summary>Запись оборвалась: сеть пропала или диск не принял данные.</summary>
    Failed,
}

public enum RecordingMode
{
    /// <summary>Останавливается кнопкой.</summary>
    Manual,
    /// <summary>Останавливается сама, когда закончится передача по телепрограмме.</summary>
    Scheduled,
}

/// <summary>Запись эфира: описание лежит в recordings.json, сам поток — в .ts рядом.</summary>
public sealed record Recording
{
    public required string Id { get; init; }
    public required string ChannelName { get; init; }
    public string? ChannelKey { get; init; }
    public string? ChannelUrl { get; init; }

    /// <summary>Название передачи из телепрограммы, если оно было известно.</summary>
    public string? ProgrammeTitle { get; init; }

    public required string FilePath { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? StoppedAt { get; init; }

    /// <summary>Момент, когда запись должна остановиться сама.</summary>
    public DateTimeOffset? StopsAt { get; init; }

    public RecordingMode Mode { get; init; } = RecordingMode.Manual;
    public RecordingState State { get; init; } = RecordingState.Active;
    public long SizeBytes { get; init; }
    public string? Error { get; init; }

    public TimeSpan Duration => (StoppedAt ?? DateTimeOffset.Now) - StartedAt;

    public string Title => string.IsNullOrWhiteSpace(ProgrammeTitle)
        ? ChannelName
        : $"{ChannelName} — {ProgrammeTitle}";

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];
}
