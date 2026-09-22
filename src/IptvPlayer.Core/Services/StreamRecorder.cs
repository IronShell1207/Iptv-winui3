using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IptvPlayer.Core.Services;

/// <summary>
/// Пишет живой MPEG-TS в файл как есть, без перекодирования: поток от udpxy
/// уже готовый транспортный поток, его достаточно сохранить побайтно.
/// </summary>
public sealed class StreamRecorder
{
    private const int BufferSize = 256 * 1024;

    private readonly HttpClient _http;
    private readonly ILogger _log;

    public StreamRecorder(HttpClient http, ILogger? log = null)
    {
        _http = http;
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>
    /// Качает поток в файл, пока не отменят. Возвращает записанный объём.
    /// Прогресс отдаётся не чаще раза в секунду, чтобы не дёргать интерфейс.
    /// </summary>
    public async Task<long> RecordAsync(
        string url,
        string path,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // без таймаута: запись живёт часами, а HttpClient.Timeout распространяется на всё чтение
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        long total = 0;
        var lastReport = DateTimeOffset.UtcNow;

        while (true)
        {
            int read;
            try
            {
                read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;      // остановили запись — файл остаётся годным
            }

            if (read <= 0) break;

            await target.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None).ConfigureAwait(false);
            total += read;

            if (progress is not null && DateTimeOffset.UtcNow - lastReport > TimeSpan.FromSeconds(1))
            {
                lastReport = DateTimeOffset.UtcNow;
                progress.Report(total);
            }
        }

        await target.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        progress?.Report(total);

        _log.LogInformation("Записано {Bytes} байт в {Path}", total, path);
        return total;
    }

    /// <summary>Превращает название канала и передачи в пригодное имя файла.</summary>
    public static string BuildFileName(DateTimeOffset startedAt, string channelName, string? programmeTitle)
    {
        var name = string.IsNullOrWhiteSpace(programmeTitle)
            ? channelName
            : $"{channelName} - {programmeTitle}";

        var clean = Sanitize(name);
        if (clean.Length > 90) clean = clean[..90].TrimEnd();

        return $"{startedAt:yyyy-MM-dd HH-mm} {clean}.ts";
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(c => invalid.Contains(c) ? ' ' : c)
            .ToArray();

        var result = new string(chars).Trim();
        while (result.Contains("  ", StringComparison.Ordinal))
            result = result.Replace("  ", " ", StringComparison.Ordinal);

        return result.Length == 0 ? "Запись" : result;
    }
}
