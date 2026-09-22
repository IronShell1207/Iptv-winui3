using IptvPlayer.Core.Models;

namespace IptvPlayer.Core.Services;

/// <summary>Настройки и состояние сессии, кэшируются в памяти.</summary>
public sealed class SettingsService
{
    private const string SettingsFile = "settings.json";
    private const string SessionFile = "session.json";

    private readonly JsonStore _store;
    private AppSettings? _settings;
    private SessionState? _session;

    public SettingsService(JsonStore store) => _store = store;

    public AppSettings Current => _settings ??= new AppSettings();

    public SessionState Session => _session ??= new SessionState();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        _settings = await _store.LoadAsync<AppSettings>(SettingsFile, ct).ConfigureAwait(false)
                    ?? new AppSettings();
        _session = await _store.LoadAsync<SessionState>(SessionFile, ct).ConfigureAwait(false)
                   ?? new SessionState();
    }

    public async Task UpdateAsync(Func<AppSettings, AppSettings> mutate, CancellationToken ct = default)
    {
        _settings = mutate(Current);
        await _store.SaveAsync(SettingsFile, _settings, ct).ConfigureAwait(false);
    }

    public async Task UpdateSessionAsync(Func<SessionState, SessionState> mutate, CancellationToken ct = default)
    {
        _session = mutate(Session);
        await _store.SaveAsync(SessionFile, _session, ct).ConfigureAwait(false);
    }
}
