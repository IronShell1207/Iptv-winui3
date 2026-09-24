using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IptvPlayer.Core.Models;
using IptvPlayer.Core.Services;
using IptvPlayer.Services;

namespace IptvPlayer.ViewModels;

/// <summary>Настройки приложения. Каждое изменение сразу пишется в settings.json.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly LogoCacheService _logos;
    private readonly PlaylistService _playlists;
    private readonly RecordingService _recordings;
    private bool _loading;

    public SettingsViewModel(
        SettingsService settings,
        LogoCacheService logos,
        PlaylistService playlists,
        RecordingService recordings)
    {
        _settings = settings;
        _logos = logos;
        _playlists = playlists;
        _recordings = recordings;

        Load();
    }

    [ObservableProperty]
    public partial string UdpxyBaseUrl { get; set; } = AppSettings.DefaultUdpxyBaseUrl;

    /// <summary>0 — через udpxy, 1 — прямой мультикаст.</summary>
    [ObservableProperty]
    public partial int SourceModeIndex { get; set; }

    [ObservableProperty]
    public partial int BufferMilliseconds { get; set; } = 1500;

    [ObservableProperty]
    public partial bool HardwareDecoding { get; set; } = true;

    [ObservableProperty]
    public partial bool DownmixToStereo { get; set; } = true;

    [ObservableProperty]
    public partial bool ResumeLastChannel { get; set; } = true;

    [ObservableProperty]
    public partial int EpgCacheHours { get; set; } = 6;

    [ObservableProperty]
    public partial string LogLevel { get; set; } = "Information";

    [ObservableProperty]
    public partial string RecordingsFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int RecordingPaddingMinutes { get; set; } = 3;

    [ObservableProperty]
    public partial bool PauseCacheEnabled { get; set; } = true;

    [ObservableProperty]
    public partial int PauseCacheMinutes { get; set; } = 30;

    [ObservableProperty]
    public partial bool TimeshiftEnabled { get; set; }

    [ObservableProperty]
    public partial int TimeshiftMinutes { get; set; } = 30;

    [ObservableProperty]
    public partial string? CheckResult { get; set; }

    [ObservableProperty]
    public partial bool IsChecking { get; set; }

    public string[] LogLevels { get; } = ["Trace", "Debug", "Information", "Warning", "Error"];

    public string DataFolder => AppPaths.Root;

    public string LogFolder => AppPaths.Logs;

    public string MulticastHint =>
        "Прямой мультикаст работает только на проводном подключении и требует IGMP. "
        + "По Wi-Fi HD-каналы рассыпаются — оставьте режим через udpxy.";

    private void Load()
    {
        _loading = true;
        var s = _settings.Current;

        UdpxyBaseUrl = s.UdpxyBaseUrl;
        SourceModeIndex = s.SourceMode == StreamSourceMode.DirectMulticast ? 1 : 0;
        BufferMilliseconds = s.BufferMilliseconds;
        HardwareDecoding = s.HardwareDecoding;
        DownmixToStereo = s.DownmixToStereo;
        ResumeLastChannel = s.ResumeLastChannel;
        EpgCacheHours = s.EpgCacheHours;
        LogLevel = s.LogLevel;
        RecordingsFolder = string.IsNullOrWhiteSpace(s.RecordingsFolder)
            ? AppPaths.DefaultRecordingsFolder
            : s.RecordingsFolder;
        RecordingPaddingMinutes = s.RecordingPaddingMinutes;
        PauseCacheEnabled = s.PauseCacheEnabled;
        PauseCacheMinutes = s.PauseCacheMinutes;
        TimeshiftEnabled = s.TimeshiftEnabled;
        TimeshiftMinutes = s.TimeshiftMinutes;

        _loading = false;
    }

    private void Save()
    {
        if (_loading) return;

        _ = _settings.UpdateAsync(s => s with
        {
            UdpxyBaseUrl = string.IsNullOrWhiteSpace(UdpxyBaseUrl)
                ? AppSettings.DefaultUdpxyBaseUrl
                : UdpxyBaseUrl.Trim(),
            SourceMode = SourceModeIndex == 1
                ? StreamSourceMode.DirectMulticast
                : StreamSourceMode.Udpxy,
            BufferMilliseconds = Math.Clamp(BufferMilliseconds, 200, 10_000),
            HardwareDecoding = HardwareDecoding,
            DownmixToStereo = DownmixToStereo,
            ResumeLastChannel = ResumeLastChannel,
            EpgCacheHours = Math.Clamp(EpgCacheHours, 1, 72),
            LogLevel = LogLevel,
            RecordingsFolder = RecordingsFolder,
            RecordingPaddingMinutes = Math.Clamp(RecordingPaddingMinutes, 0, 30),
            PauseCacheEnabled = PauseCacheEnabled,
            PauseCacheMinutes = Math.Clamp(PauseCacheMinutes, 5, 240),
            TimeshiftEnabled = TimeshiftEnabled,
            TimeshiftMinutes = Math.Clamp(TimeshiftMinutes, 5, 240),
        });

        _recordings.OutputFolder = RecordingsFolder;
    }

    partial void OnUdpxyBaseUrlChanged(string value) => Save();
    partial void OnSourceModeIndexChanged(int value) => Save();
    partial void OnBufferMillisecondsChanged(int value) => Save();
    partial void OnHardwareDecodingChanged(bool value) => Save();
    partial void OnDownmixToStereoChanged(bool value) => Save();
    partial void OnResumeLastChannelChanged(bool value) => Save();
    partial void OnEpgCacheHoursChanged(int value) => Save();
    partial void OnLogLevelChanged(string value) => Save();
    partial void OnRecordingsFolderChanged(string value) => Save();
    partial void OnRecordingPaddingMinutesChanged(int value) => Save();
    partial void OnPauseCacheEnabledChanged(bool value) => Save();
    partial void OnPauseCacheMinutesChanged(int value) => Save();
    partial void OnTimeshiftEnabledChanged(bool value) => Save();
    partial void OnTimeshiftMinutesChanged(int value) => Save();

    public string PauseCacheHint =>
        "На паузе эфир продолжает писаться на диск, поэтому после продолжения "
        + "вы увидите то, что пропустили, а не обрыв. Пока идёт пауза, канал "
        + "приходит с роутера вторым потоком.";

    public string TimeshiftHint =>
        "Буфер пишется вторым соединением к udpxy, поэтому канал идёт с роутера дважды. "
        + "На проводной сети это незаметно, по Wi-Fi лучше держать выключенным.";

    [RelayCommand]
    private async Task CheckUdpxyAsync()
    {
        IsChecking = true;
        CheckResult = null;
        try
        {
            var ok = await _playlists.CheckUdpxyAsync(UdpxyBaseUrl);
            CheckResult = ok
                ? "udpxy отвечает, всё в порядке."
                : "udpxy не отвечает. Проверьте, что вы в домашней сети и роутер включён.";
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private void ClearLogoCache()
    {
        _logos.Clear();
        CheckResult = "Кэш логотипов очищен.";
    }
}
