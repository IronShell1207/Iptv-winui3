using IptvPlayer.Core.Models;
using IptvPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace IptvPlayer.Views;

public sealed partial class PlaylistsPage : Page
{
    private readonly ChannelsViewModel _channels;

    public PlaylistsPage()
    {
        ViewModel = App.GetService<PlaylistsViewModel>();
        _channels = App.GetService<ChannelsViewModel>();

        InitializeComponent();
    }

    public PlaylistsViewModel ViewModel { get; }

    private static PlaylistItemViewModel? ItemOf(object sender)
        => sender is FrameworkElement { Tag: PlaylistItemViewModel item } ? item : null;

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;

        await _channels.OpenAsync(item.Playlist);
        (App.Current.MainWindow)?.NavigateTo("channels");
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) await ViewModel.RefreshAsync(item);
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Удалить плейлист?",
            Content = $"«{item.Name}» и его избранное будут удалены. Сам источник не пострадает.",
            PrimaryButtonText = "Удалить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RemoveAsync(item);
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;

        var input = new TextBox { Text = item.Name, SelectionStart = item.Name.Length };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Переименовать плейлист",
            Content = input,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary &&
            !string.IsNullOrWhiteSpace(input.Text))
        {
            await ViewModel.RenameAsync(item, input.Text);
        }
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
        => await ShowAddDialogAsync(PlaylistSourceKind.M3uUrl);

    private async void OnAddM3uClick(object sender, RoutedEventArgs e)
        => await ShowAddDialogAsync(PlaylistSourceKind.M3uUrl);

    private async void OnAddXtreamClick(object sender, RoutedEventArgs e)
        => await ShowAddDialogAsync(PlaylistSourceKind.Xtream);

    private async void OnAddFileClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".m3u");
        picker.FileTypeFilter.Add(".m3u8");
        picker.FileTypeFilter.Add(".txt");

        // Для unpackaged-приложения picker нужно привязать к окну вручную
        var window = App.Current.MainWindow;
        if (window is not null)
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        await ViewModel.AddAsync(
            System.IO.Path.GetFileNameWithoutExtension(file.Name),
            PlaylistSourceKind.LocalFile,
            file.Path);
    }

    private async Task ShowAddDialogAsync(PlaylistSourceKind kind)
    {
        var dialog = new AddPlaylistDialog(kind) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await ViewModel.AddAsync(
            dialog.PlaylistName,
            dialog.Kind,
            dialog.Source,
            dialog.Username,
            dialog.Password,
            dialog.EpgUrl);
    }
}
