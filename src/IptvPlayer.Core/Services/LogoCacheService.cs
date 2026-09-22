using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IptvPlayer.Core.Services;

/// <summary>
/// Дисковый кэш логотипов каналов. Возвращает локальный путь к файлу,
/// который UI отдаёт в BitmapImage — так список не дёргает сеть при прокрутке.
/// </summary>
public sealed class LogoCacheService
{
    private const string CacheFolder = "logos";
    private const long MaxLogoBytes = 2 * 1024 * 1024;

    private readonly string _folder;
    private readonly HttpClient _http;
    private readonly ILogger<LogoCacheService> _log;
    private readonly SemaphoreSlim _downloads = new(4, 4);
    private readonly Dictionary<string, Task<string?>> _inFlight = new(StringComparer.Ordinal);

    public LogoCacheService(JsonStore store, HttpClient http, ILogger<LogoCacheService>? log = null)
    {
        _folder = Path.Combine(store.RootFolder, CacheFolder);
        _http = http;
        _log = log ?? NullLogger<LogoCacheService>.Instance;
        Directory.CreateDirectory(_folder);
    }

    /// <summary>Локальный путь, если логотип уже скачан.</summary>
    public string? TryGetCached(string logoUrl)
    {
        var path = PathFor(logoUrl);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Возвращает путь к файлу логотипа, скачивая его при необходимости.</summary>
    public Task<string?> GetAsync(string logoUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return Task.FromResult<string?>(null);

        var cached = TryGetCached(logoUrl);
        if (cached is not null) return Task.FromResult<string?>(cached);

        lock (_inFlight)
        {
            if (_inFlight.TryGetValue(logoUrl, out var running)) return running;

            var task = DownloadAsync(logoUrl, ct);
            _inFlight[logoUrl] = task;
            return task;
        }
    }

    private async Task<string?> DownloadAsync(string logoUrl, CancellationToken ct)
    {
        try
        {
            if (!Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
                return null;

            await _downloads.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var response = await _http
                    .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode) return null;
                if (response.Content.Headers.ContentLength > MaxLogoBytes) return null;

                var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length == 0 || bytes.Length > MaxLogoBytes) return null;

                var path = PathFor(logoUrl);
                var temp = path + ".tmp";
                await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
                File.Move(temp, path, overwrite: true);
                return path;
            }
            finally
            {
                _downloads.Release();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _log.LogDebug(ex, "Логотип не скачан: {Url}", logoUrl);
            return null;
        }
        finally
        {
            lock (_inFlight) _inFlight.Remove(logoUrl);
        }
    }

    private string PathFor(string logoUrl)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(logoUrl));
        var name = Convert.ToHexString(hash)[..20].ToLowerInvariant();
        var ext = Path.GetExtension(new Uri(logoUrl, UriKind.RelativeOrAbsolute).IsAbsoluteUri
            ? new Uri(logoUrl).AbsolutePath
            : logoUrl);

        if (ext.Length is 0 or > 5) ext = ".img";
        return Path.Combine(_folder, name + ext);
    }

    /// <summary>Чистит кэш логотипов целиком (кнопка в настройках).</summary>
    public void Clear()
    {
        foreach (var file in Directory.EnumerateFiles(_folder))
        {
            try { File.Delete(file); }
            catch (IOException ex) { _log.LogDebug(ex, "Файл кэша занят: {File}", file); }
        }
    }
}
