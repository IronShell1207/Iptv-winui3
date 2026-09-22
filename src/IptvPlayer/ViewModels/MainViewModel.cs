using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IptvPlayer.Core.Models;
using IptvPlayer.Core.Services;

namespace IptvPlayer.ViewModels;

/// <summary>Пункт бокового меню.</summary>
public sealed record NavigationItem(string Tag, string Title, string Glyph);

/// <summary>Состояние оболочки: навигация, статус подключения, видимость плеера.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly PlaylistService _playlists;
    private readonly SettingsService _settings;

    public MainViewModel(PlaylistService playlists, SettingsService settings)
    {
        _playlists = playlists;
        _settings = settings;
    }

    public ObservableCollection<NavigationItem> PrimaryItems { get; } =
    [
        new("playlists", "Плейлисты", "Playlists"),
        new("favorites", "Избранное", "Favorites"),
        new("channels", "Каналы", "Channels"),
        new("guide", "Телепрограмма", "Guide"),
        new("recordings", "Записи", "Recordings"),
    ];

    public ObservableCollection<NavigationItem> SecondaryItems { get; } =
    [
        new("settings", "Настройки", "Settings"),
        new("about", "О программе", "About"),
    ];

    [ObservableProperty]
    public partial NavigationItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsPlayerActive { get; set; }

    [ObservableProperty]
    public partial bool? IsSourceOnline { get; set; }

    [ObservableProperty]
    public partial string ConnectionTitle { get; set; } = "Проверка связи";

    [ObservableProperty]
    public partial string ConnectionSubtitle { get; set; } = "Ищем источник…";

    public MainViewModel Self => this;

    /// <summary>Проверяет udpxy и сообщает о результате в подвал бокового меню.</summary>
    public async Task CheckConnectionAsync()
    {
        var udpxy = _settings.Current.UdpxyBaseUrl;
        var ok = await _playlists.CheckUdpxyAsync(udpxy);

        IsSourceOnline = ok;
        ConnectionTitle = ok ? "Подключено" : "Нет связи";
        ConnectionSubtitle = ok
            ? "Можно смотреть"
            : $"{HostOf(udpxy)} недоступен";
    }

    private static string HostOf(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
    }

    public AppSettings Settings => _settings.Current;
}
