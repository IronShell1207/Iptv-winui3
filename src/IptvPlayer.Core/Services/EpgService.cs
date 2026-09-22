using IptvPlayer.Core.Models;
using IptvPlayer.Core.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IptvPlayer.Core.Services;

/// <summary>
/// Загружает XMLTV и отвечает на вопросы «что идёт сейчас» и «что дальше».
/// Передачи держатся в памяти, сгруппированные по channel id и отсортированные по началу.
/// </summary>
public sealed class EpgService
{
    private const string CacheFolder = "epg";

    private readonly JsonStore _store;
    private readonly HttpClient _http;
    private readonly ILogger<EpgService> _log;

    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private Dictionary<string, EpgProgramme[]> _byChannel = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, EpgChannel> _channels = new(StringComparer.OrdinalIgnoreCase);

    public EpgService(JsonStore store, HttpClient http, ILogger<EpgService>? log = null)
    {
        _store = store;
        _http = http;
        _log = log ?? NullLogger<EpgService>.Instance;
        Directory.CreateDirectory(Path.Combine(_store.RootFolder, CacheFolder));
    }

    public bool HasData => _byChannel.Count > 0;
    public DateTimeOffset? LoadedAt { get; private set; }
    public int ProgrammeCount { get; private set; }

    public event EventHandler? Updated;

    private string CachePath(string playlistId)
        => Path.Combine(_store.RootFolder, CacheFolder, playlistId + ".xml");

    /// <summary>
    /// Грузит EPG: свежий кэш используется как есть, иначе скачивает.
    /// Возвращает false, если данных получить не удалось (не ошибка для UI).
    /// </summary>
    /// <remarks>
    /// Загрузки сериализованы: плейлист показывается из кэша и следом обновляется
    /// из сети, поэтому вызовы приходят парами. Второй дожидается первого и почти
    /// всегда обходится уже скачанным файлом.
    /// </remarks>
    public async Task<bool> LoadAsync(
        string playlistId, string? epgUrl, TimeSpan cacheLifetime, CancellationToken ct = default)
    {
        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(playlistId, epgUrl, cacheLifetime, ct).ConfigureAwait(false);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task<bool> LoadCoreAsync(
        string playlistId, string? epgUrl, TimeSpan cacheLifetime, CancellationToken ct)
    {
        var cache = CachePath(playlistId);

        if (File.Exists(cache) &&
            DateTimeOffset.Now - File.GetLastWriteTime(cache) < cacheLifetime)
        {
            if (await TryLoadFileAsync(cache, ct).ConfigureAwait(false))
                return true;
        }

        if (string.IsNullOrWhiteSpace(epgUrl))
            return File.Exists(cache) && await TryLoadFileAsync(cache, ct).ConfigureAwait(false);

        try
        {
            _log.LogInformation("Загрузка EPG с {Url}", epgUrl);
            using var response = await _http
                .GetAsync(epgUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var temp = $"{cache}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var network = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var file = File.Create(temp))
                {
                    await network.CopyToAsync(file, ct).ConfigureAwait(false);
                }

                File.Move(temp, cache, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); } catch (IOException) { /* остатки не критичны */ }
                }
            }

            return await TryLoadFileAsync(cache, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _log.LogWarning(ex, "EPG недоступен, пробуем кэш");
            return File.Exists(cache) && await TryLoadFileAsync(cache, ct).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryLoadFileAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var doc = await XmltvParser.ParseAsync(stream, ct).ConfigureAwait(false);
            Apply(doc);
            return doc.Programmes.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or InvalidDataException)
        {
            _log.LogWarning(ex, "Не удалось разобрать EPG {Path}", path);
            return false;
        }
    }

    public void Apply(XmltvDocument doc)
    {
        _byChannel = doc.Programmes
            .GroupBy(p => p.ChannelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(p => p.Start).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        _channels = doc.Channels
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        ProgrammeCount = doc.Programmes.Count;
        LoadedAt = DateTimeOffset.Now;
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<EpgProgramme> ForChannel(string? tvgId)
        => tvgId is not null && _byChannel.TryGetValue(tvgId, out var list)
            ? list
            : Array.Empty<EpgProgramme>();

    /// <summary>
    /// Идущая сейчас передача. На стыке передач выбирается та, у которой
    /// Start &lt;= now &lt; Stop — конец передачи принадлежит следующей.
    /// </summary>
    public EpgProgramme? NowOn(string? tvgId, DateTimeOffset now)
    {
        var list = ForChannel(tvgId);
        for (var i = 0; i < list.Count; i++)
        {
            var p = list[i];
            if (p.Start <= now && now < p.Stop) return p;
            if (p.Start > now) break;
        }
        return null;
    }

    public EpgProgramme? NextOn(string? tvgId, DateTimeOffset now)
    {
        var list = ForChannel(tvgId);
        foreach (var p in list)
        {
            if (p.Start > now) return p;
        }
        return null;
    }

    /// <summary>Сетка передач за сутки от указанного момента.</summary>
    public IReadOnlyList<EpgProgramme> DaySchedule(string? tvgId, DateTimeOffset dayStart)
    {
        var dayEnd = dayStart.AddDays(1);
        return ForChannel(tvgId)
            .Where(p => p.Stop > dayStart && p.Start < dayEnd)
            .ToList();
    }

    public EpgChannel? Describe(string? tvgId)
        => tvgId is not null && _channels.TryGetValue(tvgId, out var c) ? c : null;
}
