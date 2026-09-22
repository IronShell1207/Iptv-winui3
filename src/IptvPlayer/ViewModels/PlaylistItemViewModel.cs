using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IptvPlayer.Core.Models;

namespace IptvPlayer.ViewModels;

/// <summary>Карточка плейлиста на главном экране.</summary>
public sealed partial class PlaylistItemViewModel : ObservableObject
{
    private static readonly string[] CoverPalette =
    [
        "#3B5BA5", "#2F6F8F", "#6D4AA8", "#2E7D6E", "#A3562F", "#4A5BAF",
    ];

    public PlaylistItemViewModel(Playlist playlist)
    {
        Playlist = playlist;
        SyncGroups();
    }

    /// <summary>До трёх групп для чипов на карточке.</summary>
    public ObservableCollection<string> TopGroups { get; } = new();

    [ObservableProperty]
    public partial Playlist Playlist { get; set; }

    [ObservableProperty]
    public partial bool? IsOnline { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    public string Id => Playlist.Id;
    public string Name => Playlist.Name;

    public string ChannelCountText => Playlist.ChannelCount switch
    {
        0 => "Нет данных",
        1 => "1 канал",
        var n and >= 2 and <= 4 => $"{n} канала",
        var n => $"{n:N0} каналов".Replace(' ', ' '),
    };

    public string SourceText => Playlist.Kind switch
    {
        PlaylistSourceKind.LocalFile => "Локальный файл",
        PlaylistSourceKind.Xtream => "Xtream Codes",
        _ => "M3U-плейлист",
    };

    public string StatusText => IsOnline switch
    {
        true => "Online",
        false => "Offline",
        _ => "Проверка…",
    };

    public string UpdatedText
    {
        get
        {
            if (Playlist.LastUpdatedAt is not { } updated) return "Ещё не обновлялся";

            var age = DateTimeOffset.Now - updated;
            return age switch
            {
                { TotalMinutes: < 1 } => "Обновлён только что",
                { TotalMinutes: < 60 } => $"Обновлён {(int)age.TotalMinutes} мин назад",
                { TotalHours: < 24 } when updated.Date == DateTimeOffset.Now.Date
                    => $"Обновлён сегодня в {updated:HH:mm}",
                { TotalHours: < 24 } => $"Обновлён {(int)age.TotalHours} ч назад",
                { TotalDays: < 30 } => $"Обновлён {(int)age.TotalDays} дн назад",
                _ => $"Обновлён {updated:dd.MM.yyyy}",
            };
        }
    }

    public bool HasGroups => Playlist.Groups.Count > 0;

    /// <summary>Цвет обложки выводится из Id — карточка выглядит одинаково между запусками.</summary>
    public string CoverColor => CoverPalette[Math.Abs(Playlist.Id.GetHashCode()) % CoverPalette.Length];

    partial void OnPlaylistChanged(Playlist value)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ChannelCountText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(HasGroups));
        SyncGroups();
    }

    partial void OnIsOnlineChanged(bool? value) => OnPropertyChanged(nameof(StatusText));

    private void SyncGroups()
    {
        TopGroups.Clear();
        foreach (var group in Playlist.Groups.Take(3)) TopGroups.Add(group);
    }
}
