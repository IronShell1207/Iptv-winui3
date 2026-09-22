using IptvPlayer.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace IptvPlayer.Views;

/// <summary>
/// Избранное — тот же список каналов с включённым фильтром:
/// отдельного экрана он не заслуживает, а поведение должно совпадать.
/// </summary>
public sealed partial class FavoritesPage : Page
{
    private readonly ChannelsViewModel _channels;

    public FavoritesPage()
    {
        _channels = App.GetService<ChannelsViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _channels.SelectedGroup = ChannelsViewModel.FavoritesGroup;
        Host.Navigate(typeof(ChannelsPage));
    }
}
