using IptvPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace IptvPlayer.Views;

public sealed partial class ChannelsPage : Page
{
    public ChannelsPage()
    {
        ViewModel = App.GetService<ChannelsViewModel>();
        InitializeComponent();
    }

    public ChannelsViewModel ViewModel { get; }

    /// <summary>Заголовок повторяет имя открытого плейлиста.</summary>
    public string Title => ViewModel.CurrentPlaylist?.Name ?? "Каналы";

    public string Subtitle => ViewModel.CurrentPlaylist is null
        ? "Выберите плейлист, чтобы увидеть каналы"
        : "Выберите канал — начнётся воспроизведение";

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshEpgLabels();
        Bindings.Update();
    }

    private void OnChannelClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ChannelItemViewModel item) ViewModel.Activate(item);
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChannelItemViewModel item }) ViewModel.Activate(item);
    }

    private async void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChannelItemViewModel item })
            await ViewModel.ToggleFavoriteAsync(item);
    }

    private void OnGoToPlaylistsClick(object sender, RoutedEventArgs e)
        => App.Current.MainWindow?.NavigateTo("playlists");
}
