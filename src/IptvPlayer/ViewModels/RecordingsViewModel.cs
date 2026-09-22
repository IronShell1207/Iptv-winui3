using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IptvPlayer.Core.Models;
using IptvPlayer.Core.Services;
using Microsoft.UI.Dispatching;

namespace IptvPlayer.ViewModels;

/// <summary>Одна запись в списке.</summary>
public sealed partial class RecordingItemViewModel : ObservableObject
{
    public RecordingItemViewModel(Recording recording) => Recording = recording;

    [ObservableProperty]
    public partial Recording Recording { get; set; }

    public string Id => Recording.Id;
    public string Title => Recording.Title;
    public string FilePath => Recording.FilePath;
    public bool IsActive => Recording.State == RecordingState.Active;
    public bool IsPlayable => Recording.State != RecordingState.Active && File.Exists(Recording.FilePath);

    public string SizeText => Recording.SizeBytes switch
    {
        < 1024L * 1024 => "меньше мегабайта",
        < 1024L * 1024 * 1024 => $"{Recording.SizeBytes / 1024.0 / 1024.0:F0} МБ",
        _ => $"{Recording.SizeBytes / 1024.0 / 1024.0 / 1024.0:F1} ГБ",
    };

    public string DurationText
    {
        get
        {
            var d = Recording.Duration;
            return d.TotalHours >= 1 ? $"{(int)d.TotalHours} ч {d.Minutes} мин" : $"{d.Minutes} мин";
        }
    }

    public string StateText => Recording.State switch
    {
        RecordingState.Active when Recording.Mode == RecordingMode.Scheduled && Recording.StopsAt is { } stops
            => $"Идёт запись до {stops:HH:mm}",
        RecordingState.Active => "Идёт запись",
        RecordingState.Failed => $"Прервалась: {Recording.Error}",
        _ => $"{Recording.StartedAt:d MMMM, HH:mm}",
    };

    public string Subtitle => $"{StateText}  ·  {DurationText}  ·  {SizeText}";

    partial void OnRecordingChanged(Recording value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsPlayable));
    }
}

/// <summary>Вкладка «Записи»: что записано и что пишется прямо сейчас.</summary>
public sealed partial class RecordingsViewModel : ObservableObject
{
    private readonly RecordingService _recordings;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public RecordingsViewModel(RecordingService recordings)
    {
        _recordings = recordings;
        _recordings.Changed += (_, _) => _dispatcher.TryEnqueue(Reload);
    }

    public ObservableCollection<RecordingItemViewModel> Items { get; } = new();

    public bool IsEmpty => Items.Count == 0;

    public string Folder => _recordings.OutputFolder;

    /// <summary>Просьба к оболочке: открыть запись в плеере.</summary>
    public event EventHandler<RecordingItemViewModel>? PlayRequested;

    public void Reload()
    {
        _recordings.RefreshSizes();

        Items.Clear();
        foreach (var recording in _recordings.Recordings)
            Items.Add(new RecordingItemViewModel(recording));

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Play(RecordingItemViewModel? item)
    {
        if (item is { IsPlayable: true }) PlayRequested?.Invoke(this, item);
    }

    [RelayCommand]
    private void StopRecording(RecordingItemViewModel? item)
    {
        if (item is { IsActive: true }) _recordings.Stop(item.Id);
    }

    [RelayCommand]
    private async Task DeleteAsync(RecordingItemViewModel? item)
    {
        if (item is null) return;

        await _recordings.DeleteAsync(item.Id);
        Reload();
    }
}
