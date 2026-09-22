using FFmpegInteropX;
using IptvPlayer.Core.Models;
using IptvPlayer.Core.Services;
using Microsoft.Extensions.Logging;
using Windows.Media.Playback;
using Windows.System.Display;

namespace IptvPlayer.Services;

public enum PlaybackStatus
{
    Idle,
    Opening,
    Buffering,
    Playing,
    Paused,
    Failed,
}

public sealed record PlaybackStatusChangedEventArgs(PlaybackStatus Status, string? Message);

/// <summary>
/// Единственный на приложение MediaPlayer. При переключении канала меняется только Source,
/// старый FFmpegMediaSource освобождается строго после подмены.
/// </summary>
public sealed class PlaybackService : IDisposable
{
    private const int WatchdogSeconds = 7;
    private const int MaxAttempts = 3;

    private readonly SettingsService _settings;
    private readonly ILogger<PlaybackService> _log;
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private readonly DisplayRequest _displayRequest = new();

    private FFmpegMediaSource? _source;
    private CancellationTokenSource? _currentSwitch;
    private bool _displayRequested;
    private bool _disposed;

    public PlaybackService(SettingsService settings, ILogger<PlaybackService> log)
    {
        _settings = settings;
        _log = log;

        Player = new MediaPlayer
        {
            AutoPlay = true,
            Volume = Math.Clamp(settings.Current.Volume, 0, 100) / 100.0,
            IsMuted = settings.Current.IsMuted,
        };

        Player.MediaFailed += OnMediaFailed;
        Player.MediaOpened += OnMediaOpened;
        Player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
    }

    public MediaPlayer Player { get; }

    public Channel? CurrentChannel { get; private set; }

    public PlaybackStatus Status { get; private set; } = PlaybackStatus.Idle;

    public event EventHandler<PlaybackStatusChangedEventArgs>? StatusChanged;

    public double Volume
    {
        get => Player.Volume * 100;
        set
        {
            Player.Volume = Math.Clamp(value, 0, 100) / 100.0;
            _ = _settings.UpdateAsync(s => s with { Volume = (int)Math.Round(value) });
        }
    }

    public bool IsMuted
    {
        get => Player.IsMuted;
        set
        {
            Player.IsMuted = value;
            _ = _settings.UpdateAsync(s => s with { IsMuted = value });
        }
    }

    /// <summary>
    /// Открывает канал. Предыдущее переключение отменяется, вызовы сериализованы —
    /// быстрое перещёлкивание каналов безопасно.
    /// </summary>
    public async Task PlayAsync(Channel channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var previous = Interlocked.Exchange(ref _currentSwitch, null);
        previous?.Cancel();
        previous?.Dispose();

        var cts = new CancellationTokenSource();
        _currentSwitch = cts;

        await _switchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (cts.IsCancellationRequested) return;

            CurrentChannel = channel;
            var url = StreamUrlResolver.Resolve(
                channel.Url, _settings.Current.SourceMode, _settings.Current.UdpxyBaseUrl);

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (cts.IsCancellationRequested) return;

                SetStatus(PlaybackStatus.Opening, attempt == 1
                    ? null
                    : $"Повторное подключение ({attempt} из {MaxAttempts})…");

                try
                {
                    await OpenAsync(url, cts.Token).ConfigureAwait(false);

                    if (await WaitForFirstFrameAsync(cts.Token).ConfigureAwait(false))
                    {
                        _log.LogInformation("Канал {Name} открыт ({Url})", channel.Name, url);
                        return;
                    }

                    _log.LogWarning("Нет первого кадра за {Seconds} с: {Url}", WatchdogSeconds, url);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Не удалось открыть {Url} (попытка {Attempt})", url, attempt);
                    if (attempt == MaxAttempts)
                    {
                        Fail($"Канал не отвечает: {ex.Message}");
                        return;
                    }
                }
            }

