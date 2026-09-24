using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IptvPlayer.Core.Models;
using IptvPlayer.Core.Services;
using IptvPlayer.Services;
using Microsoft.UI.Dispatching;
using Windows.Media.Playback;

namespace IptvPlayer.ViewModels;

/// <summary>
/// Экран плеера: канал, текущая передача, состояние воспроизведения,
/// автоскрытие панелей и ввод номера канала.
/// </summary>
public sealed partial class PlayerViewModel : ObservableObject
{
    private const int AutoHideSeconds = 3;

    /// <summary>Полоса у левого края, притягивающая панель каналов.</summary>
    private const double HoverEdge = 32;

    /// <summary>Ширина панели каналов вместе с отступом.</summary>
    private const double PanelWidth = 460;
    private const int NumberInputMs = 1200;

    private readonly PlaybackService _playback;
    private readonly ChannelsViewModel _channels;
    private readonly EpgService _epg;
    private readonly SettingsService _settings;
    private readonly RecordingService _recordings;
    private readonly TimeshiftService _timeshift;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private DispatcherQueueTimer? _hideTimer;
    private DispatcherQueueTimer? _tickTimer;
    private DispatcherQueueTimer? _numberTimer;
    private DispatcherQueueTimer? _toastTimer;
    private DispatcherQueueTimer? _volumeOsdTimer;
    private ChannelItemViewModel? _previousChannel;
    private string _numberBuffer = string.Empty;
    private bool _pointerOverChrome;

    public PlayerViewModel(
        PlaybackService playback,
        ChannelsViewModel channels,
        EpgService epg,
        SettingsService settings,
        RecordingService recordings,
        TimeshiftService timeshift)
    {
        _playback = playback;
        _channels = channels;
        _epg = epg;
        _settings = settings;
        _recordings = recordings;
        _timeshift = timeshift;

        _recordings.Changed += (_, _) => _dispatcher.TryEnqueue(RefreshRecordingState);
        _playback.FileEnded += (_, _) => _dispatcher.TryEnqueue(() => _ = GoLiveAsync());
        _playback.StatusChanged += OnPlaybackStatusChanged;
        _playback.Player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;

        Volume = _settings.Current.Volume;
        IsMuted = _settings.Current.IsMuted;
        VideoFit = _settings.Current.VideoFit;

        StartTimers();
    }

    public MediaPlayer Player => _playback.Player;

    public ChannelsViewModel Channels => _channels;

