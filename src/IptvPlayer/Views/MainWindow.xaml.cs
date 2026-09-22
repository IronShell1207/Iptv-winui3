using IptvPlayer.Core.Services;
using IptvPlayer.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace IptvPlayer.Views;

/// <summary>
/// Окно приложения: заголовок, размеры, полноэкранный режим и PiP.
/// Всё содержимое живёт в <see cref="ShellView"/>.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1360;
    private const int DefaultHeight = 900;

    private readonly SettingsService _settings;
    private readonly PlayerViewModel _player;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private OverlappedPresenter? _presenter;
    private bool _restoringSession;

    public MainWindow(SettingsService settings, PlayerViewModel player)
    {
        _settings = settings;
        _player = player;

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Shell.TitleBar);

        _presenter = AppWindow.Presenter as OverlappedPresenter;
        RestoreWindowPlacement();

        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;

        _player.FullScreenRequested += OnFullScreenRequested;
        _player.CompactOverlayRequested += OnCompactOverlayRequested;
    }

    /// <summary>Переключение вкладки снаружи — из страниц и из плеера.</summary>
    public void NavigateTo(string tag) => Shell.NavigateTo(tag);

    private void OnFullScreenRequested(object? sender, bool fullScreen)
    {
        if (fullScreen)
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            _presenter = AppWindow.Presenter as OverlappedPresenter;
        }
    }

    private void OnCompactOverlayRequested(object? sender, bool compact)
    {
        AppWindow.SetPresenter(compact
            ? AppWindowPresenterKind.CompactOverlay
            : AppWindowPresenterKind.Overlapped);

        if (!compact) _presenter = AppWindow.Presenter as OverlappedPresenter;
    }

    private void RestoreWindowPlacement()
    {
        var session = _settings.Session;

        _restoringSession = true;
        try
        {
            AppWindow.Resize(new SizeInt32(
                session.WindowWidth is > 400 ? session.WindowWidth.Value : DefaultWidth,
                session.WindowHeight is > 300 ? session.WindowHeight.Value : DefaultHeight));

            if (session.WindowX is { } x && session.WindowY is { } y)
                AppWindow.Move(new PointInt32(x, y));

            if (session.IsMaximized) _presenter?.Maximize();
        }
        finally
        {
            _restoringSession = false;
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_restoringSession) return;
        if (!args.DidPositionChange && !args.DidSizeChange) return;
        if (AppWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped) return;

        var maximized = _presenter?.State == OverlappedPresenterState.Maximized;
        var position = AppWindow.Position;
        var size = AppWindow.Size;

        _ = _settings.UpdateSessionAsync(s => maximized
            ? s with { IsMaximized = true }
            : s with
            {
                IsMaximized = false,
                WindowX = position.X,
                WindowY = position.Y,
                WindowWidth = size.Width,
                WindowHeight = size.Height,
            });
    }

    private void OnClosed(object sender, WindowEventArgs args) => App.Current.Shutdown();

    /// <summary>Показывает ошибку, не роняя приложение.</summary>
    public void ShowErrorAsync(string title, string message)
        => _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Shell.XamlRoot,
                    Title = title,
                    Content = message,
                    CloseButtonText = "Закрыть",
                    DefaultButton = ContentDialogButton.Close,
                };
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // диалог мог не открыться (показан другой) — ошибка уже в логе
            }
        });
}
