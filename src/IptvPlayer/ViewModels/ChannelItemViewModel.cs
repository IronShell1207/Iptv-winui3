using CommunityToolkit.Mvvm.ComponentModel;
using IptvPlayer.Core.Models;

namespace IptvPlayer.ViewModels;

/// <summary>Канал в списке: избранное, логотип и текущая передача обновляются на месте.</summary>
public sealed partial class ChannelItemViewModel : ObservableObject
{
    public ChannelItemViewModel(Channel channel) => Channel = channel;

    public Channel Channel { get; }

    public string Name => Channel.Name;
    public int Number => Channel.Number;
    public string? Group => Channel.Group;
    public string Key => Channel.Key;
    public string? TvgId => Channel.TvgId;

    /// <summary>Заглушка вместо логотипа — первые буквы названия.</summary>
    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "?";
            return words.Length == 1
                ? words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant()
                : $"{words[0][0]}{words[1][0]}".ToUpperInvariant();
        }
    }

    [ObservableProperty]
    public partial string? LogoPath { get; set; }

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial string? NowTitle { get; set; }

    [ObservableProperty]
    public partial string? NextTitle { get; set; }

    [ObservableProperty]
    public partial double NowProgress { get; set; }

    [ObservableProperty]
    public partial string? NowTimeRange { get; set; }

    public bool HasLogo => !string.IsNullOrEmpty(LogoPath);

    public bool HasEpg => !string.IsNullOrEmpty(NowTitle);

    partial void OnLogoPathChanged(string? value) => OnPropertyChanged(nameof(HasLogo));

    partial void OnNowTitleChanged(string? value) => OnPropertyChanged(nameof(HasEpg));

    public void ApplyEpg(EpgProgramme? now, EpgProgramme? next, DateTimeOffset at)
    {
        NowTitle = now?.Title;
        NextTitle = next?.Title;
        NowProgress = now?.ProgressAt(at) ?? 0;
        NowTimeRange = now is null
            ? null
            : $"{now.Start.ToLocalTime():HH:mm} – {now.Stop.ToLocalTime():HH:mm}";
    }
}
