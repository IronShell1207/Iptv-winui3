using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IptvPlayer.Core.Models;
using IptvPlayer.Core.Services;

namespace IptvPlayer.ViewModels;

/// <summary>Передача в сетке на день.</summary>
public sealed partial class EpgSlotViewModel : ObservableObject
{
    public EpgSlotViewModel(EpgProgramme programme, DateTimeOffset now)
    {
        Programme = programme;
        IsCurrent = programme.Start <= now && now < programme.Stop;
        IsPast = programme.Stop <= now;
        Progress = IsCurrent ? programme.ProgressAt(now) : 0;
    }

    public EpgProgramme Programme { get; }

    public string Title => Programme.Title;
    public string? Description => Programme.Description;
    public string? Category => Programme.Category;
    public string TimeRange =>
        $"{Programme.Start.ToLocalTime():HH:mm} – {Programme.Stop.ToLocalTime():HH:mm}";
    public string StartTime => Programme.Start.ToLocalTime().ToString("HH:mm");
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool IsCurrent { get; }
    public bool IsPast { get; }
    public double Progress { get; }
}

/// <summary>Вкладка «Телепрограмма»: сетка на выбранный день по выбранному каналу.</summary>
public sealed partial class EpgViewModel : ObservableObject
{
    private readonly EpgService _epg;
    private readonly ChannelsViewModel _channels;

    public EpgViewModel(EpgService epg, ChannelsViewModel channels)
    {
        _epg = epg;
        _channels = channels;
        _epg.Updated += (_, _) => Reload();
    }

    public ObservableCollection<EpgSlotViewModel> Slots { get; } = new();

    public ChannelsViewModel Channels => _channels;

    [ObservableProperty]
    public partial ChannelItemViewModel? SelectedChannel { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset SelectedDay { get; set; } = DateTimeOffset.Now.Date;

    [ObservableProperty]
    public partial string? EmptyMessage { get; set; }

    public bool HasSlots => Slots.Count > 0;

    public string DayTitle => SelectedDay.Date == DateTimeOffset.Now.Date
        ? "Сегодня"
        : SelectedDay.ToString("dddd, d MMMM");

    partial void OnSelectedChannelChanged(ChannelItemViewModel? value) => Reload();

    partial void OnSelectedDayChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(DayTitle));
        Reload();
    }

    public void Reload()
    {
        Slots.Clear();

        if (SelectedChannel is null)
        {
            SelectedChannel = _channels.SelectedChannel ?? _channels.Channels.FirstOrDefault();
            if (SelectedChannel is null)
            {
                EmptyMessage = "Сначала откройте плейлист — программа подтянется для его каналов.";
                OnPropertyChanged(nameof(HasSlots));
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(SelectedChannel.TvgId))
        {
            EmptyMessage = $"У канала «{SelectedChannel.Name}» нет tvg-id, сопоставить программу не с чем.";
            OnPropertyChanged(nameof(HasSlots));
            return;
        }

        var now = DateTimeOffset.Now;
        var dayStart = new DateTimeOffset(SelectedDay.Date, now.Offset);

        foreach (var programme in _epg.DaySchedule(SelectedChannel.TvgId, dayStart))
            Slots.Add(new EpgSlotViewModel(programme, now));

        EmptyMessage = Slots.Count == 0
            ? "На выбранный день программы нет."
            : null;

        OnPropertyChanged(nameof(HasSlots));
    }

    [RelayCommand]
    private void PreviousDay() => SelectedDay = SelectedDay.AddDays(-1);

    [RelayCommand]
    private void NextDay() => SelectedDay = SelectedDay.AddDays(1);

    [RelayCommand]
    private void Today() => SelectedDay = DateTimeOffset.Now.Date;
}
