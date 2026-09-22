namespace IptvPlayer.Core.Models;

public enum PlaylistSourceKind
{
    /// <summary>M3U/M3U8 по HTTP(S).</summary>
    M3uUrl,
    /// <summary>Локальный файл на диске.</summary>
    LocalFile,
    /// <summary>Xtream Codes: сервер + логин + пароль (плейлист берётся через get.php).</summary>
    Xtream,
}

/// <summary>Описание плейлиста, сохраняемое в playlists.json.</summary>
public sealed record Playlist
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public PlaylistSourceKind Kind { get; init; } = PlaylistSourceKind.M3uUrl;

    /// <summary>URL M3U (для M3uUrl) или путь к файлу (для LocalFile) или адрес сервера (для Xtream).</summary>
    public required string Source { get; init; }

    public string? Username { get; init; }
    public string? Password { get; init; }

    /// <summary>URL XMLTV. Если пусто — берётся url-tvg из заголовка плейлиста.</summary>
    public string? EpgUrl { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? LastUpdatedAt { get; init; }
    public int ChannelCount { get; init; }
    public IReadOnlyList<string> Groups { get; init; } = Array.Empty<string>();

    /// <summary>Для Xtream формирует URL get.php, для остальных возвращает Source.</summary>
    public string ResolveM3uLocation()
    {
        if (Kind != PlaylistSourceKind.Xtream) return Source;

        var baseUrl = Source.TrimEnd('/');
        if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            baseUrl = "http://" + baseUrl;

        return $"{baseUrl}/get.php?username={Uri.EscapeDataString(Username ?? string.Empty)}" +
               $"&password={Uri.EscapeDataString(Password ?? string.Empty)}&type=m3u_plus&output=ts";
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];
}
