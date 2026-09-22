using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IptvPlayer.Core.Models;
using IptvPlayer.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace IptvPlayer.ViewModels;

/// <summary>
/// Библиотека каналов текущего плейлиста: загрузка, поиск, группы, избранное, EPG.
/// Живёт как singleton — на неё смотрят и список каналов, и плеер.
/// </summary>
public sealed partial class ChannelsViewModel : ObservableObject
{
    public const string AllGroups = "Все";
    public const string FavoritesGroup = "Избранное";

    private readonly PlaylistService _playlists;
    private readonly EpgService _epg;
    private readonly FavoritesService _favorites;
    private readonly LogoCacheService _logos;
    private readonly SettingsService _settings;
    private readonly ILogger<ChannelsViewModel> _log;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private readonly List<ChannelItemViewModel> _all = new();
    private CancellationTokenSource? _loadCts;
    private DispatcherQueueTimer? _epgTimer;

    public ChannelsViewModel(
        PlaylistService playlists,
        EpgService epg,
        FavoritesService favorites,
        LogoCacheService logos,
        SettingsService settings,
        ILogger<ChannelsViewModel> log)
    {
        _playlists = playlists;
        _epg = epg;
        _favorites = favorites;
        _logos = logos;
        _settings = settings;
        _log = log;

        Groups.Add(AllGroups);
        SelectedGroup = AllGroups;

        StartEpgTimer();
    }

    public ObservableCollection<ChannelItemViewModel> Channels { get; } = new();

    public ObservableCollection<string> Groups { get; } = new();

    public IReadOnlyList<ChannelItemViewModel> AllChannels => _all;

