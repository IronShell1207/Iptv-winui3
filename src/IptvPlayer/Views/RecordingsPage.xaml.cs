using IptvPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace IptvPlayer.Views;

public sealed partial class RecordingsPage : Page
{
    public RecordingsPage()
    {
        ViewModel = App.GetService<RecordingsViewModel>();
        InitializeComponent();
    }

    public RecordingsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Reload();
    }

    private static RecordingItemViewModel? ItemOf(object sender)
        => sender is FrameworkElement { Tag: RecordingItemViewModel item } ? item : null;

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) ViewModel.PlayCommand.Execute(item);
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) ViewModel.StopRecordingCommand.Execute(item);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Удалить запись?",
            Content = $"«{item.Title}» будет удалена вместе с файлом.",
            PrimaryButtonText = "Удалить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteCommand.ExecuteAsync(item);
    }

    private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(ViewModel.Folder);
        await Windows.System.Launcher.LaunchFolderPathAsync(ViewModel.Folder);
    }
}
