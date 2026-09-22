using IptvPlayer.Core.Services;
using IptvPlayer.Services;
using IptvPlayer.ViewModels;
using IptvPlayer.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace IptvPlayer;

public partial class App : Application
{
    private FileLoggerProvider? _fileLogger;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static new App Current => (App)Application.Current;

    public IServiceProvider Services { get; private set; } = null!;

    public MainWindow? MainWindow { get; private set; }

    public static T GetService<T>() where T : class
        => Current.Services.GetRequiredService<T>();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = BuildServices();
        ApplyCulture();

        MainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow.Activate();
    }

    /// <summary>
    /// Интерфейс русский, поэтому даты и числа форматируем по-русски
    /// независимо от языка системы.
    /// </summary>
    private void ApplyCulture()
    {
        var language = Services.GetRequiredService<SettingsService>().Current.Language;
        if (string.IsNullOrWhiteSpace(language)) return;

        try
        {
            var culture = new System.Globalization.CultureInfo(language);
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            // язык из настроек не поддерживается системой — остаёмся на системном
        }
    }

    private IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        var store = new JsonStore(AppPaths.Root);
        var settings = new SettingsService(store);

        // настройки нужны до построения контейнера — от них зависит уровень логирования
        settings.LoadAsync().GetAwaiter().GetResult();
        var level = Enum.TryParse<LogLevel>(settings.Current.LogLevel, true, out var parsed)
            ? parsed
            : LogLevel.Information;

        _fileLogger = new FileLoggerProvider(AppPaths.Logs, level);

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(level);
            builder.AddProvider(_fileLogger);
        });

        services.AddSingleton(store);
        services.AddSingleton(settings);

        services.AddSingleton(_ =>
        {
            var http = new HttpClient(new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(8),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            })
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("IptvPlayer/1.0");
            return http;
        });

        services.AddSingleton<PlaylistService>();
        services.AddSingleton<EpgService>();
        services.AddSingleton<FavoritesService>();
        services.AddSingleton<LogoCacheService>();
        services.AddSingleton<PlaybackService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<PlaylistsViewModel>();
        services.AddSingleton<ChannelsViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<EpgViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            Services?.GetService<ILogger<App>>()?
                .LogCritical(e.Exception, "Необработанное исключение: {Message}", e.Message);
        }
        catch (Exception)
        {
            // на этом этапе логировать больше нечем
        }

        // приложение не должно молча падать на ошибке в обработчике UI
        e.Handled = true;
        MainWindow?.ShowErrorAsync("Внутренняя ошибка", e.Exception?.Message ?? e.Message);
    }

    public void Shutdown()
    {
        Services?.GetService<PlaybackService>()?.Dispose();
        _fileLogger?.Dispose();
    }
}
