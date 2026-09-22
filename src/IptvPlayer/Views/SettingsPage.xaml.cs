using IptvPlayer.Services;
using IptvPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IptvPlayer.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    private async void OnOpenLogsClick(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchFolderPathAsync(AppPaths.Logs);
}
