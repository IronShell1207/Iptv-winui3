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
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private DispatcherQueueTimer? _hideTimer;
    private DispatcherQueueTimer? _tickTimer;
    private DispatcherQueueTimer? _numberTimer;
    private string _numberBuffer = string.Empty;
    private bool _pointerOverChrome;

    public PlayerViewModel(
        PlaybackService playback,
        ChannelsViewModel channels,
        EpgService epg,
        SettingsService settings)
    {
        _playback = playback;
        _channels = channels;
        _epg = epg;
        _settings = settings;

        _playback.StatusChanged += OnPlaybackStatusChanged;
        _playback.Player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;

        Volume = _settings.Current.Volume;
        IsMuted = _settings.Current.IsMuted;

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

    public string ChannelName => Current?.Name ?? "Канал не выбран";

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

    partial void OnVolumeChanged(double value) => _playback.Volume = value;

    partial void OnIsMutedChanged(bool value) => _playback.IsMuted = value;

    partial void OnIsFullScreenChanged(bool value) => FullScreenRequested?.Invoke(this, value);

    partial void OnIsCompactOverlayChanged(bool value) => CompactOverlayRequested?.Invoke(this, value);

    /// <summary>Открыть канал в плеере.</summary>
    public async Task PlayAsync(ChannelItemViewModel item)
    {
        Current = item;
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
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        _playback.TogglePlayPause();
        ShowControls();
    }

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
    }

    [RelayCommand]
    private void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

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
