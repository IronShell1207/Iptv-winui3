using System.Security.Cryptography;
using System.Text;
using IptvPlayer.Core.Models;
using IptvPlayer.Core.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IptvPlayer.Core.Services;

/// <summary>Загруженный плейлист: описание, каналы и признак «из кэша».</summary>
public sealed record PlaylistContent
{
    public required Playlist Playlist { get; init; }
    public required IReadOnlyList<Channel> Channels { get; init; }
    public string? EpgUrl { get; init; }
    public bool FromCache { get; init; }
    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.Now;

    public IReadOnlyList<string> Groups => Channels
        .Select(c => c.Group)
        .Where(g => !string.IsNullOrWhiteSpace(g))
        .Select(g => g!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
}

/// <summary>
/// Хранит список плейлистов, качает M3U с дисковым кэшем.
/// Кэш отдаётся сразу, обновление идёт следом.
/// </summary>
public sealed class PlaylistService
{
    private const string FileName = "playlists.json";
    private const string CacheFolder = "playlists";

    private readonly JsonStore _store;
    private readonly HttpClient _http;
    private readonly ILogger<PlaylistService> _log;
    private List<Playlist> _playlists = new();

    public PlaylistService(JsonStore store, HttpClient http, ILogger<PlaylistService>? log = null)
    {
        _store = store;
        _http = http;
        _log = log ?? NullLogger<PlaylistService>.Instance;
        Directory.CreateDirectory(Path.Combine(_store.RootFolder, CacheFolder));
    }

    public IReadOnlyList<Playlist> Playlists => _playlists;

    public event EventHandler? PlaylistsChanged;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        _playlists = await _store.LoadAsync<List<Playlist>>(FileName, ct).ConfigureAwait(false)
                     ?? new List<Playlist>();
        PlaylistsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<Playlist> AddAsync(Playlist playlist, CancellationToken ct = default)
    {
        _playlists.Add(playlist);
        await SaveIndexAsync(ct).ConfigureAwait(false);
        return playlist;
    }

    public async Task UpdateAsync(Playlist playlist, CancellationToken ct = default)
    {
        var index = _playlists.FindIndex(p => p.Id == playlist.Id);
        if (index < 0) return;

        _playlists[index] = playlist;
        await SaveIndexAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string playlistId, CancellationToken ct = default)
    {
        _playlists.RemoveAll(p => p.Id == playlistId);

        var cache = CachePath(playlistId);
        if (File.Exists(cache))
        {
            try { File.Delete(cache); }
            catch (IOException ex) { _log.LogWarning(ex, "Не удалось удалить кэш плейлиста {Id}", playlistId); }
        }

        await SaveIndexAsync(ct).ConfigureAwait(false);
    }

    private async Task SaveIndexAsync(CancellationToken ct)
    {
        await _store.SaveAsync(FileName, _playlists, ct).ConfigureAwait(false);
        PlaylistsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string CachePath(string playlistId)
        => Path.Combine(_store.RootFolder, CacheFolder, playlistId + ".m3u");

    /// <summary>Читает плейлист из дискового кэша; null, если кэша нет.</summary>
    public async Task<PlaylistContent?> LoadFromCacheAsync(Playlist playlist, CancellationToken ct = default)
    {
        var path = CachePath(playlist.Id);
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var doc = M3uParser.ParseBytes(bytes);
            return Build(playlist, doc, fromCache: true);
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            _log.LogWarning(ex, "Кэш плейлиста {Id} нечитаем", playlist.Id);
            return null;
        }
    }

    /// <summary>Скачивает (или читает с диска) плейлист и обновляет кэш.</summary>
    public async Task<PlaylistContent> RefreshAsync(Playlist playlist, CancellationToken ct = default)
    {
        var location = playlist.ResolveM3uLocation();
        byte[] bytes;

        if (playlist.Kind == PlaylistSourceKind.LocalFile)
        {
            if (!File.Exists(location))
                throw new FileNotFoundException($"Файл плейлиста не найден: {location}", location);

            bytes = await File.ReadAllBytesAsync(location, ct).ConfigureAwait(false);
        }
        else
        {
            _log.LogInformation("Загрузка плейлиста {Name} с {Url}", playlist.Name, location);
            using var response = await _http
                .GetAsync(location, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }

        var doc = M3uParser.ParseBytes(bytes);
        if (doc.Channels.Count == 0)
            throw new InvalidDataException("Плейлист не содержит каналов.");

        await File.WriteAllBytesAsync(CachePath(playlist.Id), bytes, ct).ConfigureAwait(false);

        var content = Build(playlist, doc, fromCache: false);

        var updated = playlist with
        {
            LastUpdatedAt = DateTimeOffset.Now,
            ChannelCount = content.Channels.Count,
            Groups = content.Groups,
            EpgUrl = string.IsNullOrWhiteSpace(playlist.EpgUrl) ? doc.EpgUrl : playlist.EpgUrl,
        };
        await UpdateAsync(updated, ct).ConfigureAwait(false);

        return content with { Playlist = updated };
    }

    private static PlaylistContent Build(Playlist playlist, M3uDocument doc, bool fromCache) => new()
    {
        Playlist = playlist,
        Channels = doc.Channels,
        EpgUrl = string.IsNullOrWhiteSpace(playlist.EpgUrl) ? doc.EpgUrl : playlist.EpgUrl,
        FromCache = fromCache,
    };

    /// <summary>Быстрая проверка доступности источника — для индикатора Online/Offline.</summary>
    public async Task<bool> CheckAvailabilityAsync(Playlist playlist, CancellationToken ct = default)
    {
        try
        {
            if (playlist.Kind == PlaylistSourceKind.LocalFile)
                return File.Exists(playlist.Source);

            using var request = new HttpRequestMessage(HttpMethod.Head, playlist.ResolveM3uLocation());
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode is System.Net.HttpStatusCode.MethodNotAllowed
                or System.Net.HttpStatusCode.NotImplemented)
            {
                // некоторые сервера не умеют HEAD — пробуем частичный GET
                using var get = await _http
                    .GetAsync(playlist.ResolveM3uLocation(), HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                return get.IsSuccessStatusCode;
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return false;
        }
    }

    /// <summary>Проверка udpxy по адресу /status.</summary>
    public async Task<bool> CheckUdpxyAsync(string udpxyBaseUrl, CancellationToken ct = default)
    {
        try
        {
            var url = udpxyBaseUrl.TrimEnd('/') + "/status";
            using var response = await _http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return false;
        }
    }

    /// <summary>Устойчивый идентификатор для источника — чтобы не плодить дубли плейлистов.</summary>
    public static string HashSource(string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
