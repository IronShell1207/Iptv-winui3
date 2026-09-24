namespace IptvPlayer.Core.Models;

public enum StreamSourceMode
{
    /// <summary>Через HTTP-прокси udpxy (по умолчанию).</summary>
    Udpxy,
    /// <summary>Прямой мультикаст udp://@… (только проводная сеть).</summary>
    DirectMulticast,
}

/// <summary>Настройки приложения (settings.json).</summary>
public sealed record AppSettings
{
    public const string DefaultPlaylistUrl = "http://192.168.3.1/iptv.m3u";
    public const string DefaultEpgUrl = "http://streamer.mkpnet.ru/epg_service.xml";
    public const string DefaultUdpxyBaseUrl = "http://192.168.3.1:4022";

    public string UdpxyBaseUrl { get; init; } = DefaultUdpxyBaseUrl;
    public StreamSourceMode SourceMode { get; init; } = StreamSourceMode.Udpxy;

    /// <summary>Громкость 0..100.</summary>
    public int Volume { get; init; } = 80;
    public bool IsMuted { get; init; }
    public bool ResumeLastChannel { get; init; } = true;

    /// <summary>Буфер упреждающего чтения, мс.</summary>
    public int BufferMilliseconds { get; init; } = 1500;
    public bool HardwareDecoding { get; init; } = true;

    /// <summary>Режим вписывания кадра, выбранный кнопкой в плеере.</summary>
    public VideoFitMode VideoFit { get; init; } = VideoFitMode.Fit;
    public bool DownmixToStereo { get; init; } = true;

    /// <summary>Папка для записей эфира. Пусто — подпапка в «Видео».</summary>
    public string? RecordingsFolder { get; init; }

    /// <summary>Запас после конца передачи при записи по телепрограмме, минут.</summary>
    public int RecordingPaddingMinutes { get; init; } = 3;

    /// <summary>На паузе продолжать писать эфир, чтобы продолжить с того же места.</summary>
    public bool PauseCacheEnabled { get; init; } = true;

    /// <summary>Сколько эфира копить на паузе, минут.</summary>
    public int PauseCacheMinutes { get; init; } = 30;

    /// <summary>Писать эфир в буфер, чтобы его можно было отмотать назад.</summary>
    public bool TimeshiftEnabled { get; init; }

    /// <summary>Глубина буфера отмотки, минут.</summary>
    public int TimeshiftMinutes { get; init; } = 30;

    /// <summary>Время жизни кэша EPG, часов.</summary>
    public int EpgCacheHours { get; init; } = 6;
    public string LogLevel { get; init; } = "Information";
    public string Language { get; init; } = "ru";
}
