using System.ComponentModel;
using IptvPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace IptvPlayer.Views;

public sealed partial class PlayerView : UserControl
{
    private PlayerViewModel? _viewModel;

    public PlayerView()
    {
        InitializeComponent();

        // handledEventsToo: движение над видео и кнопками тоже должно будить панели
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), handledEventsToo: true);
        DoubleTapped += OnDoubleTapped;
    }

    /// <summary>Модель приходит из окна, а не из DI напрямую — контрол создаётся разметкой.</summary>
    public void Initialize(PlayerViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        VideoElement.SetMediaPlayer(viewModel.Player);
        UpdatePanelPosition();
        Bindings.Update();
    }

    public PlayerViewModel ViewModel => _viewModel ??= App.GetService<PlayerViewModel>();

    /// <summary>Нижняя панель и кнопки гаснут вместе — одна прозрачность на всех.</summary>
    public double ChromeOpacity => ViewModel.AreControlsVisible ? 1 : 0;

    /// <summary>Панель каналов живёт своей жизнью: наведение к краю или закрепление кнопкой.</summary>
    public double ChannelPanelOpacity => ViewModel.IsChannelPanelOpen ? 1 : 0;

    public string PlayPauseGlyph => ViewModel.IsPlaying ? "Pause" : "Play";

    public string VolumeGlyph => ViewModel.IsMuted || ViewModel.Volume <= 0 ? "VolumeMute" : "Volume";

    public string FullScreenGlyph => ViewModel.IsFullScreen ? "FullscreenExit" : "Fullscreen";

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.AreControlsVisible):
                OnPropertyChangedLocal(nameof(ChromeOpacity));
                break;
            case nameof(PlayerViewModel.IsChannelPanelOpen):
                UpdatePanelPosition();
                OnPropertyChangedLocal(nameof(ChannelPanelOpacity));
                break;
            case nameof(PlayerViewModel.IsPlaying):
                OnPropertyChangedLocal(nameof(PlayPauseGlyph));
                break;
            case nameof(PlayerViewModel.IsMuted):
            case nameof(PlayerViewModel.Volume):
                OnPropertyChangedLocal(nameof(VolumeGlyph));
                break;
            case nameof(PlayerViewModel.IsFullScreen):
                OnPropertyChangedLocal(nameof(FullScreenGlyph));
                break;
        }
    }

    // x:Bind на свойства самого контрола обновляется через Bindings.Update
    private void OnPropertyChangedLocal(string _) => Bindings.Update();

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.ShowControls();
        ViewModel.UpdatePointer(e.GetCurrentPoint(this).Position.X);
    }

    /// <summary>Скрытая панель отъезжает влево — так возврат читается как выезд, а не вспышка.</summary>
    private void UpdatePanelPosition()
        => ChannelPanel.Translation = ViewModel.IsChannelPanelOpen
            ? new System.Numerics.Vector3(0, 0, 0)
            : new System.Numerics.Vector3(-40, 0, 0);

    private void OnHotZoneEntered(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.IsChannelPanelOpen = true;
        ViewModel.ShowControls();
    }

    private void OnChromePointerEntered(object sender, PointerRoutedEventArgs e)
        => ViewModel.SetPointerOverChrome(true);

    private void OnChromePointerExited(object sender, PointerRoutedEventArgs e)
        => ViewModel.SetPointerOverChrome(false);

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        => ViewModel.ToggleFullScreenCommand.Execute(null);

    private async void OnChannelClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ChannelItemViewModel item)
            await ViewModel.PlayAsync(item);
    }

    private void OnFocusSearchClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Visibility = SearchBox.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (SearchBox.Visibility == Visibility.Visible) SearchBox.Focus(FocusState.Programmatic);
        ViewModel.ShowControls();
    }

    private void OnAllChannelsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Channels.SelectedGroup = ChannelsViewModel.AllGroups;
        ViewModel.IsChannelPanelOpen = true;
    }

    private void OnFavoritesClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Channels.SelectedGroup = ChannelsViewModel.FavoritesGroup;
        ViewModel.IsChannelPanelOpen = true;
    }

    private void OnGroupsClick(object sender, RoutedEventArgs e)
    {
        GroupCombo.IsDropDownOpen = true;
        ViewModel.ShowControls();
    }

    private void OnGuideClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseCommand.Execute(null);
        App.Current.MainWindow?.NavigateTo("guide");
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseCommand.Execute(null);
        App.Current.MainWindow?.NavigateTo("settings");
    }

    private void OnMoreClick(object sender, RoutedEventArgs e) => ViewModel.ShowControls();

    private void OnCloseClick(object sender, RoutedEventArgs e)
        => ViewModel.CloseCommand.Execute(null);

    private async void OnRefreshStreamClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Current is { } current) await ViewModel.PlayAsync(current);
    }
}