            Fail("Канал не отвечает. Проверьте подключение к сети и доступность роутера.");
        }
        finally
        {
            _switchGate.Release();
        }
    }

    private async Task OpenAsync(string url, CancellationToken ct)
    {
        var settings = _settings.Current;

        var config = new MediaSourceConfig();
        config.Video.VideoDecoderMode = settings.HardwareDecoding
            ? VideoDecoderMode.Automatic
            : VideoDecoderMode.ForceSystemDecoder;
        config.Audio.DownmixAudioStreamsToStereo = settings.DownmixToStereo;
        config.General.ReadAheadBufferEnabled = true;
        config.General.ReadAheadBufferDuration =
            TimeSpan.FromMilliseconds(Math.Clamp(settings.BufferMilliseconds, 200, 10_000));
        config.General.SkipErrors = 50;

        // живой эфир: минимальная задержка и автопереподключение
        config.FFmpegOptions["fflags"] = "nobuffer";
        config.FFmpegOptions["flags"] = "low_delay";
        config.FFmpegOptions["reconnect"] = "1";
        config.FFmpegOptions["reconnect_streamed"] = "1";
        config.FFmpegOptions["reconnect_delay_max"] = "5";
        config.FFmpegOptions["timeout"] = "5000000";     // микросекунды
        config.FFmpegOptions["rw_timeout"] = "5000000";
        config.FFmpegOptions["user_agent"] = "IptvPlayer/1.0";

        var newSource = await FFmpegMediaSource.CreateFromUriAsync(url, config).AsTask(ct)
            .ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        var old = _source;
        _source = newSource;

        Player.Source = newSource.CreateMediaPlaybackItem();
        Player.Play();

        old?.Dispose();     // только после подмены Source, иначе падает нативный код
        RequestDisplayActive();
    }

    /// <summary>Watchdog: ждём первый кадр не дольше <see cref="WatchdogSeconds"/> секунд.</summary>
    private async Task<bool> WaitForFirstFrameAsync(CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(WatchdogSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var session = Player.PlaybackSession;
            if (session.NaturalVideoWidth > 0 &&
                session.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Paused)
            {
                return true;
            }

            if (Status == PlaybackStatus.Failed) return false;

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return false;
    }

    public void Pause()
    {
        Player.Pause();
        ReleaseDisplayActive();
    }

    public void Resume()
    {
        if (Player.Source is null) return;
        Player.Play();
        RequestDisplayActive();
    }

    public void TogglePlayPause()
    {
        if (Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) Pause();
        else Resume();
    }

    public void Stop()
    {
        Player.Pause();
        Player.Source = null;

        var old = _source;
        _source = null;
        old?.Dispose();

        CurrentChannel = null;
        ReleaseDisplayActive();
        SetStatus(PlaybackStatus.Idle, null);
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
        => SetStatus(PlaybackStatus.Playing, null);

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        _log.LogError("MediaFailed: {Error} {Message} (0x{Code:X8})",
            args.Error, args.ErrorMessage, args.ExtendedErrorCode?.HResult ?? 0);
        Fail(string.IsNullOrWhiteSpace(args.ErrorMessage)
            ? $"Ошибка воспроизведения: {args.Error}"
            : args.ErrorMessage);
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        var status = sender.PlaybackState switch
        {
            MediaPlaybackState.Opening => PlaybackStatus.Opening,
            MediaPlaybackState.Buffering => PlaybackStatus.Buffering,
            MediaPlaybackState.Playing => PlaybackStatus.Playing,
            MediaPlaybackState.Paused => PlaybackStatus.Paused,
            _ => Status,
        };

        if (Status != PlaybackStatus.Failed || status == PlaybackStatus.Playing)
            SetStatus(status, null);
    }

    private void Fail(string message) => SetStatus(PlaybackStatus.Failed, message);

    private void SetStatus(PlaybackStatus status, string? message)
    {
        Status = status;
        StatusChanged?.Invoke(this, new PlaybackStatusChangedEventArgs(status, message));
    }

    private void RequestDisplayActive()
    {
        if (_displayRequested) return;
        try
        {
            _displayRequest.RequestActive();
            _displayRequested = true;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "DisplayRequest.RequestActive не сработал");
        }
    }

    private void ReleaseDisplayActive()
    {
        if (!_displayRequested) return;
        try
        {
            _displayRequest.RequestRelease();
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "DisplayRequest.RequestRelease не сработал");
        }
        finally
        {
            _displayRequested = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _currentSwitch?.Cancel();
        _currentSwitch?.Dispose();

        Player.MediaFailed -= OnMediaFailed;
        Player.MediaOpened -= OnMediaOpened;
        Player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;

        ReleaseDisplayActive();

        Player.Source = null;
        _source?.Dispose();
        _source = null;
        Player.Dispose();
        _switchGate.Dispose();
    }
}