    [ObservableProperty]
    public partial ChannelItemViewModel? Current { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool IsBuffering { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool AreControlsVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsChannelPanelOpen { get; set; }

    /// <summary>Панель, открытая кнопкой, остаётся до повторного нажатия.</summary>
    [ObservableProperty]
    public partial bool IsChannelPanelPinned { get; set; }

    [ObservableProperty]
    public partial bool IsFullScreen { get; set; }

    [ObservableProperty]
    public partial bool IsCompactOverlay { get; set; }

    [ObservableProperty]
    public partial double Volume { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial string? NumberOsd { get; set; }

    /// <summary>Как кадр вписывается в окно.</summary>
    [ObservableProperty]
    public partial VideoFitMode VideoFit { get; set; }

    /// <summary>Короткая плашка в углу: показывает, что именно переключили.</summary>
    [ObservableProperty]
    public partial string? Toast { get; set; }

    /// <summary>Идёт запись текущего канала.</summary>
    [ObservableProperty]
    public partial bool IsRecording { get; set; }

    /// <summary>Сколько уже пишется — подпись рядом с красной точкой.</summary>
    [ObservableProperty]
    public partial string? RecordingCaption { get; set; }

    /// <summary>Эфир отмотан назад и играет из буфера.</summary>
    [ObservableProperty]
    public partial bool IsTimeshifted { get; set; }

    /// <summary>Насколько мы отстаём от эфира.</summary>
    [ObservableProperty]
    public partial string? TimeshiftCaption { get; set; }

    /// <summary>Шкала громкости у правого края — при изменении с клавиатуры и кнопок.</summary>
    [ObservableProperty]
    public partial bool IsVolumeOsdVisible { get; set; }

    /// <summary>Громкость в процентах для подписи на шкале.</summary>
    public int VolumePercent => IsMuted ? 0 : (int)Math.Round(Volume);

    [ObservableProperty]
    public partial string ClockTime { get; set; } = DateTime.Now.ToString("HH:mm");

    [ObservableProperty]
    public partial string ClockDate { get; set; } =
        DateTime.Now.ToString("ddd, d MMM", System.Globalization.CultureInfo.CurrentCulture);

    // --- текущая и следующая передача ---

    [ObservableProperty]
    public partial string? NowTitle { get; set; }

    [ObservableProperty]
    public partial string? NowStart { get; set; }

    [ObservableProperty]
    public partial string? NowStop { get; set; }

    [ObservableProperty]
    public partial double NowProgress { get; set; }

    [ObservableProperty]
    public partial string? NextTitle { get; set; }

    [ObservableProperty]
    public partial string? NextTimeRange { get; set; }

    public string ChannelName => Current?.Name ?? RecordingTitle ?? "Канал не выбран";

    /// <summary>Идёт запись из файла, а не эфир.</summary>
    public bool IsRecordingPlayback => RecordingTitle is not null;

    public string? ChannelGroup => Current?.Group;

    public bool HasError => ErrorMessage is not null;

    public bool HasEpg => NowTitle is not null;

    public bool HasNext => NextTitle is not null;

    public bool IsFavorite => Current?.IsFavorite ?? false;

    /// <summary>Запрос к окну: включить/выключить полноэкранный режим или PiP.</summary>
    public event EventHandler<bool>? FullScreenRequested;

    public event EventHandler<bool>? CompactOverlayRequested;

    /// <summary>Пользователь закрыл плеер — вернуться к списку.</summary>
    public event EventHandler? CloseRequested;

    partial void OnRecordingTitleChanged(string? value)
    {
        OnPropertyChanged(nameof(ChannelName));
        OnPropertyChanged(nameof(IsRecordingPlayback));
    }

    partial void OnCurrentChanged(ChannelItemViewModel? value)
    {
        OnPropertyChanged(nameof(ChannelName));
        OnPropertyChanged(nameof(ChannelGroup));
        OnPropertyChanged(nameof(IsFavorite));
        UpdateEpg();
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnNowTitleChanged(string? value) => OnPropertyChanged(nameof(HasEpg));

    partial void OnNextTitleChanged(string? value) => OnPropertyChanged(nameof(HasNext));

    partial void OnVolumeChanged(double value)
    {
        _playback.Volume = value;
        OnPropertyChanged(nameof(VolumePercent));
    }

    partial void OnIsMutedChanged(bool value)
    {
        _playback.IsMuted = value;
        OnPropertyChanged(nameof(VolumePercent));
    }

    partial void OnVideoFitChanged(VideoFitMode value)
    {
        _ = _settings.UpdateAsync(s => s with { VideoFit = value });
        ShowToast(DescribeFit(value));
    }

    partial void OnIsFullScreenChanged(bool value) => FullScreenRequested?.Invoke(this, value);

    partial void OnIsCompactOverlayChanged(bool value) => CompactOverlayRequested?.Invoke(this, value);

    /// <summary>Открывает сохранённую запись: у файла есть длительность и перемотка.</summary>
    public async Task PlayRecordingAsync(Recording recording)
    {
        Current = null;
        IsTimeshifted = false;
        TimeshiftCaption = null;
        ErrorMessage = null;
        RecordingTitle = recording.Title;

        _timeshift.Stop();
        ShowControls();

        await _playback.PlayFileAsync(recording.FilePath);
    }

    /// <summary>Название открытой записи, если сейчас играет не эфир.</summary>
    [ObservableProperty]
    public partial string? RecordingTitle { get; set; }

    /// <summary>Открыть канал в плеере.</summary>
    public async Task PlayAsync(ChannelItemViewModel item)
    {
        if (Current is { } previous && !ReferenceEquals(previous, item))
            _previousChannel = previous;

        Current = item;
        RecordingTitle = null;
        ErrorMessage = null;
        StatusMessage = "Подключение…";
        ShowControls();

        foreach (var c in _channels.AllChannels) c.IsPlaying = false;
        item.IsPlaying = true;

        await _settings.UpdateSessionAsync(s => s with
        {
            LastPlaylistId = _channels.CurrentPlaylist?.Id,
            LastChannelKey = item.Key,
        });

        await _playback.PlayAsync(item.Channel);

        StartTimeshiftBuffer(item);
        RefreshRecordingState();
    }

    /// <summary>
    /// Буфер отмотки пишется вторым соединением, поэтому включается только
    /// когда его попросили в настройках: иначе роутер зря отдаёт поток дважды.
    /// </summary>
    private void StartTimeshiftBuffer(ChannelItemViewModel item)
    {
        IsTimeshifted = false;
        TimeshiftCaption = null;

        if (!_settings.Current.TimeshiftEnabled)
        {
            _timeshift.Stop();
            return;
        }

        _timeshift.Depth = TimeSpan.FromMinutes(Math.Clamp(_settings.Current.TimeshiftMinutes, 5, 240));
        _timeshift.Start(item.Channel, ResolveUrl(item.Channel));
    }

    private string ResolveUrl(Channel channel) => StreamUrlResolver.Resolve(
        channel.Url, _settings.Current.SourceMode, _settings.Current.UdpxyBaseUrl);

    /// <summary>
    /// Пауза живого эфира. Поток продолжает писаться в буфер, поэтому продолжаем
    /// ровно с того места, где остановились, а не с обрыва.
    /// </summary>
    [RelayCommand]
    private async Task TogglePlayPause()
    {
        ShowControls();

        if (IsPlaying)
        {
            PauseLive();
            return;
        }

        await ResumeAsync();
    }

    private void PauseLive()
    {
        _playback.Pause();

        // у записи и так есть перемотка, буфер ей не нужен
        if (IsRecordingPlayback || Current is null) return;

        _pausedAt = CurrentAirTime;

        if (_timeshift.IsRunning)
        {
            PauseCaption = $"Пауза · эфир пишется с {_pausedAt:HH:mm:ss}";
            return;
        }

        if (!_settings.Current.PauseCacheEnabled)
        {
            _pausedAt = null;
            PauseCaption = "Пауза";
            return;
        }

        // буфер поднят только ради паузы: после возврата в эфир его выключим
        _bufferOwnedByPause = true;
        _timeshift.Depth = TimeSpan.FromMinutes(Math.Clamp(_settings.Current.PauseCacheMinutes, 5, 240));
        _timeshift.Start(Current.Channel, ResolveUrl(Current.Channel));

        PauseCaption = $"Пауза · эфир пишется с {_pausedAt:HH:mm:ss}";
    }

    private async Task ResumeAsync()
    {
        PauseCaption = null;

        if (_pausedAt is not { } paused || !_timeshift.IsRunning)
        {
            _playback.Resume();
            return;
        }

        var resumeFrom = TimeshiftService.ResumePoint(paused, _timeshift.Earliest, DateTimeOffset.Now);
        var located = _timeshift.Locate(resumeFrom);

        if (located is null)
        {
            // за паузу ничего не накопилось — возвращаемся в прямой эфир
            _playback.Resume();
            return;
        }

        await _playback.PlayFileAsync(located.Value.Segment.Path, located.Value.Offset);

        _timeshiftBase = located.Value.Segment.StartedAt;
        IsTimeshifted = true;
        UpdateTimeshiftCaption();

        if (resumeFrom > paused)
            ShowToast($"Пауза была долгой — продолжаем с {resumeFrom:HH:mm}");

        _pausedAt = null;
    }

    /// <summary>Подпись на паузе: видно, что эфир не потерян.</summary>
    [ObservableProperty]
    public partial string? PauseCaption { get; set; }

    private DateTimeOffset? _pausedAt;
    private bool _bufferOwnedByPause;

    [RelayCommand]
    private Task NextChannel() => SwitchAsync(+1);

    [RelayCommand]
    private Task PreviousChannel() => SwitchAsync(-1);

    private async Task SwitchAsync(int delta)
    {
        var next = _channels.Neighbour(Current, delta);
        if (next is not null) await PlayAsync(next);
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        ShowControls();
        ShowVolumeOsd();
    }

    /// <summary>Громкость меняется шагами по 5 — так удобнее держать клавишу.</summary>
    [RelayCommand]
    private void VolumeUp() => ChangeVolume(+5);

    [RelayCommand]
    private void VolumeDown() => ChangeVolume(-5);

    private void ChangeVolume(double delta)
    {
        if (IsMuted && delta > 0) IsMuted = false;

        Volume = Math.Clamp(Volume + delta, 0, 100);
        ShowControls();
        ShowVolumeOsd();
    }

    /// <summary>Показывает шкалу громкости и прячет её через пару секунд.</summary>
    public void ShowVolumeOsd()
    {
        IsVolumeOsdVisible = true;

        _volumeOsdTimer ??= CreateTimer(
            TimeSpan.FromSeconds(1.6), () => IsVolumeOsdVisible = false, repeat: false);
        _volumeOsdTimer.Stop();
        _volumeOsdTimer.Start();
    }

    /// <summary>Возврат к каналу, с которого только что ушли.</summary>
    [RelayCommand]
    private async Task LastChannel()
    {
        if (_previousChannel is { } previous) await PlayAsync(previous);
        else ShowToast("Предыдущего канала ещё нет");
    }

    [RelayCommand]
    private void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

    /// <summary>Перебирает режимы вписывания кадра по кругу.</summary>
    [RelayCommand]
    private void CycleVideoFit()
    {
        VideoFit = VideoFit switch
        {
            VideoFitMode.Fit => VideoFitMode.Crop,
            VideoFitMode.Crop => VideoFitMode.Stretch,
            VideoFitMode.Stretch => VideoFitMode.Original,
            _ => VideoFitMode.Fit,
        };

        ShowControls();
    }

    public static string DescribeFit(VideoFitMode mode) => mode switch
    {
        VideoFitMode.Fit => "По размеру окна",
        VideoFitMode.Crop => "Заполнить с обрезкой",
        VideoFitMode.Stretch => "Растянуть на всё окно",
        _ => "Оригинальный размер",
    };

    private void ShowToast(string message)
    {
        Toast = message;

        _toastTimer ??= CreateTimer(TimeSpan.FromSeconds(1.8), () => Toast = null, repeat: false);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    [RelayCommand]
    private void ToggleCompactOverlay() => IsCompactOverlay = !IsCompactOverlay;

    [RelayCommand]
    private void ToggleChannelPanel()
    {
        IsChannelPanelPinned = !IsChannelPanelPinned;
        IsChannelPanelOpen = IsChannelPanelPinned;
        ShowControls();
    }

    /// <summary>
    /// Движение мыши: у левого края панель каналов выезжает,
    /// при уходе правее — прячется, если её не закрепили кнопкой.
    /// </summary>
    public void UpdatePointer(double x)
    {
        if (x <= HoverEdge) IsChannelPanelOpen = true;
        else if (!IsChannelPanelPinned && x > PanelWidth) IsChannelPanelOpen = false;
    }

    [RelayCommand]
    private async Task ToggleFavorite()
    {
        if (Current is null) return;

        await _channels.ToggleFavoriteAsync(Current);
        OnPropertyChanged(nameof(IsFavorite));
    }

    /// <summary>Пишет канал, пока не остановят кнопкой.</summary>
    [RelayCommand]
    private void RecordManually()
    {
        if (Current is null) return;

        if (_recordings.ActiveFor(Current.Key) is { } active)
        {
            _recordings.Stop(active.Id);
            ShowToast("Запись остановлена");
            return;
        }

        var recording = _recordings.Start(
            Current.Channel, ResolveUrl(Current.Channel), NowTitle, stopsAt: null, RecordingMode.Manual);

        ShowToast($"Идёт запись: {recording.ChannelName}");
        RefreshRecordingState();
    }

    /// <summary>Пишет текущую передачу и останавливается сама по телепрограмме.</summary>
    [RelayCommand]
    private void RecordProgramme()
    {
        if (Current is null) return;

        var programme = _epg.NowOn(Current.TvgId, DateTimeOffset.Now);
        if (programme is null)
        {
            ShowToast("Для этого канала нет телепрограммы");
            return;
        }

        var deadline = RecordingService.DeadlineFor(programme, _settings.Current.RecordingPaddingMinutes);

        _recordings.Start(
            Current.Channel, ResolveUrl(Current.Channel), programme.Title, deadline, RecordingMode.Scheduled);

        ShowToast($"Запись до {deadline:HH:mm}: {programme.Title}");
        RefreshRecordingState();
    }

    private void RefreshRecordingState()
    {
        var active = _recordings.ActiveFor(Current?.Key);

        IsRecording = active is not null;
        RecordingCaption = active is null
            ? null
            : $@"{active.Duration:hh\:mm\:ss}  ·  {active.SizeBytes / 1024.0 / 1024.0:F0} МБ";
    }

    // --- отмотка назад ---

    public bool CanTimeshift => _timeshift.IsRunning;

    /// <summary>Отматывает эфир назад на полминуты.</summary>
    [RelayCommand]
    private Task SeekBack() => SeekRelativeAsync(TimeSpan.FromSeconds(-30));

    [RelayCommand]
    private Task SeekForward() => SeekRelativeAsync(TimeSpan.FromSeconds(30));

    private async Task SeekRelativeAsync(TimeSpan delta)
    {
        if (!_timeshift.IsRunning)
        {
            ShowToast("Отмотка выключена в настройках");
            return;
        }

        var target = CurrentAirTime + delta;

        // вперёд дальше эфира не уйти
        if (target >= DateTimeOffset.Now.AddSeconds(-2))
        {
            await GoLiveAsync();
            return;
        }

        if (_timeshift.Earliest is { } earliest && target < earliest) target = earliest;

        var located = _timeshift.Locate(target);
        if (located is null)
        {
            ShowToast("Буфер ещё не накопился");
            return;
        }

        await _playback.PlayFileAsync(located.Value.Segment.Path, located.Value.Offset);

        _timeshiftBase = located.Value.Segment.StartedAt;
        IsTimeshifted = true;
        UpdateTimeshiftCaption();
        ShowControls();
    }

    /// <summary>Возвращает к прямому эфиру.</summary>
    [RelayCommand]
    private async Task GoLive()
    {
        await GoLiveAsync();
    }

    private async Task GoLiveAsync()
    {
        if (Current is null) return;

        IsTimeshifted = false;
        TimeshiftCaption = null;
        _pausedAt = null;

        await _playback.PlayAsync(Current.Channel);

        // буфер, поднятый ради паузы, дальше не нужен
        if (_bufferOwnedByPause && !_settings.Current.TimeshiftEnabled)
        {
            _bufferOwnedByPause = false;
            _timeshift.Stop();
        }

        ShowControls();
    }

    /// <summary>Момент эфира, который сейчас на экране.</summary>
    private DateTimeOffset CurrentAirTime => IsTimeshifted
        ? _timeshiftBase + _playback.Player.PlaybackSession.Position
        : DateTimeOffset.Now;

    private DateTimeOffset _timeshiftBase;

    private void UpdateTimeshiftCaption()
    {
        if (!IsTimeshifted)
        {
            TimeshiftCaption = null;
            return;
        }

        var behind = DateTimeOffset.Now - CurrentAirTime;
        if (behind < TimeSpan.Zero) behind = TimeSpan.Zero;

        TimeshiftCaption = behind.TotalHours >= 1
            ? $@"−{behind:h\:mm\:ss}"
            : $@"−{behind:mm\:ss}";
    }

    [RelayCommand]
    private async Task Retry()
    {
        if (Current is not null) await PlayAsync(Current);
    }

    [RelayCommand]
    private void Close()
    {
        IsFullScreen = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        IsChannelPanelPinned = false;
        IsChannelPanelOpen = false;
        IsTimeshifted = false;
        TimeshiftCaption = null;
        PauseCaption = null;
        _pausedAt = null;
        _bufferOwnedByPause = false;
        _timeshift.Stop();

        _playback.Stop();
        Current = null;
        foreach (var c in _channels.AllChannels) c.IsPlaying = false;
    }

    // --- ввод номера канала цифрами ---

    public void AppendNumber(char digit)
    {
        if (!char.IsAsciiDigit(digit)) return;

        _numberBuffer += digit;
        if (_numberBuffer.Length > 4) _numberBuffer = _numberBuffer[^4..];

        NumberOsd = _numberBuffer;
        ShowControls();

        _numberTimer ??= CreateTimer(TimeSpan.FromMilliseconds(NumberInputMs), CommitNumber, repeat: false);
        _numberTimer.Stop();
        _numberTimer.Start();
    }

    private async void CommitNumber()
    {
        var buffer = _numberBuffer;
        _numberBuffer = string.Empty;
        NumberOsd = null;

        if (!int.TryParse(buffer, out var number)) return;

        var channel = _channels.ByNumber(number);
        if (channel is not null) await PlayAsync(channel);
        else StatusMessage = $"Канала с номером {number} нет";
    }

    // --- автоскрытие панелей ---

    /// <summary>Показать панели и перезапустить таймер автоскрытия.</summary>
    public void ShowControls()
    {
        AreControlsVisible = true;

        _hideTimer ??= CreateTimer(TimeSpan.FromSeconds(AutoHideSeconds), HideControls, repeat: false);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideControls()
    {
        // пока курсор лежит на панели, прятать её нельзя — человек ею пользуется
        if (_pointerOverChrome)
        {
            ShowControls();
            return;
        }

        AreControlsVisible = false;

        // закреплённая кнопкой панель остаётся на экране
        if (!IsChannelPanelPinned) IsChannelPanelOpen = false;
    }

    /// <summary>Курсор зашёл на панель или ушёл с неё.</summary>
    public void SetPointerOverChrome(bool isOver)
    {
        _pointerOverChrome = isOver;
        if (isOver) ShowControls();
    }

    private void StartTimers()
    {
        _tickTimer = CreateTimer(TimeSpan.FromSeconds(10), Tick, repeat: true);
        _tickTimer.Start();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        ClockTime = now.ToString("HH:mm");
        ClockDate = now.ToString("ddd, d MMM", System.Globalization.CultureInfo.CurrentCulture);
        UpdateEpg();
        RefreshRecordingState();
        UpdateTimeshiftCaption();
    }

    private void UpdateEpg()
    {
        var now = DateTimeOffset.Now;
        var current = _epg.NowOn(Current?.TvgId, now);
        var next = _epg.NextOn(Current?.TvgId, now);

        NowTitle = current?.Title;
        NowStart = current?.Start.ToLocalTime().ToString("HH:mm");
        NowStop = current?.Stop.ToLocalTime().ToString("HH:mm");
        NowProgress = current?.ProgressAt(now) ?? 0;

        NextTitle = next?.Title;
        NextTimeRange = next is null
            ? null
            : $"{next.Start.ToLocalTime():HH:mm} – {next.Stop.ToLocalTime():HH:mm}";
    }

    private DispatcherQueueTimer CreateTimer(TimeSpan interval, Action action, bool repeat)
    {
        var timer = _dispatcher.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = repeat;
        timer.Tick += (_, _) => action();
        return timer;
    }

    private void OnPlaybackStatusChanged(object? sender, PlaybackStatusChangedEventArgs e)
        => _dispatcher.TryEnqueue(() =>
        {
            StatusMessage = e.Message;

            switch (e.Status)
            {
                case PlaybackStatus.Failed:
                    ErrorMessage = e.Message ?? "Канал не отвечает";
                    IsBuffering = false;
                    IsPlaying = false;
                    break;

                case PlaybackStatus.Playing:
                    ErrorMessage = null;
                    StatusMessage = null;
                    IsBuffering = false;
                    IsPlaying = true;
                    UpdateEpg();
                    break;

                case PlaybackStatus.Opening:
                case PlaybackStatus.Buffering:
                    IsBuffering = true;
                    break;

                case PlaybackStatus.Paused:
                    IsPlaying = false;
                    IsBuffering = false;
                    break;

                case PlaybackStatus.Idle:
                    IsPlaying = false;
                    IsBuffering = false;
                    break;
            }
        });

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
        => _dispatcher.TryEnqueue(() =>
            IsPlaying = sender.PlaybackState == MediaPlaybackState.Playing);
}
