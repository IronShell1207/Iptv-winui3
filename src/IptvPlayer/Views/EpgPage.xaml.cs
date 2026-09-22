using IptvPlayer.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace IptvPlayer.Views;

public sealed partial class EpgPage : Page
{
    public EpgPage()
    {
        ViewModel = App.GetService<EpgViewModel>();
        InitializeComponent();
    }

    public EpgViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Reload();
    }
}
