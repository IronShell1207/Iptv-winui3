using IptvPlayer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IptvPlayer.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage() => InitializeComponent();

    public string Version =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public string DataFolder => AppPaths.Root;

    public string LogFolder => AppPaths.Logs;

    private async void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchFolderPathAsync(AppPaths.Root);

    private async void OnOpenLogsClick(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchFolderPathAsync(AppPaths.Logs);
}
