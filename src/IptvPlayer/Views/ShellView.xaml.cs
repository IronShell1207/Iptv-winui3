using IptvPlayer.Core.Services;
using IptvPlayer.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace IptvPlayer.Views;

/// <summary>
/// Оболочка приложения: боковое меню, область страниц и плеер поверх них.
/// Живёт в UserControl, а не в Window, потому что x:Bind в Window не работает.
/// </summary>
public sealed partial class ShellView : UserControl
{
    private readonly SettingsService _settings;
    private readonly PlaylistsViewModel _playlists;
    private readonly ChannelsViewModel _channels;
    private readonly PlayerViewModel _player;

    public ShellView()
    {
        ViewModel = App.GetService<MainViewModel>();
        _settings = App.GetService<SettingsService>();
        _playlists = App.GetService<PlaylistsViewModel>();
        _channels = App.GetService<ChannelsViewModel>();
        _player = App.GetService<PlayerViewModel>();

        InitializeComponent();

        Player.Initialize(_player);
        _channels.ChannelActivated += OnChannelActivated;
        _player.CloseRequested += OnPlayerCloseRequested;

        // handledEventsToo: список каналов забирает стрелки и пробел себе,
        // а плееру они нужны как горячие клавиши
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown), handledEventsToo: true);
        Loaded += OnLoaded;
    }

    public MainViewModel ViewModel { get; }

    /// <summary>Полоса, за которую тащится окно — её окно отдаёт в SetTitleBar.</summary>
    public UIElement TitleBar => TitleBarArea;

    public PlayerViewModel PlayerViewModel => _player;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PrimaryNav.SelectedIndex = 0;

        await _playlists.InitializeAsync();
        _ = ViewModel.CheckConnectionAsync();

        await RestoreLastChannelAsync();
    }

    // --- навигация ---

    private void OnPrimaryNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PrimaryNav.SelectedItem is not NavigationItem item) return;

        SecondaryNav.SelectedItem = null;
        ViewModel.SelectedItem = item;
        Navigate(item.Tag);
    }

    private void OnSecondaryNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SecondaryNav.SelectedItem is not NavigationItem item) return;

        PrimaryNav.SelectedItem = null;
        ViewModel.SelectedItem = item;
        Navigate(item.Tag);
    }

    private void Navigate(string tag)
    {
        var pageType = tag switch
        {
            "playlists" => typeof(PlaylistsPage),
            "favorites" => typeof(FavoritesPage),
            "channels" => typeof(ChannelsPage),
            "guide" => typeof(EpgPage),
            "recordings" => typeof(RecordingsPage),
            "settings" => typeof(SettingsPage),
            "about" => typeof(AboutPage),
            _ => typeof(PlaylistsPage),
        };

        if (ContentFrame.CurrentSourcePageType == pageType) return;

        ContentFrame.Navigate(pageType, null, new DrillInNavigationTransitionInfo());
    }

    /// <summary>Страницы просят открыть другую вкладку — например «Каналы» после выбора плейлиста.</summary>
    public void NavigateTo(string tag)
    {
        var index = ViewModel.PrimaryItems.ToList().FindIndex(i => i.Tag == tag);
        if (index >= 0)
        {
            PrimaryNav.SelectedIndex = index;
            return;
        }

        var secondary = ViewModel.SecondaryItems.FirstOrDefault(i => i.Tag == tag);
        if (secondary is not null) SecondaryNav.SelectedItem = secondary;
        else Navigate(tag);
    }

    // --- плеер ---

    private async void OnChannelActivated(object? sender, ChannelItemViewModel item)
    {
        ViewModel.IsPlayerActive = true;
        await _player.PlayAsync(item);
    }

    private void OnPlayerCloseRequested(object? sender, EventArgs e)
    {
        _player.Stop();
        ViewModel.IsPlayerActive = false;
    }

    // --- горячие клавиши ---

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (ctrl && e.Key == VirtualKey.F)
        {
            NavigateTo("channels");
            e.Handled = true;
            return;
        }

        if (!ViewModel.IsPlayerActive) return;

        if (ctrl && e.Key is VirtualKey.Up or VirtualKey.Down)
        {
            if (e.Key == VirtualKey.Up) _player.VolumeUpCommand.Execute(null);
            else _player.VolumeDownCommand.Execute(null);

            e.Handled = true;
            return;
        }

        // при вводе в поле поиска клавиши плееру не достаются
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox) return;

        switch (e.Key)
        {
            case VirtualKey.Space:
            case VirtualKey.K:
                _player.TogglePlayPauseCommand.Execute(null);
                break;

            case VirtualKey.F or VirtualKey.F11:
                _player.ToggleFullScreenCommand.Execute(null);
                break;

            case VirtualKey.Escape:
                if (_player.IsFullScreen) _player.IsFullScreen = false;
                else _player.CloseCommand.Execute(null);
                break;

            // в оконном режиме стрелки принадлежат списку каналов
            case VirtualKey.Up when _player.IsFullScreen:
            case VirtualKey.PageUp:
                _player.PreviousChannelCommand.Execute(null);
                break;

            case VirtualKey.Down when _player.IsFullScreen:
            case VirtualKey.PageDown:
                _player.NextChannelCommand.Execute(null);
                break;

            case VirtualKey.Left when _player.IsFullScreen:
                _player.IsChannelPanelOpen = true;
                _player.ShowControls();
                break;

            case VirtualKey.Right when _player.IsFullScreen:
                _player.IsChannelPanelOpen = false;
                break;

            case VirtualKey.M:
                _player.ToggleMuteCommand.Execute(null);
                break;

            // громкость: плюс и минус в обоих рядах клавиатуры
            case VirtualKey.Add:
            case (VirtualKey)187:       // OemPlus
                _player.VolumeUpCommand.Execute(null);
                break;

            case VirtualKey.Subtract:
            case (VirtualKey)189:       // OemMinus
                _player.VolumeDownCommand.Execute(null);
                break;

            case VirtualKey.C:
                _player.ToggleChannelPanelCommand.Execute(null);
                break;

            case VirtualKey.A:
                _player.CycleVideoFitCommand.Execute(null);
                break;

            case VirtualKey.P:
                _player.ToggleCompactOverlayCommand.Execute(null);
                break;

            case VirtualKey.R:
                _player.RetryCommand.Execute(null);
                break;

            case VirtualKey.D:
                _player.ToggleFavoriteCommand.Execute(null);
                break;

            case VirtualKey.Back:
                _player.LastChannelCommand.Execute(null);
                break;

            case >= VirtualKey.Number0 and <= VirtualKey.Number9:
                _player.AppendNumber((char)('0' + (e.Key - VirtualKey.Number0)));
                break;

            case >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9:
                _player.AppendNumber((char)('0' + (e.Key - VirtualKey.NumberPad0)));
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private async Task RestoreLastChannelAsync()
    {
        if (!_settings.Current.ResumeLastChannel) return;

        var session = _settings.Session;
        var playlist = _playlists.ById(session.LastPlaylistId);
        if (playlist is null) return;

        await _channels.OpenAsync(playlist.Playlist);

        var channel = _channels.ByKey(session.LastChannelKey);
        if (channel is null)
        {
            NavigateTo("channels");
            return;
        }

        _channels.Activate(channel);
    }
}
