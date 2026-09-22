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

        SizeChanged += (_, _) => UpdateLayoutInsets();
        ChannelPanel.SizeChanged += (_, _) => UpdateLayoutInsets();
    }

    /// <summary>Модель приходит из окна, а не из DI напрямую — контрол создаётся разметкой.</summary>
    public void Initialize(PlayerViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        VideoElement.SetMediaPlayer(viewModel.Player);
        UpdatePanelPosition();
        UpdateLayoutInsets();
        Bindings.Update();
    }

    public PlayerViewModel ViewModel => _viewModel ??= App.GetService<PlayerViewModel>();

    /// <summary>Нижняя панель и кнопки гаснут вместе — одна прозрачность на всех.</summary>
    public double ChromeOpacity => ViewModel.AreControlsVisible ? 1 : 0;

    /// <summary>Панель каналов живёт своей жизнью: наведение к краю или закрепление кнопкой.</summary>
    public double ChannelPanelOpacity => ViewModel.IsChannelPanelOpen ? 1 : 0;

    /// <summary>Отступы блока с названием канала: он уступает место открытой панели.</summary>
    public Thickness InfoMargin { get; private set; } = new(40, 40, 32, 0);

    /// <summary>Кнопки управления центрируются по свободной части кадра, а не по всему окну.</summary>
    public Thickness ControlsMargin { get; private set; } = new(0, 26, 0, 30);

    /// <summary>На узком окне «далее» уступает место названию канала.</summary>
    public bool ShowNextBlock => ViewModel.HasNext && ActualWidth >= NextBlockMinWidth;

    private const double NextBlockMinWidth = 1000;

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
            case nameof(PlayerViewModel.NextTitle):
                OnPropertyChangedLocal(nameof(ShowNextBlock));
                break;
            case nameof(PlayerViewModel.IsChannelPanelOpen):
                UpdatePanelPosition();
                UpdateLayoutInsets();
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
    /// <summary>
    /// Пересчитывает отступы под текущую ширину окна и состояние панели каналов.
    /// На узком окне панель накрывает кадр целиком, сдвигать содержимое некуда —
    /// тогда отступы остаются базовыми.
    /// </summary>
    private void UpdateLayoutInsets()
    {
        var panelWidth = ChannelPanel.ActualWidth + ChannelPanel.Margin.Left + 40;
        var available = ActualWidth;

        var shifted = ViewModel.IsChannelPanelOpen
                      && available > 0
                      && panelWidth < available * 0.55;

        var left = shifted ? panelWidth : 40;

        InfoMargin = new Thickness(left, 40, 32, 0);
        ControlsMargin = new Thickness(shifted ? panelWidth : 0, 26, 0, 30);

        Bindings.Update();
    }

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