    [ObservableProperty]
    public partial Playlist? CurrentPlaylist { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedGroup { get; set; } = AllGroups;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial ChannelItemViewModel? SelectedChannel { get; set; }

    public bool HasChannels => _all.Count > 0;

    public bool IsEmpty => !IsLoading && _all.Count == 0 && ErrorMessage is null;

    public bool HasError => ErrorMessage is not null;

    public string CountText => Channels.Count == _all.Count
        ? $"{_all.Count} каналов"
        : $"{Channels.Count} из {_all.Count}";

    /// <summary>Событие для UI: пользователь выбрал канал и надо открыть плеер.</summary>
    public event EventHandler<ChannelItemViewModel>? ChannelActivated;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedGroupChanged(string value) => ApplyFilter();

    /// <summary>
    /// Открывает плейлист: сначала показывает кэш, затем обновляет из сети.
    /// </summary>
    public async Task OpenAsync(Playlist playlist, bool forceRefresh = false)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        CurrentPlaylist = playlist;
        ErrorMessage = null;

        var cached = forceRefresh ? null : await _playlists.LoadFromCacheAsync(playlist, ct);
        if (cached is not null && !ct.IsCancellationRequested)
        {
            Apply(cached);
            _ = RefreshInBackgroundAsync(playlist, ct);
            return;
        }

        IsLoading = true;
        OnPropertyChanged(nameof(IsEmpty));
        try
        {
            var content = await _playlists.RefreshAsync(playlist, ct);
            if (ct.IsCancellationRequested) return;

            Apply(content);
        }
        catch (OperationCanceledException)
        {
            // открыли другой плейлист — это нормально
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Плейлист {Name} не загрузился", playlist.Name);
            ErrorMessage = Describe(ex);
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task RefreshInBackgroundAsync(Playlist playlist, CancellationToken ct)
    {
        IsRefreshing = true;
        try
        {
            var content = await _playlists.RefreshAsync(playlist, ct);
            if (!ct.IsCancellationRequested) Apply(content);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // кэш уже показан — фоновая ошибка не должна портить экран
            _log.LogWarning(ex, "Фоновое обновление плейлиста {Name} не удалось", playlist.Name);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Перечитать текущий плейлист по кнопке.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (CurrentPlaylist is { } playlist)
            await OpenAsync(playlist, forceRefresh: true);
    }

    private void Apply(PlaylistContent content)
    {
        CurrentPlaylist = content.Playlist;

        var playingKey = _all.FirstOrDefault(c => c.IsPlaying)?.Key;

        _all.Clear();
        foreach (var channel in content.Channels)
        {
            _all.Add(new ChannelItemViewModel(channel)
            {
                IsFavorite = _favorites.IsFavorite(content.Playlist.Id, channel.Key),
                IsPlaying = channel.Key == playingKey,
            });
        }

        Groups.Clear();
        Groups.Add(AllGroups);
        Groups.Add(FavoritesGroup);
        foreach (var group in content.Groups) Groups.Add(group);

        if (!Groups.Contains(SelectedGroup)) SelectedGroup = AllGroups;

        ApplyFilter();
        OnPropertyChanged(nameof(HasChannels));
        OnPropertyChanged(nameof(IsEmpty));

        _ = LoadEpgAsync(content);
        _ = LoadLogosAsync();
    }

    private async Task LoadEpgAsync(PlaylistContent content)
    {
        try
        {
            var ok = await _epg.LoadAsync(
                content.Playlist.Id,
                content.EpgUrl,
                TimeSpan.FromHours(Math.Max(1, _settings.Current.EpgCacheHours)));

            if (ok) _dispatcher.TryEnqueue(RefreshEpgLabels);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "EPG не загружен");
        }
    }

    private async Task LoadLogosAsync()
    {
        foreach (var item in _all.ToList())
        {
            var url = item.Channel.LogoUrl;
            if (string.IsNullOrWhiteSpace(url)) continue;

            var cached = _logos.TryGetCached(url);
            if (cached is not null)
            {
                item.LogoPath = cached;
                continue;
            }

            var path = await _logos.GetAsync(url);
            if (path is not null)
                _dispatcher.TryEnqueue(() => item.LogoPath = path);
        }
    }

    private void StartEpgTimer()
    {
        _epgTimer = _dispatcher.CreateTimer();
        _epgTimer.Interval = TimeSpan.FromSeconds(30);
        _epgTimer.Tick += (_, _) => RefreshEpgLabels();
        _epgTimer.Start();
    }

    /// <summary>Пересчитывает «сейчас/далее» — раз в 30 секунд и после загрузки EPG.</summary>
    public void RefreshEpgLabels()
    {
        if (!_epg.HasData) return;

        var now = DateTimeOffset.Now;
        foreach (var item in _all)
            item.ApplyEpg(_epg.NowOn(item.TvgId, now), _epg.NextOn(item.TvgId, now), now);
    }

    public void ApplyFilter()
    {
        var query = SearchText.Trim();
        var group = SelectedGroup;

        IEnumerable<ChannelItemViewModel> source = _all;

        if (group == FavoritesGroup)
            source = source.Where(c => c.IsFavorite);
        else if (group != AllGroups)
            source = source.Where(c => string.Equals(c.Group, group, StringComparison.OrdinalIgnoreCase));

        if (query.Length > 0)
        {
            source = source.Where(c =>
                c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                c.Number.ToString().StartsWith(query, StringComparison.Ordinal));
        }

        Channels.Clear();
        foreach (var item in source) Channels.Add(item);

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    public async Task ToggleFavoriteAsync(ChannelItemViewModel? item)
    {
        if (item is null || CurrentPlaylist is null) return;

        item.IsFavorite = await _favorites.ToggleAsync(CurrentPlaylist.Id, item.Key);

        if (SelectedGroup == FavoritesGroup) ApplyFilter();
    }

    [RelayCommand]
    public void Activate(ChannelItemViewModel? item)
    {
        if (item is null) return;

        foreach (var c in _all) c.IsPlaying = false;
        item.IsPlaying = true;
        SelectedChannel = item;

        ChannelActivated?.Invoke(this, item);
    }

    /// <summary>Соседний канал в текущем отфильтрованном списке (стрелки в плеере).</summary>
    public ChannelItemViewModel? Neighbour(ChannelItemViewModel? current, int delta)
    {
        if (Channels.Count == 0) return null;
        if (current is null) return Channels[0];

        var index = Channels.IndexOf(current);
        if (index < 0) return Channels[0];

        var next = (index + delta) % Channels.Count;
        if (next < 0) next += Channels.Count;
        return Channels[next];
    }

    public ChannelItemViewModel? ByNumber(int number)
        => _all.FirstOrDefault(c => c.Number == number);

    public ChannelItemViewModel? ByKey(string? key)
        => key is null ? null : _all.FirstOrDefault(c => c.Key == key);

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException => "Не удалось связаться с источником. Проверьте, что вы в домашней сети "
                                + "и роутер доступен.",
        TaskCanceledException => "Источник не ответил за отведённое время.",
        FileNotFoundException => "Файл плейлиста не найден.",
        InvalidDataException e => e.Message,
        _ => ex.Message,
    };
}
