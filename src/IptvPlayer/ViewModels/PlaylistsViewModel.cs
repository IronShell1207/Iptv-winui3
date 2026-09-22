using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IptvPlayer.Core.Models;
using IptvPlayer.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace IptvPlayer.ViewModels;

/// <summary>Главный экран: карточки плейлистов, добавление и удаление.</summary>
public sealed partial class PlaylistsViewModel : ObservableObject
{
    private readonly PlaylistService _playlists;
    private readonly FavoritesService _favorites;
    private readonly SettingsService _settings;
    private readonly ILogger<PlaylistsViewModel> _log;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private readonly List<PlaylistItemViewModel> _all = new();

    public PlaylistsViewModel(
        PlaylistService playlists,
        FavoritesService favorites,
        SettingsService settings,
        ILogger<PlaylistsViewModel> log)
    {
        _playlists = playlists;
        _favorites = favorites;
        _settings = settings;
        _log = log;
    }

    /// <summary>Карточки плейлистов и замыкающая карточка «добавить».</summary>
    public ObservableCollection<object> Items { get; } = new();

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool IsEmpty => !IsBusy && _all.Count == 0;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await _playlists.LoadAsync();
            await _favorites.LoadAsync();
            Reload();
        }
        finally
        {
            IsBusy = false;
        }

        _ = CheckAvailabilityAsync();
    }

    private void Reload()
    {
        _all.Clear();
        foreach (var playlist in _playlists.Playlists)
            _all.Add(new PlaylistItemViewModel(playlist));

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();

        Items.Clear();
        foreach (var item in _all)
        {
            if (query.Length > 0 &&
                !item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                continue;

            Items.Add(item);
        }

        Items.Add(AddPlaylistPlaceholder.Instance);
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Фоновая проверка источников для бейджа Online/Offline. Заодно подтягивает
    /// каналы плейлистов, о которых мы ещё ничего не знаем, — иначе карточка
    /// показывала бы «Нет данных» до первого открытия.
    /// </summary>
    public async Task CheckAvailabilityAsync()
    {
        foreach (var item in _all.ToList())
        {
            var online = await _playlists.CheckAvailabilityAsync(item.Playlist);
            _dispatcher.TryEnqueue(() => item.IsOnline = online);

            if (online && item.Playlist.ChannelCount == 0)
                await RefreshAsync(item);
        }
    }

    public async Task<Playlist> AddAsync(
        string name, PlaylistSourceKind kind, string source,
        string? username = null, string? password = null, string? epgUrl = null)
    {
        var playlist = new Playlist
        {
            Id = Playlist.NewId(),
            Name = string.IsNullOrWhiteSpace(name) ? "Новый плейлист" : name.Trim(),
            Kind = kind,
            Source = source.Trim(),
            Username = username,
            Password = password,
            EpgUrl = string.IsNullOrWhiteSpace(epgUrl) ? null : epgUrl.Trim(),
        };

        await _playlists.AddAsync(playlist);

        var item = new PlaylistItemViewModel(playlist);
        _all.Add(item);
        ApplyFilter();

        _ = RefreshAsync(item);
        return playlist;
    }

    /// <summary>Обновляет один плейлист и подтягивает количество каналов и группы.</summary>
    [RelayCommand]
    public async Task RefreshAsync(PlaylistItemViewModel? item)
    {
        if (item is null) return;

        item.IsRefreshing = true;
        try
        {
            var content = await _playlists.RefreshAsync(item.Playlist);
            item.Playlist = content.Playlist;
            item.IsOnline = true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Плейлист {Name} не обновился", item.Name);
            item.IsOnline = false;
        }
        finally
        {
            item.IsRefreshing = false;
        }
    }

    [RelayCommand]
    public async Task RemoveAsync(PlaylistItemViewModel? item)
    {
        if (item is null) return;

        await _playlists.RemoveAsync(item.Id);
        await _favorites.RemovePlaylistAsync(item.Id);

        _all.Remove(item);
        ApplyFilter();
    }

    public async Task RenameAsync(PlaylistItemViewModel item, string newName)
    {
        var updated = item.Playlist with { Name = newName.Trim() };
        await _playlists.UpdateAsync(updated);
        item.Playlist = updated;
        ApplyFilter();
    }

    /// <summary>Первый запуск: предлагаем плейлист из настроек по умолчанию.</summary>
    public async Task EnsureDefaultAsync()
    {
        if (_all.Count > 0) return;

        await AddAsync(
            "Домашний IPTV",
            PlaylistSourceKind.M3uUrl,
            AppSettings.DefaultPlaylistUrl,
            epgUrl: AppSettings.DefaultEpgUrl);
    }

    public PlaylistItemViewModel? ById(string? id)
        => id is null ? null : _all.FirstOrDefault(p => p.Id == id);

    public string UdpxyBaseUrl => _settings.Current.UdpxyBaseUrl;
}
